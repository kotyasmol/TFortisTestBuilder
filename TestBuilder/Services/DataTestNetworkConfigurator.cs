using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Services;

public sealed record BenchAdapter(string Id, string Name, string Mac, string Status, string Ipv4,
    double SpeedMbps = 0)
{
    public string ChoiceDisplay => $"{Name} · {Mac}";
    public string LinkDisplay => Status.Equals("Up", StringComparison.OrdinalIgnoreCase)
        ? $"Подключена · {SpeedMbps:F0} Мбит/с" : $"Нет соединения ({Status})";
    public string Display => $"{Name}  |  {Mac}  |  {Status}  |  " +
        (SpeedMbps > 0 ? $"{SpeedMbps:F0} Mbps" : "скорость неизвестна") + $"  |  {Ipv4}";
}

public sealed record AdapterAssignment(string Ip, string AdapterId);

public static class DataTestNetworkConfigurator
{
    public static IReadOnlyList<string> BenchIps => AppSettings.Instance.NetworkPlan.Addresses.Select(a => a.Ip).ToArray();
    public static IReadOnlyList<BenchAdapter> GetAdapters() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(adapter => adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or
            NetworkInterfaceType.GigabitEthernet)
        .Select(adapter => new BenchAdapter(
            adapter.Id,
            adapter.Name,
            adapter.GetPhysicalAddress().ToString(),
            adapter.OperationalStatus.ToString(),
            string.Join(", ", adapter.GetIPProperties().UnicastAddresses
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => address.Address.ToString())),
            GetSpeedMbps(adapter)))
        .OrderBy(adapter => adapter.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    private static double GetSpeedMbps(NetworkInterface adapter)
    {
        try { return adapter.Speed > 0 ? adapter.Speed / 1_000_000.0 : 0; }
        catch { return 0; }
    }

    private static string TemporaryIpFor(string benchIp, BenchNetworkPlan plan) => plan.Addresses.Single(a => a.Ip == benchIp).TemporaryIp;

    public static string? ValidateAssignments(IReadOnlyList<AdapterAssignment> assignments,
        IReadOnlyList<BenchAdapter> adapters, BenchNetworkPlan? plan = null,
        IReadOnlyDictionary<string, string>? managedAddresses = null)
    {
        plan ??= AppSettings.Instance.NetworkPlan;
        managedAddresses ??= AppSettings.Instance.DataTestAdapterIds;
        try { plan.Validate(); } catch (InvalidOperationException ex) { return ex.Message; }
        var benchIps = plan.Addresses.Select(a => a.Ip).ToArray();
        if (assignments.Count != benchIps.Length ||
            !benchIps.SequenceEqual(assignments.Select(assignment => assignment.Ip)))
            return "Нужно выбрать адаптер для каждого адреса плана сети.";

        if (assignments.Any(assignment => string.IsNullOrWhiteSpace(assignment.AdapterId)))
            return "Для некоторых адресов адаптер не выбран.";

        if (assignments.Select(assignment => assignment.AdapterId)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != assignments.Count)
            return "Один адаптер выбран для нескольких адресов.";

        var availableIds = adapters.Select(adapter => adapter.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (assignments.Any(assignment => !availableIds.Contains(assignment.AdapterId)))
            return "Один из выбранных адаптеров больше не найден. Обновите список.";

        var selectedIds = assignments.Select(assignment => assignment.AdapterId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var adapter in adapters.Where(item => selectedIds.Contains(item.Id)))
        {
            var assignment = assignments.Single(item =>
                string.Equals(item.AdapterId, adapter.Id, StringComparison.OrdinalIgnoreCase));
            var otherIp = adapter.Ipv4.Split(", ").FirstOrDefault(ip =>
                !string.IsNullOrWhiteSpace(ip) && !benchIps.Contains(ip) &&
                !(managedAddresses?.TryGetValue(ip, out var owner) == true &&
                    string.Equals(owner, adapter.Id, StringComparison.OrdinalIgnoreCase)) &&
                ip != TemporaryIpFor(assignment.Ip, plan) &&
                !ip.StartsWith("169.254.", StringComparison.Ordinal));
            if (otherIp != null)
                return $"Карта {adapter.Name} имеет адрес {otherIp} вне сети стенда. Не меняйте служебное подключение; выберите другую карту.";
        }
        foreach (var adapter in adapters.Where(item => !selectedIds.Contains(item.Id)))
        {
            var conflictingIp = adapter.Ipv4.Split(", ")
                .FirstOrDefault(benchIps.Contains);
            if (conflictingIp != null)
                return $"Адрес {conflictingIp} уже назначен карте {adapter.Name}, которая не выбрана для стенда.";
        }

        return null;
    }

    public static async Task<string?> ApplyAsync(IReadOnlyList<AdapterAssignment> assignments,
        IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return "Настройка адаптеров доступна только в Windows.";

        var plan = System.Text.Json.JsonSerializer.Deserialize<BenchNetworkPlan>(System.Text.Json.JsonSerializer.Serialize(AppSettings.Instance.NetworkPlan))!;
        var benchIps = plan.Addresses.Select(a => a.Ip).ToArray();
        var adapters = GetAdapters();
        var error = ValidateAssignments(assignments, adapters, plan);
        if (error != null) return error;

        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            return "Текущий процесс TestBuilder не запущен с повышенными правами. " +
                "Закройте приложение и запустите TestBuilder.exe от имени администратора.";

        // set address replaces all static IPv4 addresses on the interface. Move
        // changed adapters to distinct temporary addresses first, so a swap
        // such as .2 ↔ .8 never creates a duplicate bench address.
        var temporaryIpOwners = assignments.ToDictionary(
            assignment => TemporaryIpFor(assignment.Ip, plan), assignment => assignment.AdapterId);
        var occupiedTemporaryIp = NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => (adapter.Id, Ip: address.Address.ToString())))
            .FirstOrDefault(item => temporaryIpOwners.TryGetValue(item.Ip, out var ownerId) &&
                !string.Equals(item.Id, ownerId, StringComparison.OrdinalIgnoreCase));
        if (occupiedTemporaryIp.Ip != null)
            return $"Временный адрес {occupiedTemporaryIp.Ip} уже используется. Освободите его перед настройкой карт.";

        foreach (var assignment in assignments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var adapter = adapters.Single(item =>
                string.Equals(item.Id, assignment.AdapterId, StringComparison.OrdinalIgnoreCase));
            if (!adapter.Ipv4.Split(", ").Any(ip => benchIps.Contains(ip) && ip != assignment.Ip))
                continue;

            var temporaryIp = TemporaryIpFor(assignment.Ip, plan);
            progress.Report($"Освобождение прежнего адреса на {adapter.Name}...");
            var releaseError = await RunNetshAsync(["interface", "ipv4", "set", "address",
                $"name={adapter.Name}", "source=static", $"address={temporaryIp}",
                $"mask={plan.Mask}", "gateway=none", "store=persistent"], cancellationToken);
            if (releaseError != null)
                return $"Не удалось освободить адрес на {adapter.Name}: {releaseError}";
            if (!await WaitForAddressAsync(adapter.Id, temporaryIp, cancellationToken))
                return $"netsh завершился без ошибки, но временный адрес {temporaryIp} " +
                    $"на карте {adapter.Name} не появился. Адреса не были назначены.";
        }

        foreach (var assignment in assignments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var adapter = adapters.Single(item =>
                string.Equals(item.Id, assignment.AdapterId, StringComparison.OrdinalIgnoreCase));
            progress.Report($"Настройка {adapter.Name}: {assignment.Ip}, маска {plan.Mask}...");

            var setError = await RunNetshAsync(["interface", "ipv4", "set", "address",
                $"name={adapter.Name}", "source=static", $"address={assignment.Ip}",
                $"mask={plan.Mask}", "gateway=none", "store=persistent"], cancellationToken);
            if (setError != null)
                return $"Не удалось настроить {adapter.Name} ({assignment.Ip}): {setError}";

            if (!await WaitForAddressAsync(adapter.Id, assignment.Ip, cancellationToken))
                return $"netsh завершился без ошибки, но адрес {assignment.Ip} на карте {adapter.Name} не появился.";

            progress.Report($"Настроено: {adapter.Name} → {assignment.Ip}, маска {plan.Mask}");
        }

        return null;
    }

    private static async Task<bool> WaitForAddressAsync(string adapterId, string ip,
        CancellationToken cancellationToken)
    {
        // Windows can publish a new address shortly after netsh exits.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (GetAdapters().Any(item =>
                string.Equals(item.Id, adapterId, StringComparison.OrdinalIgnoreCase) &&
                item.Ipv4.Split(", ").Contains(ip)))
                return true;
            await Task.Delay(200, cancellationToken);
        }
        return false;
    }

    private static async Task<string?> RunNetshAsync(IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("netsh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = NetshOutputEncoding(),
            StandardErrorEncoding = NetshOutputEncoding()
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start);
        if (process == null) return "Не удалось запустить netsh";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            cancellationToken.ThrowIfCancellationRequested();
            return "netsh не завершился за 30 секунд. Обновите состояние карт перед повторным применением.";
        }
        var standardOutput = (await stdout).Trim();
        var standardError = (await stderr).Trim();
        if (process.ExitCode == 0 && standardError.Length == 0) return null;
        var output = string.Join(" ", new[] { standardOutput, standardError }
            .Where(part => part.Length > 0));
        return $"netsh (код {process.ExitCode}): " +
            (output.Length > 0 ? output : "команда не вернула текст ошибки");
    }

    private static Encoding NetshOutputEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
        catch (ArgumentException) { return Encoding.UTF8; }
    }
}
