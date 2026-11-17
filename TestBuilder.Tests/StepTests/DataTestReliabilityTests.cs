using System.Diagnostics;
using System.Net.NetworkInformation;
using TestBuilder.Domain.Steps;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class DataTestReliabilityTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Пара,192.168.0.2")]
    [InlineData("Пара,192.168.0.2,192.168.0.2,100")]
    [InlineData("Пара,192.168.0.2,wrong-ip,100")]
    [InlineData("Пара,192.168.0.2,192.168.0.3,typo")]
    public void InvalidPairCannotBeSilentlySkippedAtCompilation(string ports)
    {
        var node = new RunDataTestNodeViewModel { PortsText = ports };
        Assert.Throws<InvalidOperationException>(() => node.CreateStep(NullLogger.Instance));
    }

    [Fact]
    public async Task CalibrationPrefersOneSenderWhenItReachesTarget()
    {
        var calls = new List<int>();
        var selected = await DataTestSenderCalibration.SelectAsync((lanes, _) =>
        {
            calls.Add(lanes);
            return Task.FromResult(new PacedSendResult(40637, 500, "native"));
        }, 40637, 1514, 1000, 2, true, _ => { }, default);
        Assert.Equal(1, selected);
        Assert.Equal(new[] { 1 }, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CalibrationCanChooseTwoSendersWithoutAcceptingShortOrSlowSingleSend(bool shortSend)
    {
        var selected = await DataTestSenderCalibration.SelectAsync((lanes, _) =>
            Task.FromResult(new PacedSendResult(lanes == 1 && shortSend ? 12191 : 40637,
                lanes == 1 && !shortSend ? 1000 : 500, "native")),
            40637, 1514, 1000, 2, true, _ => { }, default);
        Assert.Equal(2, selected);
    }

    [Fact]
    public async Task CalibrationRejectsBothFailedStrategiesBeforeMainMeasurement()
    {
        var calls = 0;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DataTestSenderCalibration.SelectAsync((_, _) =>
        {
            calls++;
            throw new InvalidOperationException("native short transmit");
        }, 40637, 1514, 1000, 2, true, _ => { }, default));
        Assert.Equal(2, calls);
        Assert.Contains("Основное измерение не началось", error.Message);
        Assert.Contains("native short transmit", error.Message);
    }

    [Fact]
    public async Task CompleteLineRateQueueWithSlowDriverIsNotMistakenForDutLoss()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DataTestSenderCalibration.SelectAsync((_, _) =>
            Task.FromResult(new PacedSendResult(40637, 623, "NpcapLineRateQueue")),
            40637, 1514, 1000, 2, false, _ => { }, default));
        Assert.Contains("за 623 мс", error.Message);
        Assert.Contains("NpcapLineRateQueue", error.Message);
        Assert.Contains("не измеренная потеря", error.Message);
    }

    [Fact]
    public async Task RealtekLineRateCanSelectTwoSendersWithTheSameSpeedRequirement()
    {
        // APK-04-04 after the Realtek update: a single short queue sender
        // cannot fill the link; independent handles overlap driver completion.
        var selected = await DataTestSenderCalibration.SelectAsync((lanes, _) =>
            Task.FromResult(new PacedSendResult(40637, lanes == 1 ? 565 : 509,
                lanes == 1 ? "NpcapLineRateQueue" : "PcapParallelQueues")),
            40637, 1514, 1000, 2, true, _ => { }, default);
        Assert.Equal(2, selected);
    }

    [Fact]
    public async Task ReceiveDrainIncludesDelayedWindowsBatch()
    {
        var timer = Stopwatch.StartNew();
        await RunDataTestStep.WaitForCaptureDrainAsync(() => timer.ElapsedMilliseconds >= 120 ? 2 : 1, default, 2);
        Assert.True(timer.ElapsedMilliseconds >= 120);
    }

    [Theory]
    [InlineData(4000, true)]
    [InlineData(3800, false)]
    public async Task PacedFallbackIsJudgedByActualRateWithTheConfiguredDeficit(int count, bool accepted)
    {
        Task<int> Run() => DataTestSenderCalibration.SelectAsync((_, _) =>
            Task.FromResult(new PacedSendResult(count, 500, "SharpPcapSendPacket")),
            4064, 1514, 100, 2, false, _ => { }, default);
        if (accepted) Assert.Equal(1, await Run());
        else await Assert.ThrowsAsync<InvalidOperationException>(Run);
    }

    [Fact]
    public void HostCounterDeltaReportsNicDropsWithoutIncludingOlderErrors()
    {
        var before = new DataTestHostCounters(true, 100, 200, 300, 400);
        Assert.False(before.Delta(before).HasErrors);
        Assert.True(new DataTestHostCounters(true, 101, 200, 300, 400).Delta(before).HasErrors);
        Assert.False(default(DataTestHostCounters).Delta(before).Available);
        Assert.Contains("first 0, last 4, longest gap 2", RunDataTestStep.DescribeMissing(new[] { 0, 0, 1, 1, 0 }));
    }

    private sealed record Adapter(string Name, PhysicalAddress Mac);

    [Theory]
    [InlineData("{11111111-1111-1111-1111-111111111111}")]
    [InlineData("{22222222-2222-2222-2222-222222222222}")]
    public void AdapterBindingUsesLocalIdEvenWhenVirtualAdaptersShareMac(string localId)
    {
        var mac = PhysicalAddress.Parse("001B21A7DF07");
        var actual = new Adapter(@"\Device\NPF_" + localId, mac);
        var clone = new Adapter(@"\Device\NPF_{33333333-3333-3333-3333-333333333333}", mac);
        Assert.Same(actual, DataTestAdapterBinding.Resolve(new[] { clone, actual }, localId, mac, a => a.Name, a => a.Mac));
        Assert.Throws<InvalidOperationException>(() => DataTestAdapterBinding.Resolve(new[] { actual, clone },
            "missing-id", mac, a => a.Name, a => a.Mac));
    }
}
