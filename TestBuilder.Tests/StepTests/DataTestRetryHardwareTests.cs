using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Tests.Support;
using Xunit.Abstractions;

namespace TestBuilder.Tests.StepTests;

public sealed class DataTestRetryHardwareFactAttribute : FactAttribute
{
    public DataTestRetryHardwareFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TFORTIS_NETWORK_RETRY_PAIRS")))
            Skip = "Задайте TFORTIS_NETWORK_RETRY_PAIRS=badA,badB;goodA,goodB на стенде с известными прямыми соединениями карт.";
    }
}

// The operator explicitly selects one non-connected and one connected pair.
// Real packets only; no graph, power, IP changes, printing or server requests.
[Collection(AppSettingsTestCollection.Name)]
public class DataTestRetryHardwareTests(ITestOutputHelper output)
{
    [DataTestRetryHardwareFact]
    [Trait("Category", "Hardware")]
    public async Task RealLossIsRetriedOnceAndDoesNotHideBehindNextSuccessfulPair()
    {
        var pairs = Environment.GetEnvironmentVariable("TFORTIS_NETWORK_RETRY_PAIRS")!.Split(';').Select(p => p.Split(',')).ToArray();
        Assert.Equal(2, pairs.Length); Assert.All(pairs, p => Assert.Equal(2, p.Length));
        var cards = DataTestNetworkConfigurator.GetAdapters();
        foreach (var ips in pairs)
        {
            var first = Assert.Single(cards.Where(c => c.Ipv4.Split(", ").Contains(ips[0])));
            var second = Assert.Single(cards.Where(c => c.Ipv4.Split(", ").Contains(ips[1])));
            Assert.Null(NetworkConnectivityProbe.CheckBindings(new("Контроль", ips[0], ips[1], first.Id, second.Id, 1), cards));
        }
        var ports = pairs.Select((ips, i) => new DataTestPortConfig(i == 0 ? "Без соединения" : "Прямое соединение", ips[0], ips[1], 1));
        var step = new RunDataTestStep(NullLogger.Instance, "SoftwarePcap", 1, 128, 43962, 5000, 1, 300,
            0, 0, 0, 100, true, ports, "RetryProbe", true);
        var context = new TestContext(new RegisterState());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Assert.Equal(StepResult.False, await step.ExecuteAsync(context, cancellation.Token));
        Assert.False(context.GetVariable<bool>("RetryProbe.Passed"));
        for (var i = 0; i < pairs.Length; i++)
        {
            foreach (var direction in new[] { "Forward", "Reverse" })
            {
                var prefix = $"RetryProbe.Port{i}.{direction}";
                Assert.Equal(i == 0 ? 2 : 1, context.GetVariable<int>(prefix + ".AttemptCount"));
                Assert.Equal(i != 0, context.GetVariable<bool>(prefix + ".Passed"));
                var tx = context.GetVariable<int>(prefix + ".TransmittedPackets");
                var rx = context.GetVariable<int>(prefix + ".ReceivedPackets");
                Assert.True(tx > 0); Assert.Equal(i == 0 ? 0 : tx, rx);
                output.WriteLine($"Пара {pairs[i][0]} ↔ {pairs[i][1]}, {direction}: попыток {context.GetVariable<int>(prefix + ".AttemptCount")}, TX {tx}, RX {rx}");
                if (i == 0)
                    for (var attempt = 1; attempt <= 2; attempt++)
                    {
                        Assert.False(context.GetVariable<bool>($"{prefix}.Attempt{attempt}.Passed"));
                        Assert.False(context.GetVariable<bool>($"{prefix}.Attempt{attempt}.InfrastructureFailure"));
                        Assert.True(context.GetVariable<int>($"{prefix}.Attempt{attempt}.TransmittedPackets") > 0);
                        Assert.Equal(0, context.GetVariable<int>($"{prefix}.Attempt{attempt}.ReceivedPackets"));
                        output.WriteLine(context.GetVariable<string>($"{prefix}.Attempt{attempt}.Error"));
                    }
            }
        }
        Assert.Contains("обе попытки не пройдены", context.ReportEntries[0].Value);
        Assert.Equal(TestErrorCode.DataTestLoss, context.ReportEntries[0].ErrorCode);
        Assert.False(context.ReportEntries[0].IsSuccess); Assert.True(context.ReportEntries[1].IsSuccess);
    }
}
