# Windows PowerShell 5.1, no additional modules or downloads.
$ErrorActionPreference = 'Stop'
$report = New-Object 'System.Collections.Generic.List[string]'
$script:bundleErrors = 0
$script:stationWarnings = 0
function Result($level, $message) {
    $line = "[$level] $message"
    $report.Add($line)
    $color = switch ($level) { 'OK' { 'Green' }; 'ERROR' { 'Red' }; 'WARN' { 'Yellow' }; default { 'Gray' } }
    Write-Host $line -ForegroundColor $color
    if ($level -eq 'ERROR') { $script:bundleErrors++ }
    if ($level -eq 'WARN') { $script:stationWarnings++ }
}

try {
    Result 'INFO' ('Проверка TFortisTestBuilder — ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
    if ([Environment]::Is64BitOperatingSystem -and [Environment]::Is64BitProcess) {
        Result 'OK' 'Windows x64 / PowerShell x64'
    } else {
        Result 'ERROR' 'Нужны Windows 10/11 x64 и 64-разрядная PowerShell.'
    }
    $os = Get-CimInstance Win32_OperatingSystem
    if ($os.ProductType -eq 1 -and [version]$os.Version -ge [version]'10.0') {
        Result 'OK' ($os.Caption + ' / ' + $os.Version)
    } else {
        Result 'WARN' ('Система не входит в целевые Windows 10/11: ' + $os.Caption)
    }

    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'release-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Result 'INFO' ("Версия $($manifest.version); исходники $($manifest.sourceCommit)")
    Write-Host 'Проверка SHA-256 файлов комплекта…'
    foreach ($file in $manifest.files) {
        $path = Join-Path $PSScriptRoot $file.path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            Result 'ERROR' ("Нет файла: $($file.path)")
            continue
        }
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ($hash -ne $file.sha256) {
            # These files are intentionally editable in the application.
            if ($file.path -eq 'testbuilder.settings' -or $file.path.StartsWith('profiles/')) {
                Result 'WARN' ("Локально изменён профиль/настройки: $($file.path)")
            } else {
                Result 'ERROR' ("Файл отличается от релиза: $($file.path)")
            }
        }
    }
    if ($script:bundleErrors -eq 0) {
        Result 'OK' ("Комплект проверен: $($manifest.files.Count) файлов; .NET и браузер включены.")
    }

    $npcap = Get-Service -Name npcap -ErrorAction SilentlyContinue
    $pcapDll = Join-Path $env:SystemRoot 'System32\Npcap\wpcap.dll'
    if ($null -ne $npcap -and (Test-Path -LiteralPath $pcapDll)) {
        if ($npcap.Status -eq 'Running') {
            Result 'OK' 'Npcap: системный драйвер запущен, библиотека x64 найдена.'
        } else {
            Result 'WARN' ("Npcap установлен, состояние службы: $($npcap.Status). Проверьте перед DataTest.")
        }
    } else {
        Result 'WARN' 'Npcap не найден. DataTest требует отдельной установки: https://npcap.com/#download'
    }

    $ports = @([System.IO.Ports.SerialPort]::GetPortNames())
    if ($ports.Count -gt 0) { Result 'OK' ('COM-порты: ' + ($ports -join ', ')) }
    else { Result 'WARN' 'Нет COM-портов: подключите USB–RS-485 и установите драйвер его производителя.' }

    $printers = @(Get-CimInstance Win32_Printer)
    if (@($printers | Where-Object { $_.Name -eq 'TSC TE310' }).Count -gt 0) {
        Result 'OK' 'Очередь принтера TSC TE310 найдена; печать не выполнялась.'
    } else {
        Result 'WARN' 'Очередь TSC TE310 не найдена. Установите принтер или измените имя в профиле.'
    }

    foreach ($firmware in $manifest.externalFirmware) {
        if (Test-Path -LiteralPath $firmware.path -PathType Leaf) {
            Result 'OK' ("Файл прошивки найден: $($firmware.path) (содержимое не проверено)")
        } else {
            Result 'WARN' ("Нет прошивки для $($firmware.profile): $($firmware.path). Нужна, только если шаг обновляет DUT.")
        }
    }

    $settings = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'testbuilder.settings') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($settings.standId -eq '123' -or [string]::IsNullOrWhiteSpace($settings.standId)) {
        Result 'WARN' 'Stand ID оставлен заводским (123). Укажите фактический номер стенда в настройках.'
    }
    Result 'INFO' 'Сетевые карты компьютера (привязка DataTest выполняется в приложении):'
    foreach ($adapter in @(Get-NetAdapter -Physical)) {
        Result 'INFO' ("$($adapter.Name): $($adapter.InterfaceDescription), $($adapter.Status), $($adapter.LinkSpeed)")
    }
    Result 'INFO' 'Проверка не включает оборудование, не меняет сеть, не печатает и не прошивает DUT.'
    Result 'INFO' 'Для производственных операций нужны настройка карт, действующая сессия лаунчера и доступ к серверу.'
    Result 'INFO' ("Ошибок комплекта: $script:bundleErrors; замечаний: $script:stationWarnings")
} catch {
    Result 'ERROR' ('Проверка не завершена: ' + $_.Exception.Message)
} finally {
    $report | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'installation-check.txt') -Encoding UTF8
}
if ($script:bundleErrors -gt 0) { exit 1 }
if ($script:stationWarnings -gt 0) { exit 2 }
exit 0
