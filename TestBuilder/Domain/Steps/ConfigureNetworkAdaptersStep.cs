using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps;

/// <summary>
/// Assigns one static IPv4 address to every configured Windows network adapter.
/// Selectors are an interface alias (Name=Ethernet 0), MAC address
/// (MAC=001122334455), or Auto=Switch. MAC selectors are preferred because
/// aliases can change. Adapters with an IPv4 default gateway are always protected.
/// </summary>
public sealed class ConfigureNetworkAdaptersStep : ITestStep
{
    private readonly ILogger _logger;
    private readonly IReadOnlyList<NetworkAdapterConfiguration> _configurations;
    private readonly string _outputVariableName;
    private readonly bool _failOnError;

    public ConfigureNetworkAdaptersStep(
        ILogger logger,
        IEnumerable<NetworkAdapterConfiguration> configurations,
        string outputVariableName,
        bool failOnError)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configurations = configurations?.ToList() ?? throw new ArgumentNullException(nameof(configurations));
        _outputVariableName = string.IsNullOrWhiteSpace(outputVariableName) ? "NetworkConfig" : outputVariableName.Trim();
        _failOnError = failOnError;
    }

    public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
            return Finish(context, false, "Настройка сетевых карт поддерживается только в Windows.");

        if (_configurations.Count == 0)
            return Finish(context, false, "Не задана ни одна сетевая карта. Заполните строки формата Name=Ethernet 0;192.168.10.1;24.");

        if (!IsAdministrator())
            return Finish(context, false, "Для настройки сетевых карт запустите TestBuilder от имени администратора.");

        _logger.Info($"[ШАГ] Настройка {_configurations.Count} сетевых карт.");
        var errors = new List<string>();
        var switchCandidates = GetSwitchCandidates();
        var claimedAutoAdapters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        context.SetVariable(
            $"{_outputVariableName}.AvailableSwitchAdapters",
            string.Join("; ", switchCandidates.Select(adapter => $"{adapter.Name} ({FormatMac(adapter.GetPhysicalAddress())})")));

        for (var index = 0; index < _configurations.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await context.WaitWhilePausedAsync(cancellationToken);

            var configuration = _configurations[index];
            var prefix = $"{_outputVariableName}.Adapter{index}";
            context.SetVariable($"{prefix}.Selector", configuration.Selector);
            context.SetVariable($"{prefix}.Address", configuration.Address);
            context.SetVariable($"{prefix}.PrefixLength", configuration.PrefixLength);

            if (!TryValidate(configuration, out var address, out var error))
            {
                WriteFailure(context, prefix, error);
                errors.Add(error);
                continue;
            }

            var adapter = FindAdapter(configuration.Selector, switchCandidates, claimedAutoAdapters);
            if (adapter == null)
            {
                error = $"Не найдена сетевая карта '{configuration.Selector}'. Доступные карты коммутатора: {context.GetVariable<string>($"{_outputVariableName}.AvailableSwitchAdapters")}";
                WriteFailure(context, prefix, error);
                errors.Add(error);
                continue;
            }

            if (HasDefaultIpv4Gateway(adapter))
            {
                error = $"Карта '{adapter.Name}' имеет IPv4-шлюз по умолчанию и защищена от изменения. Это, вероятно, интернет-подключение.";
                WriteFailure(context, prefix, error);
                errors.Add(error);
                continue;
            }

            if (IsAutoSwitchSelector(configuration.Selector))
                claimedAutoAdapters.Add(adapter.Id);

            var result = await SetStaticAddressAsync(adapter.Name, address!, configuration.PrefixLength, cancellationToken);
            context.SetVariable($"{prefix}.AdapterName", adapter.Name);
            context.SetVariable($"{prefix}.MacAddress", FormatMac(adapter.GetPhysicalAddress()));
            context.SetVariable($"{prefix}.ExitCode", result.ExitCode);
            context.SetVariable($"{prefix}.StdOut", result.StdOut);
            context.SetVariable($"{prefix}.StdErr", result.StdErr);
            context.SetVariable($"{prefix}.Success", result.ExitCode == 0);

            if (result.ExitCode == 0)
            {
                _logger.Info($"[OK] {adapter.Name}: {address}/{configuration.PrefixLength}.");
                continue;
            }

            error = $"Не удалось настроить '{adapter.Name}' ({result.StdErr})";
            _logger.Warning($"[ОШИБКА] {error}");
            errors.Add(error);
        }

        return Finish(context, errors.Count == 0, string.Join("; ", errors));
    }

    public static IReadOnlyList<NetworkAdapterConfiguration> ParseConfigurations(string? configurationText)
    {
        var result = new List<NetworkAdapterConfiguration>();
        if (string.IsNullOrWhiteSpace(configurationText))
            return result;

        var lines = configurationText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            if (line.StartsWith('#'))
                continue;

            var values = line.Split(';', StringSplitOptions.TrimEntries);
            if (values.Length != 3 || !int.TryParse(values[2], out var prefixLength))
            {
                result.Add(new NetworkAdapterConfiguration(line, string.Empty, 0));
                continue;
            }

            result.Add(new NetworkAdapterConfiguration(values[0], values[1], prefixLength));
        }

        return result;
    }

    private static bool TryValidate(NetworkAdapterConfiguration configuration, out IPAddress? address, out string error)
    {
        address = null;
        if (string.IsNullOrWhiteSpace(configuration.Selector))
        {
            error = "У сетевой карты не задан селектор.";
            return false;
        }

        if (!IPAddress.TryParse(configuration.Address, out address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            error = $"Некорректный IPv4-адрес '{configuration.Address}' для '{configuration.Selector}'.";
            return false;
        }

        if (configuration.PrefixLength is < 1 or > 30)
        {
            error = $"Некорректная длина маски '{configuration.PrefixLength}' для '{configuration.Selector}'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static NetworkInterface? FindAdapter(
        string selector,
        IReadOnlyList<NetworkInterface> switchCandidates,
        ISet<string> claimedAutoAdapters)
    {
        var normalized = selector.Trim();
        if (IsAutoSwitchSelector(normalized))
            return switchCandidates.FirstOrDefault(adapter => !claimedAutoAdapters.Contains(adapter.Id));

        var allAdapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface => networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback);

        if (normalized.StartsWith("MAC=", StringComparison.OrdinalIgnoreCase))
        {
            var expectedMac = NormalizeMac(normalized[4..]);
            return allAdapters.FirstOrDefault(adapter =>
                string.Equals(NormalizeMac(adapter.GetPhysicalAddress().ToString()), expectedMac, StringComparison.Ordinal));
        }

        var alias = normalized.StartsWith("Name=", StringComparison.OrdinalIgnoreCase) ? normalized[5..].Trim() : normalized;
        return allAdapters.FirstOrDefault(adapter => string.Equals(adapter.Name, alias, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<NetworkInterface> GetSwitchCandidates()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
            .Where(adapter => !HasDefaultIpv4Gateway(adapter))
            .OrderBy(GetInterfaceIndex)
            .ThenBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int GetInterfaceIndex(NetworkInterface adapter)
    {
        try
        {
            return adapter.GetIPProperties().GetIPv4Properties().Index;
        }
        catch
        {
            return int.MaxValue;
        }
    }

    private static bool HasDefaultIpv4Gateway(NetworkInterface adapter)
    {
        try
        {
            return adapter.GetIPProperties().GatewayAddresses.Any(gateway =>
                gateway.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                !gateway.Address.Equals(IPAddress.Any));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAutoSwitchSelector(string selector) =>
        string.Equals(selector.Trim(), "Auto=Switch", StringComparison.OrdinalIgnoreCase);

    private static async Task<ProcessRunResult> SetStaticAddressAsync(string adapterName, IPAddress address, int prefixLength, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("interface");
            startInfo.ArgumentList.Add("ipv4");
            startInfo.ArgumentList.Add("set");
            startInfo.ArgumentList.Add("address");
            startInfo.ArgumentList.Add($"name={adapterName}");
            startInfo.ArgumentList.Add("source=static");
            startInfo.ArgumentList.Add($"address={address}");
            startInfo.ArgumentList.Add($"mask={PrefixLengthToMask(prefixLength)}");
            startInfo.ArgumentList.Add("gateway=none");
            startInfo.ArgumentList.Add("store=persistent");

            using var process = Process.Start(startInfo);
            if (process == null)
                return new ProcessRunResult(-1, string.Empty, "Не удалось запустить netsh.exe.");

            await process.WaitForExitAsync(cancellationToken);
            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            return new ProcessRunResult(process.ExitCode, stdout.Trim(), stderr.Trim());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ProcessRunResult(-1, string.Empty, ex.Message);
        }
    }

    private StepResult Finish(TestContext context, bool success, string error)
    {
        context.SetVariable($"{_outputVariableName}.Passed", success);
        context.SetVariable($"{_outputVariableName}.Error", error);
        if (success)
        {
            _logger.Info("[OK] Сетевые карты настроены.");
            return StepResult.True;
        }

        _logger.Warning($"[ОШИБКА] Настройка сетевых карт не выполнена: {error}");
        return _failOnError ? StepResult.False : StepResult.True;
    }

    private static void WriteFailure(TestContext context, string prefix, string error)
    {
        context.SetVariable($"{prefix}.Success", false);
        context.SetVariable($"{prefix}.Error", error);
    }

    private static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string PrefixLengthToMask(int prefixLength)
    {
        var mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
        return string.Join('.', new[]
        {
            (mask >> 24) & 0xff,
            (mask >> 16) & 0xff,
            (mask >> 8) & 0xff,
            mask & 0xff
        });
    }

    private static string NormalizeMac(string value) => new string(value.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();

    private static string FormatMac(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 0 ? string.Empty : string.Join(':', bytes.Select(value => value.ToString("X2")));
    }

    private sealed record ProcessRunResult(int ExitCode, string StdOut, string StdErr);
}

public sealed record NetworkAdapterConfiguration(string Selector, string Address, int PrefixLength);
