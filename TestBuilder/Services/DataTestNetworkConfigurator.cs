using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Services;

public sealed record BenchAdapter(string Id, string Name, string Mac, string Status, string Ipv4,
    double SpeedMbps = 0)
{
    public string Display => $"{Name}  |  {Mac}  |  {Status}  |  " +
        (SpeedMbps > 0 ? $"{SpeedMbps:F0} Mbps" : "скорость неизвестна") + $"  |  {Ipv4}";
}

public sealed record AdapterAssignment(string Ip, string AdapterId);

public static class DataTestNetworkConfigurator
{
    public static IReadOnlyList<string> BenchIps { get; } = Enumerable.Range(2, 10)
        .Select(lastOctet => $"192.168.0.{lastOctet}").ToArray();

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

    public static string? ValidateAssignments(IReadOnlyList<AdapterAssignment> assignments,
        IReadOnlyList<BenchAdapter> adapters)
    {
        if (assignments.Count != BenchIps.Count ||
            !BenchIps.SequenceEqual(assignments.Select(assignment => assignment.Ip)))
            return "Нужно выбрать адаптер для каждого адреса 192.168.0.2–192.168.0.11.";

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
        foreach (var adapter in adapters.Where(item => !selectedIds.Contains(item.Id)))
        {
            var conflictingIp = adapter.Ipv4.Split(", ")
                .FirstOrDefault(BenchIps.Contains);
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

        var adapters = GetAdapters();
        var error = ValidateAssignments(assignments, adapters);
        if (error != null) return error;

        foreach (var assignment in assignments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var adapter = adapters.Single(item =>
                string.Equals(item.Id, assignment.AdapterId, StringComparison.OrdinalIgnoreCase));
            progress.Report($"Настройка {adapter.Name}: {assignment.Ip}/24...");

            var start = new ProcessStartInfo("netsh")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("interface");
            start.ArgumentList.Add("ipv4");
            start.ArgumentList.Add("set");
            start.ArgumentList.Add("address");
            start.ArgumentList.Add($"name={adapter.Name}");
            start.ArgumentList.Add("source=static");
            start.ArgumentList.Add($"address={assignment.Ip}");
            start.ArgumentList.Add("mask=255.255.255.0");
            start.ArgumentList.Add("gateway=none");
            start.ArgumentList.Add("store=persistent");

            using var process = Process.Start(start);
            if (process == null) return $"Не удалось запустить netsh для {assignment.Ip}.";
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = (await stdout + " " + await stderr).Trim();
            if (process.ExitCode != 0)
                return $"Не удалось настроить {adapter.Name} ({assignment.Ip}): {output}. Запустите приложение от имени администратора.";

            // Windows can publish the new address shortly after netsh exits.
            var verified = false;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                if (GetAdapters().Any(item =>
                    string.Equals(item.Id, assignment.AdapterId, StringComparison.OrdinalIgnoreCase) &&
                    item.Ipv4.Split(", ").Contains(assignment.Ip)))
                {
                    verified = true;
                    break;
                }
                await Task.Delay(200, cancellationToken);
            }
            if (!verified)
                return $"netsh завершился без ошибки, но адрес {assignment.Ip} на карте {adapter.Name} не появился.";

            progress.Report($"Настроено: {adapter.Name} → {assignment.Ip}/24");
        }

        return null;
    }
}
