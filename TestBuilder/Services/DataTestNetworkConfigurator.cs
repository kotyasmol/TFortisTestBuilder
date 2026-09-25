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

    // PSW-2G6F+ has six 100 Mbps copper ports and two 1 Gbps SFP ports.
    // The remaining two bench adapters are kept as spare addresses for other models.
    public static (IReadOnlyList<AdapterAssignment>? Assignments, string? Error)
        SuggestPsw2G6FAssignments(IReadOnlyList<BenchAdapter> adapters,
            IReadOnlyCollection<string> savedAdapterIds)
    {
        var savedIds = savedAdapterIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = adapters.Where(adapter => savedIds.Contains(adapter.Id) ||
                adapter.Ipv4.Split(", ").Any(BenchIps.Contains))
            .ToArray();
        if (candidates.Length != BenchIps.Count)
            return (null, $"Для подбора нужно найти ровно 10 карт стенда по текущим IP или сохранённой привязке; найдено {candidates.Length}. Выберите карты вручную.");

        var copper = candidates.Where(adapter => adapter.Status.Equals("Up", StringComparison.OrdinalIgnoreCase) &&
                adapter.SpeedMbps is >= 95 and <= 105).ToArray();
        var sfp = candidates.Where(adapter => adapter.Status.Equals("Up", StringComparison.OrdinalIgnoreCase) &&
                adapter.SpeedMbps >= 950).ToArray();
        var spare = candidates.Where(adapter => !adapter.Status.Equals("Up", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (copper.Length != 6 || sfp.Length != 2 || spare.Length != 2)
            return (null, $"Для PSW-2G6F+ нужны 6 поднятых карт 100 Мбит/с, 2 поднятые карты 1 Гбит/с и 2 отключённые резервные. Сейчас: {copper.Length}, {sfp.Length}, {spare.Length}. Проверьте подключение или выберите карты вручную.");

        // Keep the current IP order within each speed group. Speed alone cannot
        // establish which physical port or pair a cable is connected to.
        var ordered = copper.OrderBy(CurrentBenchIpOrder).Concat(sfp.OrderBy(CurrentBenchIpOrder))
            .Concat(spare.OrderBy(CurrentBenchIpOrder)).ToArray();
        return (BenchIps.Zip(ordered, (ip, adapter) => new AdapterAssignment(ip, adapter.Id)).ToArray(), null);
    }

    private static int CurrentBenchIpOrder(BenchAdapter adapter)
    {
        foreach (var ip in adapter.Ipv4.Split(", "))
        {
            for (var index = 0; index < BenchIps.Count; index++)
                if (BenchIps[index] == ip) return index;
        }
        return int.MaxValue;
    }

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
        foreach (var adapter in adapters.Where(item => selectedIds.Contains(item.Id)))
        {
            var otherIp = adapter.Ipv4.Split(", ").FirstOrDefault(ip =>
                !string.IsNullOrWhiteSpace(ip) && !BenchIps.Contains(ip) &&
                !ip.StartsWith("169.254.", StringComparison.Ordinal));
            if (otherIp != null)
                return $"Карта {adapter.Name} имеет адрес {otherIp} вне сети стенда. Не меняйте служебное подключение; выберите другую карту.";
        }
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

        // Release old bench addresses before assigning new ones. Otherwise a
        // permutation such as .2 ↔ .8 temporarily duplicates an IPv4 address.
        foreach (var assignment in assignments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var adapter = adapters.Single(item =>
                string.Equals(item.Id, assignment.AdapterId, StringComparison.OrdinalIgnoreCase));
            foreach (var oldIp in adapter.Ipv4.Split(", ").Where(BenchIps.Contains))
            {
                if (oldIp == assignment.Ip) continue;
                progress.Report($"Освобождение {oldIp} на {adapter.Name}...");
                var deleteError = await RunNetshAsync(["interface", "ipv4", "delete", "address",
                    $"name={adapter.Name}", $"address={oldIp}", "store=persistent"], cancellationToken);
                if (deleteError != null)
                    return $"Не удалось освободить {oldIp} на {adapter.Name}: {deleteError}. Запустите приложение от имени администратора.";
            }
        }

        foreach (var assignment in assignments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var adapter = adapters.Single(item =>
                string.Equals(item.Id, assignment.AdapterId, StringComparison.OrdinalIgnoreCase));
            progress.Report($"Настройка {adapter.Name}: {assignment.Ip}/24...");

            var setError = await RunNetshAsync(["interface", "ipv4", "set", "address",
                $"name={adapter.Name}", "source=static", $"address={assignment.Ip}",
                "mask=255.255.255.0", "gateway=none", "store=persistent"], cancellationToken);
            if (setError != null)
                return $"Не удалось настроить {adapter.Name} ({assignment.Ip}): {setError}. Запустите приложение от имени администратора.";

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

    private static async Task<string?> RunNetshAsync(IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("netsh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start);
        if (process == null) return "Не удалось запустить netsh";
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = (await stdout + " " + await stderr).Trim();
        return process.ExitCode == 0 ? null : output;
    }
}
