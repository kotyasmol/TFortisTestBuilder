using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;

namespace TestBuilder.Services;

public sealed record NetworkProbePair(string Name, string FirstIp, string SecondIp, string FirstId, string SecondId, int RequiredMbps = 100);
public sealed record NetworkProbeDirection(bool Passed, int Transmitted, int Received, string Error);
public sealed record NetworkProbeResult(NetworkProbePair Pair, string Status, string Details,
    NetworkProbeDirection? Forward = null, NetworkProbeDirection? Reverse = null)
{
    public string Name => $"{Pair.Name}: {Pair.FirstIp} ↔ {Pair.SecondIp}";
    public bool Passed => Forward is { Passed: true, Transmitted: > 0, Received: > 0 } &&
        Reverse is { Passed: true, Transmitted: > 0, Received: > 0 };
}

// Short, low-rate probes through the production packet engine. No graph, DUT
// power operations, Modbus, serial numbers, labels or server reports are run.
internal static class NetworkConnectivityProbe
{
    internal const int ProbeDurationMs = 300;
    internal const int ProbeMbps = 1;
    internal const int ProbePacketBytes = 128;

    internal static IReadOnlyList<NetworkProbePair> CreatePairs(BenchNetworkPlan plan,
        IReadOnlyDictionary<string, string> bindings)
    {
        plan.BuildDataTestPairs();
        return plan.Addresses.Where(a => a.IncludeInDataTest).GroupBy(a => a.Pair.Trim()).Select(group =>
        {
            var rows = group.ToArray();
            if (!bindings.TryGetValue(rows[0].Ip, out var first) || string.IsNullOrWhiteSpace(first) ||
                !bindings.TryGetValue(rows[1].Ip, out var second) || string.IsNullOrWhiteSpace(second))
                throw new InvalidOperationException($"Пара «{group.Key}»: выберите обе сетевые карты.");
            if (first.Equals(second, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Пара «{group.Key}»: нужны две разные сетевые карты.");
            return new NetworkProbePair(group.Key, rows[0].Ip, rows[1].Ip, first, second, rows[0].TestMbps);
        }).ToArray();
    }

    internal static string? CheckBindings(NetworkProbePair pair, IReadOnlyList<BenchAdapter> adapters)
    {
        foreach (var (ip, id) in new[] { (pair.FirstIp, pair.FirstId), (pair.SecondIp, pair.SecondId) })
        {
            var selected = adapters.Where(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (selected.Length != 1) return $"Карта для {ip} отсутствует. Обновите привязки.";
            if (selected[0].HasIpv4Gateway)
                return $"Карта {selected[0].Name} имеет шлюз IPv4 и может обслуживать связь компьютера. Выберите карту стенда.";
            var owners = adapters.Where(a => a.Ipv4.Split(", ").Contains(ip)).ToArray();
            if (owners.Length != 1 || !owners[0].Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                return $"Адрес {ip} не соответствует выбранной карте. Проверьте привязки и примените адреса.";
            if (!selected[0].Status.Equals("Up", StringComparison.OrdinalIgnoreCase))
                return $"Нет линка у {ip} ({selected[0].Name}, {selected[0].Status}). Проверьте кабель и питание второй стороны.";
            if (selected[0].SpeedMbps > 0 && selected[0].SpeedMbps < pair.RequiredMbps)
                return $"Карта {ip} согласовала {selected[0].SpeedMbps:F0} Мбит/с, в плане требуется {pair.RequiredMbps} Мбит/с.";
        }
        return null;
    }

    internal static Task<NetworkProbeResult> CheckAsync(NetworkProbePair pair, CancellationToken token) =>
        Task.Run(async () =>
        {
            token.ThrowIfCancellationRequested();
            var cards = DataTestNetworkConfigurator.GetAdapters();
            if (CheckBindings(pair, cards) is { } error) return new NetworkProbeResult(pair, "Проверка не выполнена", error);
            var context = new TestContext(new RegisterState());
            var step = CreateStep(pair, LoggingService.Instance.CreateLogger("Проверка сети"));
            await step.ExecuteAsync(context, token);
            var links = string.Join("; ", new[] { pair.FirstId, pair.SecondId }.Select(id =>
            {
                var card = cards.Single(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                return $"{card.Name}: {card.LinkDisplay}";
            }));
            var result = ReadResult(pair, context);
            return result with { Details = links + Environment.NewLine + result.Details };
        }, token);

    internal static RunDataTestStep CreateStep(NetworkProbePair pair, ILogger logger) => new(logger,
        "SoftwarePcap", 1, ProbePacketBytes, 43962, 5000, ProbeMbps, ProbeDurationMs,
        0, 0, 0, 100, true, new[] { new DataTestPortConfig(pair.Name, pair.FirstIp, pair.SecondIp, ProbeMbps) },
        "NetworkProbe", true);

    internal static NetworkProbeResult ReadResult(NetworkProbePair pair, TestContext context)
    {
        NetworkProbeDirection Direction(string name) => new(
            context.GetVariable<bool>($"NetworkProbe.Port0.{name}.Passed"),
            context.GetVariable<int>($"NetworkProbe.Port0.{name}.TransmittedPackets"),
            context.GetVariable<int>($"NetworkProbe.Port0.{name}.ReceivedPackets"),
            context.GetVariable<string>($"NetworkProbe.Port0.{name}.Error") ?? "");
        var forward = Direction("Forward"); var reverse = Direction("Reverse");
        string Describe(string source, string target, NetworkProbeDirection d) =>
            $"{source} → {target}: TX {d.Transmitted}, RX {d.Received}" + (d.Error.Length == 0 ? "" : $". {d.Error}");
        var details = Describe(pair.SecondIp, pair.FirstIp, forward) + Environment.NewLine +
            Describe(pair.FirstIp, pair.SecondIp, reverse);
        var error = context.GetVariable<string>("NetworkProbe.Error");
        if (!forward.Passed && !reverse.Passed && !string.IsNullOrEmpty(error)) details += Environment.NewLine + error;
        var result = new NetworkProbeResult(pair, "", details, forward, reverse);
        return result with { Status = result.Passed ? "Связь в обе стороны подтверждена" : "Связь не подтверждена" };
    }
}
