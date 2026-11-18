using TestBuilder.Services;
using Xunit.Abstractions;

namespace TestBuilder.Tests.Services;

// Opt-in check in the existing test project; uses only the two supplied
// test-card addresses. No production graph or configuration is run.
public sealed class NetworkProbeHardwareFactAttribute : FactAttribute
{
    public NetworkProbeHardwareFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TFORTIS_NETWORK_PROBE_PAIR")))
            Skip = "Задайте TFORTIS_NETWORK_PROBE_PAIR=IPv4,IPv4 на Windows-стенде с соединёнными тестовыми картами.";
    }
}

public class NetworkConnectivityHardwareTests(ITestOutputHelper output)
{
    [NetworkProbeHardwareFact]
    [Trait("Category", "Hardware")]
    public async Task ConnectedTestCardsReceiveUniqueProbesInBothDirections()
    {
        var ips = Environment.GetEnvironmentVariable("TFORTIS_NETWORK_PROBE_PAIR")!.Split(',');
        Assert.Equal(2, ips.Length);
        var cards = DataTestNetworkConfigurator.GetAdapters();
        var first = Assert.Single(cards.Where(c => c.Ipv4.Split(", ").Contains(ips[0])));
        var second = Assert.Single(cards.Where(c => c.Ipv4.Split(", ").Contains(ips[1])));
        Assert.NotEqual(first.Id, second.Id);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var pair = new NetworkProbePair("Аппаратная проверка связи", ips[0], ips[1], first.Id, second.Id);
        var result = await NetworkConnectivityProbe.CheckAsync(pair, cancellation.Token);
        output.WriteLine($"ПК {Environment.MachineName}; сборка {typeof(NetworkConnectivityProbe).Assembly.GetName().Version}");
        output.WriteLine(result.Status); output.WriteLine(result.Details);
        Assert.True(result.Passed, result.Details);
        Assert.True(result.Forward!.Transmitted > 0);
        Assert.Equal(result.Forward.Transmitted, result.Forward.Received);
        Assert.True(result.Reverse!.Transmitted > 0);
        Assert.Equal(result.Reverse.Transmitted, result.Reverse.Received);
    }
}
