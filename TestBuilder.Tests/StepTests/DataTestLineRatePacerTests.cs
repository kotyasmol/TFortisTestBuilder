using System;
using System.Collections.Generic;
using System.Threading;
using TestBuilder.Domain.Steps;
using Xunit;

namespace TestBuilder.Tests.StepTests;

public class DataTestLineRatePacerTests
{
    [Theory]
    [InlineData(1000, 1000, true)]
    [InlineData(100, 100, false)]
    [InlineData(100, 1000, false)]
    [InlineData(500, 1000, false)]
    [InlineData(1000, 2500, false)]
    [InlineData(1000, 100, false)]
    [InlineData(1000, 0, false)]
    public void OnlyFullHighSpeedLinkUsesHardwarePacing(int target, double link, bool allowed) =>
        Assert.Equal(allowed, DataTestLineRatePacer.CanUse(target, link));

    [Fact]
    public void FastNativeEnqueueIsBoundedAndFinalWireTimeIsIncluded()
    {
        double time = 0;
        var starts = new List<double>();
        // 1000 packets/s, two packets per 2 ms window; native returns after
        // accepting a batch, before the NIC has put all of it on the wire.
        var pacer = new DataTestLineRatePacer(1000, () => time, () => time += 0.125, default);
        for (var i = 0; i < 250; i++)
        {
            pacer.BeginBatch();
            starts.Add(time);
            time += 0.125;
            pacer.EndBatch(2);
        }
        pacer.Drain();
        Assert.Equal(500, time);
        for (var i = 1; i < starts.Count; i++) Assert.Equal(2, starts[i] - starts[i - 1]);
    }

    [Fact]
    public void SchedulerStallDoesNotBecomeAnUnboundedCatchUpBurst()
    {
        double time = 0;
        var pacer = new DataTestLineRatePacer(1000, () => time, () => time += 0.125, default);
        pacer.BeginBatch();
        pacer.EndBatch(2);
        time = 100; // OS descheduled the sender for many windows.
        pacer.BeginBatch();
        pacer.EndBatch(2);
        pacer.BeginBatch();
        Assert.Equal(102, time); // No replay of the 49 overdue windows at once.
        pacer.EndBatch(1);
        pacer.Drain();
        Assert.Equal(103, time);
    }

    [Fact]
    public void SlowNativeTransmitStillFailsCalibrationWithActualElapsedTime()
    {
        double time = 0;
        var pacer = new DataTestLineRatePacer(81274, () => time, () => time += 0.125, default);
        for (var i = 0; i < 250; i++)
        {
            pacer.BeginBatch();
            time += 3; // Driver can only sustain ~2/3 of the target.
            pacer.EndBatch(i == 249 ? 50 : 163);
        }
        pacer.Drain();
        Assert.Equal(750, time);
        Assert.True(RunDataTestStep.CalculateTxDeficitPercent(1000, 40637 * 1538 * 8.0 / time / 1000) > 30);
    }

    [Fact]
    public void BoundaryWaitAndDrainHonorCancellation()
    {
        using var stop = new CancellationTokenSource();
        var pacer = new DataTestLineRatePacer(1000, () => 0, stop.Cancel, stop.Token);
        pacer.BeginBatch();
        pacer.EndBatch(2);
        Assert.Throws<OperationCanceledException>(pacer.Drain);
        Assert.Throws<OperationCanceledException>(pacer.BeginBatch);
    }
}
