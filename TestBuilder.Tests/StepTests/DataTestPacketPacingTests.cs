using TestBuilder.Domain.Steps;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.StepTests;

public class DataTestPacketPacingTests
{
    private const int Packets = 4064;
    private static PacedSendResult Probe(bool timed, long elapsed = 500, int received = Packets) =>
        new(Packets, elapsed, timed ? "PcapTimedPackets" : "PcapPacedPackets")
        {
            UsesPacketTimer = timed,
            ReceiveQuality = new DataTestReceiveQuality(received, elapsed, false)
        };

    [Fact]
    public async Task TimerSingleSenderIsPreferredWithoutRunningMoreCpuHeavyStrategies()
    {
        var calls = new List<(int, bool)>();
        var selected = await DataTestPacketPacing.SelectAsync((lanes, timed, _) =>
        {
            calls.Add((lanes, timed));
            return Task.FromResult(Probe(timed));
        }, true, Packets, 1514, 100, 2, true, _ => { }, default);
        Assert.Equal(new DataTestPacingSelection(1, true), selected);
        Assert.Equal(new[] { (1, true) }, calls);
    }

    [Fact]
    public async Task SleepingTwoSendersAreTriedBeforeContinuousActiveWaiting()
    {
        var calls = new List<(int, bool)>();
        var selected = await DataTestPacketPacing.SelectAsync((lanes, timed, _) =>
        {
            calls.Add((lanes, timed));
            return Task.FromResult(Probe(timed, lanes == 1 ? 650 : 500));
        }, true, Packets, 1514, 100, 2, true, _ => { }, default);
        Assert.Equal(new DataTestPacingSelection(2, true), selected);
        Assert.Equal(new[] { (1, true), (2, true) }, calls);
    }

    [Fact]
    public async Task SlowTimerDoesNotLowerTheTargetOrRelaxTheDeficit()
    {
        var calls = new List<(int, bool)>();
        var selected = await DataTestPacketPacing.SelectAsync((lanes, timed, _) =>
        {
            calls.Add((lanes, timed));
            return Task.FromResult(Probe(timed, timed ? 650 : 500));
        }, true, Packets, 1514, 100, 2, true, _ => { }, default);
        Assert.Equal(new DataTestPacingSelection(1, false), selected);
        Assert.Equal(new[] { (1, true), (2, true), (1, false) }, calls);
    }

    [Fact]
    public async Task AllSlowStrategiesStillFail()
    {
        var calls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => DataTestPacketPacing.SelectAsync((_, timed, _) =>
        {
            calls++;
            return Task.FromResult(Probe(timed, 650));
        }, true, Packets, 1514, 100, 2, true, _ => { }, default));
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task MissingNativeTimerDoesNotPretendToUseSleepingPacing()
    {
        var calls = 0;
        var selected = await DataTestPacketPacing.SelectAsync((_, _, _) =>
        {
            calls++;
            return Task.FromResult(Probe(false));
        }, true, Packets, 1514, 100, 2, true, _ => { }, default);
        Assert.Equal(new DataTestPacingSelection(1, false), selected);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CaptureHealthStillParticipatesInTimerSelection()
    {
        var selected = await DataTestPacketPacing.SelectAsync((lanes, timed, _) =>
            Task.FromResult(Probe(timed) with
            {
                ReceiveQuality = new DataTestReceiveQuality(Packets, 500, lanes == 1)
            }), true, Packets, 1514, 100, 2, true, _ => { }, default);
        Assert.Equal(new DataTestPacingSelection(2, true), selected);
    }

    [Fact]
    public async Task CancelledTimerProbeDoesNotStartFallbackOrSecondSender()
    {
        using var stop = new CancellationTokenSource();
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DataTestPacketPacing.SelectAsync((_, _, token) =>
        {
            calls++;
            stop.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Probe(true));
        }, true, Packets, 1514, 100, 2, true, _ => { }, stop.Token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task QueueAndForcedActiveModesDoNotProbePacketTimer()
    {
        var selected = await DataTestPacketPacing.SelectAsync((_, timed, _) =>
        {
            Assert.False(timed);
            return Task.FromResult(Probe(false));
        }, false, Packets, 1514, 100, 2, true, _ => { }, default);
        Assert.Equal(new DataTestPacingSelection(1, false), selected);
    }

    [Fact]
    public void TimerPacketTrainKeepsIndividualSpacingAndIncludesSchedulerStalls()
    {
        double time = 0;
        var starts = new List<double>();
        var waits = new List<double>();
        DataTestPacketTrain.Send(4, 1000, i =>
        {
            starts.Add(time);
            time += i == 1 ? 20 : 0.125;
        }, () => time, () => throw new Exception("Unexpected active loop"), default,
            deadline =>
            {
                waits.Add(deadline);
                time = Math.Max(time, deadline);
            });
        Assert.Equal(new[] { 0.0, 1.0, 21.0, 22.0 }, starts);
        Assert.Equal(23, time);
        Assert.Equal(5, waits.Count); // Includes the final frame's wire slot.
    }

    [Fact]
    public void HundredMbpsSlotCanReleaseTheCpuForMostOfItsInterval()
    {
        double time = 0;
        var pauses = new List<double>();
        DataTestDeadlineWait.Wait(0.123, () => time, duration =>
        {
            pauses.Add(duration);
            time += duration + 0.04; // Overdue wakeup is included, not recovered in a burst.
        }, default, DataTestDeadlineWait.PacketSpinTailMs, 0.01);
        Assert.Single(pauses);
        Assert.Equal(0.093, pauses[0], 6);
        Assert.Equal(0.133, time, 6);
    }

    [Theory]
    [InlineData(true, 3, true)]
    [InlineData(true, 2, false)]
    [InlineData(false, 3, false)]
    public void OnlySlowTimedMeasurementRequestsActiveRetry(bool timed, double deficit, bool expected)
    {
        var result = DataTestPortResult.Fail("measurement") with { UsesPacketTimer = timed, TxDeficitPercent = deficit };
        Assert.Equal(expected, DataTestPacketPacing.NeedsActiveRetry(result, 2));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task MainMeasurementFallbackUsesTheExistingTwoAttemptBudgetAndKeepsHistory(bool underRate, bool failAgain)
    {
        var timed = true;
        var modes = new List<bool>();
        var result = await DataTestDirectionRetry.RunAsync(_ =>
        {
            modes.Add(timed);
            return Task.FromResult(DataTestPortResult.Fail("sample", infrastructureFailure: underRate) with
            {
                Passed = modes.Count == 2 && !failAgain, UsesPacketTimer = timed,
                TargetBandwidthMbps = 100, TransmittedPackets = 40637, ReceivedPackets = 40637,
                TxDeficitPercent = modes.Count == 1 && underRate ? 5 : 0
            });
        }, System.Net.IPAddress.Parse("192.168.0.2"), System.Net.IPAddress.Parse("192.168.0.3"), 5000,
            sample => { if (DataTestPacketPacing.NeedsActiveRetry(sample, 2)) timed = false; },
            _ => Task.CompletedTask, default);
        Assert.Equal(new[] { true, !underRate }, modes);
        Assert.Equal(!failAgain, result.Passed);
        Assert.Equal(2, result.AttemptCount);
        Assert.Equal(2, result.Attempts.Count);
        Assert.All(result.Attempts, sample =>
        {
            Assert.Equal(100, sample.TargetBandwidthMbps);
            Assert.Equal(40637, sample.TransmittedPackets);
        });
        Assert.True(result.Attempts[0].UsesPacketTimer);
        Assert.False(result.Attempts[0].Passed);
    }

    [Fact]
    public void PairSelectedAsHundredMbpsUsesHundredEvenWithGigabitFallbackOrAnotherPair()
    {
        var node = new RunDataTestNodeViewModel
        {
            AllowGigabit = true, TargetBandwidthMbps = 1000,
            PortsText = "Selected,192.168.0.2,192.168.0.3,100\r\nOther,192.168.0.4,192.168.0.5,1000"
        };
        var step = Assert.IsType<RunDataTestStep>(node.CreateStep(NullLogger.Instance));
        var selected = node.ParsePorts().Single(p => p.Name == "Selected");
        Assert.Equal(100, step.ResolveTargetBandwidthMbps(selected));
        Assert.Equal(4064, RunDataTestStep.CalculateExpectedPackets(step.ResolveTargetBandwidthMbps(selected), 1514, 500));
        Assert.False(DataTestLineRatePacer.CanUse(step.ResolveTargetBandwidthMbps(selected), 1000));
    }
}
