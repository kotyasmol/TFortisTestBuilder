using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.Services;

public class NetworkConnectivityProbeTests
{
    private static readonly NetworkProbePair Pair = new("Пара A", "10.1.1.2", "10.1.1.3", "a", "b");
    private static BenchAdapter[] Cards() => new[]
    {
        new BenchAdapter("a", "Карта A", "001122334455", "Up", Pair.FirstIp, 1000),
        new BenchAdapter("b", "Карта B", "001122334456", "Up", Pair.SecondIp, 1000)
    };

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate-ip")]
    [InlineData("wrong-binding")]
    [InlineData("not-applied")]
    [InlineData("down")]
    [InlineData("gateway")]
    [InlineData("slow-link")]
    public void InvalidPhysicalMappingCannotStartPacketProbe(string issue)
    {
        var cards = Cards();
        if (issue == "missing") cards = cards[1..];
        if (issue == "duplicate-ip") cards = cards.Append(new("c", "Другая карта", "001122334457", "Up", Pair.FirstIp)).ToArray();
        if (issue == "wrong-binding") cards = new[] { cards[0] with { Ipv4 = Pair.SecondIp }, cards[1] with { Ipv4 = Pair.FirstIp } };
        if (issue == "not-applied") cards[0] = cards[0] with { Ipv4 = "10.9.9.2" };
        if (issue == "down") cards[0] = cards[0] with { Status = "Down", SpeedMbps = 0 };
        if (issue == "gateway") cards[0] = cards[0] with { HasIpv4Gateway = true };
        if (issue == "slow-link") cards[0] = cards[0] with { SpeedMbps = 10 };
        Assert.NotNull(NetworkConnectivityProbe.CheckBindings(Pair, cards));
        Assert.Null(NetworkConnectivityProbe.CheckBindings(Pair, Cards()));
    }

    [Fact]
    public void RequestIncludesOnlySelectedPairsAndRejectsOnePhysicalCardForBothSides()
    {
        var plan = new BenchNetworkPlan { Addresses = new()
        {
            new() { Ip = Pair.FirstIp, TemporaryIp = "192.0.2.2", Pair = "A", IncludeInDataTest = true },
            new() { Ip = Pair.SecondIp, TemporaryIp = "192.0.2.3", Pair = "A", IncludeInDataTest = true },
            new() { Ip = "10.1.1.4", TemporaryIp = "192.0.2.4" }
        }};
        var bindings = new Dictionary<string, string> { [Pair.FirstIp] = "a", [Pair.SecondIp] = "b" };
        Assert.Single(NetworkConnectivityProbe.CreatePairs(plan, bindings));
        bindings[Pair.SecondIp] = "a";
        Assert.Throws<InvalidOperationException>(() => NetworkConnectivityProbe.CreatePairs(plan, bindings));
    }

    [Fact]
    public void MissingResultsNeverClaimConnectionOrThroughput()
    {
        var context = new TestContext(new RegisterState());
        var result = NetworkConnectivityProbe.ReadResult(Pair, context);
        Assert.False(result.Passed);
        Assert.Contains("не подтверждена", result.Status);
        context.SetVariable("NetworkProbe.Port0.Forward.Passed", true);
        Assert.False(NetworkConnectivityProbe.ReadResult(Pair, context).Passed);
        context.SetVariable("NetworkProbe.Port0.Reverse.Passed", true);
        Assert.False(NetworkConnectivityProbe.ReadResult(Pair, context).Passed);
        foreach (var name in new[] { "Forward", "Reverse" })
        {
            context.SetVariable($"NetworkProbe.Port0.{name}.TransmittedPackets", 10);
            context.SetVariable($"NetworkProbe.Port0.{name}.ReceivedPackets", 10);
        }
        Assert.True(NetworkConnectivityProbe.ReadResult(Pair, context).Passed);
        Assert.DoesNotContain("Гбит", NetworkConnectivityProbe.ReadResult(Pair, context).Status);
        context.SetVariable("NetworkProbe.Port0.RetryDetails", "Пройдено со второй попытки. Первая попытка: потери 10%.");
        Assert.Contains("Первая попытка: потери 10%", NetworkConnectivityProbe.ReadResult(Pair, context).Details);
    }

    [Fact]
    public void QuickProbeBuildsOnlyThePacketStepWithLowRateAndBothDirections()
    {
        var step = NetworkConnectivityProbe.CreateStep(Pair, NullLogger.Instance);
        // Packet engine is used directly, without compiling any user graph or
        // acquiring services that could switch power, print or POST reports.
        Assert.Equal("RunDataTestStep", step.GetType().Name);
        Assert.Equal(1, NetworkConnectivityProbe.ProbeMbps);
        Assert.InRange(NetworkConnectivityProbe.ProbeDurationMs, 100, 1000);
    }
}
