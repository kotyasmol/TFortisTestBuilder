# Windows PowerShell 5.1. Read-only collection; no driver or network changes.
[CmdletBinding()]
param([string]$OutputDirectory = $PSScriptRoot, [switch]$PassThru)
$ErrorActionPreference = 'Stop'
$warnings = New-Object 'System.Collections.Generic.List[string]'
$lines = New-Object 'System.Collections.Generic.List[string]'
$result = [ordered]@{ schemaVersion = 1; computer = $env:COMPUTERNAME; collectedAt = (Get-Date).ToString('o');
    collectionError = $null; capture = $null; adapters = @(); warnings = @() }
function Note([string]$text) { $lines.Add($text); Write-Host $text }
function Warn([string]$text) { $warnings.Add($text); Note ('[WARN] ' + $text) }
function Same-Version($first, $second) {
    try {
        $a = [regex]::Match([string]$first, '\d+(?:\.\d+){1,3}').Value
        $b = [regex]::Match([string]$second, '\d+(?:\.\d+){1,3}').Value
        if (-not $a -or -not $b) { return $false }
        $av = [version]$a; $bv = [version]$b
        return $av.Major -eq $bv.Major -and $av.Minor -eq $bv.Minor -and
            [Math]::Max(0, $av.Build) -eq [Math]::Max(0, $bv.Build) -and
            [Math]::Max(0, $av.Revision) -eq [Math]::Max(0, $bv.Revision)
    } catch { return $false }
}
try {
    $policyPath = Join-Path $PSScriptRoot 'reference\network\workstation-baseline.json'
    $policy = Get-Content -LiteralPath $policyPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($policy.schemaVersion -ne 1) { throw 'Не поддерживается версия справочника драйверов.' }
    $result.baselineSha256 = (Get-FileHash -LiteralPath $policyPath -Algorithm SHA256).Hash
    Note ('Компьютер: ' + $env:COMPUTERNAME + '. Сравнение с эталоном от ' + $policy.updated)
    $settingsPath = Join-Path $PSScriptRoot 'testbuilder.settings'
    $settings = $null
    if (Test-Path -LiteralPath $settingsPath) {
        $settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    $boundIds = @($settings.dataTestAdapterIds.PSObject.Properties | Where-Object { $_.Value } |
        ForEach-Object { ([string]$_.Value).Trim('{}').ToLowerInvariant() })
    $presets = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'reference\network\presets.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $planIps = @($settings.networkPlan.addresses.ip) + @($presets.presets.plan.addresses.ip)
    $dlls = @()
    foreach ($relative in @('System32\wpcap.dll', 'System32\Packet.dll', 'System32\Npcap\wpcap.dll', 'System32\Npcap\Packet.dll')) {
        $path = Join-Path $env:SystemRoot $relative
        if (Test-Path -LiteralPath $path) {
            $version = (Get-Item -LiteralPath $path).VersionInfo
            $dlls += [pscustomobject]@{ path = $path; version = $version.FileVersion; product = $version.ProductName;
                isNpcap = (($version.ProductName + ' ' + $version.FileDescription) -match '\bNpcap\b');
                sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
        }
    }
    $npcap = Get-Service -Name npcap -ErrorAction SilentlyContinue
    $uninstall = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\NpcapInst',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\NpcapInst' -ErrorAction SilentlyContinue |
        Select-Object -First 1
    $result.capture = [pscustomobject]@{ installedVersion = $uninstall.DisplayVersion; targetVersion = $policy.capture.version;
        serviceStatus = [string]$npcap.Status; libraries = $dlls }
    if ($null -eq $npcap -or -not (Same-Version $uninstall.DisplayVersion $policy.capture.version)) {
        Warn ('Нужен Npcap ' + $policy.capture.version + '; найдено: ' + $uninstall.DisplayVersion + '. ' + $policy.capture.downloadUrl)
    }
    if ($null -ne $npcap -and $npcap.Status -ne 'Running') { Warn ('Служба Npcap: ' + $npcap.Status) }
    foreach ($filename in @('wpcap.dll', 'Packet.dll')) {
        $compat = @($dlls | Where-Object { $_.path -eq (Join-Path $env:SystemRoot ('System32\' + $filename)) -and $_.isNpcap -and (Same-Version $_.version $policy.capture.version) })
        if ($compat.Count -ne 1) { Warn ('Системная ' + $filename + ' не подтверждена как Npcap ' + $policy.capture.version + '. Проверьте режим совместимости WinPcap; загруженную библиотеку показывает «Сеть → Проверить компьютер».') }
    }
    $nics = @(Get-CimInstance Win32_NetworkAdapter -Filter 'PhysicalAdapter = TRUE')
    $drivers = @(Get-CimInstance Win32_PnPSignedDriver -Filter "DeviceClass = 'NET'")
    $ips = @(Get-NetIPAddress -AddressFamily IPv4)
    $gatewayIndexes = @(Get-NetRoute -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue | ForEach-Object { $_.InterfaceIndex })
    $driverFiles = @{}
    foreach ($adapter in @(Get-NetAdapter -Physical)) {
        $id = ([string]$adapter.InterfaceGuid).Trim('{}').ToLowerInvariant()
        $nic = $nics | Where-Object { ([string]$_.GUID).Trim('{}').ToLowerInvariant() -eq $id } | Select-Object -First 1
        $driver = $drivers | Where-Object { $_.DeviceID -eq $nic.PNPDeviceID } | Select-Object -First 1
        $addresses = @($ips | Where-Object { $_.InterfaceIndex -eq $adapter.ifIndex } | ForEach-Object { $_.IPAddress })
        $hasGateway = $gatewayIndexes -contains $adapter.ifIndex
        $isTest = if ($boundIds.Count -gt 0) { $boundIds -contains $id } else {
            -not $hasGateway -and @($addresses | Where-Object { $planIps -contains $_ }).Count -gt 0
        }
        $file = $null
        if ($nic.ServiceName) {
            $service = Get-ItemProperty -LiteralPath ('HKLM:\SYSTEM\CurrentControlSet\Services\' + $nic.ServiceName) -ErrorAction SilentlyContinue
            if ($service.ImagePath) {
                $file = [Environment]::ExpandEnvironmentVariables(([string]$service.ImagePath).Trim('"'))
                if ($file.StartsWith('\??\')) { $file = $file.Substring(4) }
                if ($file.StartsWith('\SystemRoot\', [StringComparison]::OrdinalIgnoreCase)) { $file = Join-Path $env:SystemRoot $file.Substring(12) }
                elseif ($file.StartsWith('System32\', [StringComparison]::OrdinalIgnoreCase)) { $file = Join-Path $env:SystemRoot $file }
            }
        }
        $actual = $null
        if ($file -and (Test-Path -LiteralPath $file -PathType Leaf)) {
            if (-not $driverFiles.ContainsKey($file)) {
                $driverFiles[$file] = [pscustomobject]@{ path = $file; version = (Get-Item -LiteralPath $file).VersionInfo.FileVersion;
                    sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash }
            }
            $actual = $driverFiles[$file]
        }
        $baseline = @($policy.networkDrivers | Where-Object {
            $matchesHardware = $false
            foreach ($hardwareId in $_.hardwareIds) {
                if ([string]$nic.PNPDeviceID -eq $hardwareId -or ([string]$nic.PNPDeviceID).StartsWith($hardwareId + '\', [StringComparison]::OrdinalIgnoreCase)) { $matchesHardware = $true }
            }
            $matchesHardware
        })
        $entry = [pscustomobject]@{ name = $adapter.Name; description = $adapter.InterfaceDescription; id = $id;
            ipv4 = $addresses; hasGateway = $hasGateway; testAdapter = $isTest; status = [string]$adapter.Status;
            linkSpeed = [string]$adapter.LinkSpeed; hardwareId = $nic.PNPDeviceID; provider = $driver.DriverProviderName;
            inf = $driver.InfName; driverVersion = $driver.DriverVersion; signed = $driver.IsSigned; driverFile = $actual;
            baselineName = if ($baseline.Count -eq 1) { $baseline[0].name } else { $null } }
        $result.adapters += $entry
        Note ($entry.name + ': ' + $entry.description + '; IP ' + ($addresses -join ', ') + '; драйвер ' + $entry.driverVersion + '; линк ' + $entry.linkSpeed)
        if ($isTest) {
            if ($hasGateway) { Warn ($entry.name + ': карта используется шлюзом. Проверьте привязку до изменения драйверов.') }
            if ($baseline.Count -ne 1) { Warn ($entry.name + ': для этого Hardware ID пока нет единственного проверенного эталона. Нужна квалификация этой модели карты; это не заключение о её неисправности.') }
            elseif (-not (Same-Version $entry.driverVersion $baseline[0].version) -or $null -eq $actual -or $actual.sha256 -ne $baseline[0].driverFileSha256) {
                Warn ($entry.name + ': версия или файл драйвера отличается от проверенного эталона ' + $baseline[0].version + '. Не заменяйте драйвер без сверки Hardware ID.')
            }
            if ($null -eq $driver -or -not $driver.IsSigned) { Warn ($entry.name + ': подпись установленного пакета драйвера не подтверждена.') }
            if ($actual -and -not (Same-Version $entry.driverVersion $actual.version)) { Warn ($entry.name + ': версия пакета и фактического SYS расходятся: ' + $entry.driverVersion + ' / ' + $actual.version) }
        }
    }
    if (@($result.adapters | Where-Object { $_.testAdapter }).Count -eq 0) { Warn 'Тестовые карты не определены. Настройте привязки в разделе «Сеть» и повторите сбор сведений.' }
    Note 'Это проверка компонентов. Скорость и потери подтверждает полный DataTest; драйверы и адреса не изменялись.'
} catch {
    $result.collectionError = $_.Exception.Message
    Warn ('Сведения собраны не полностью: ' + $_.Exception.Message)
}
$result.warnings = @($warnings.ToArray())
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'network-driver-report.json') -Encoding UTF8
$lines | Set-Content -LiteralPath (Join-Path $OutputDirectory 'network-driver-report.txt') -Encoding UTF8
Note ('Отчёты: ' + $OutputDirectory + '\network-driver-report.json и .txt')
if ($PassThru) { return [pscustomobject]$result }
if ($result.collectionError) { exit 1 }
if ($warnings.Count -gt 0) { exit 2 }
exit 0
