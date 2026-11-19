using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using TestBuilder.Domain.Steps;
using Xunit;

namespace TestBuilder.Tests.StepTests;

public class DataTestLowCpuTests
{
    private static byte[] Packet(ulong id, int sequence, int length = 1514)
    {
        var packet = RunDataTestStep.BuildPacket(new byte[] { 0, 1, 2, 3, 4, 5 }, new byte[] { 0, 6, 7, 8, 9, 10 },
            IPAddress.Parse("192.168.0.2"), IPAddress.Parse("192.168.0.3"), length, 9000);
        RunDataTestStep.WriteProbeIdentity(packet, id, sequence);
        return packet;
    }

    [Theory]
    [InlineData(64, 64)]
    [InlineData(128, 128)]
    [InlineData(1514, 128)]
    public void TruncatedCapturePreservesIdentityWithoutChangingTransmittedFrame(int length, int captureLength)
    {
        var packet = Packet(123, 9, length);
        var receiver = new DataTestProbeReceiver(123, 10);
        receiver.Accept(packet.AsSpan(0, DataTestProbeReceiver.CaptureLength(packet.Length)));
        Assert.Equal(captureLength, DataTestProbeReceiver.CaptureLength(packet.Length));
        Assert.Equal(1, receiver.Received);
        Assert.Equal(1, receiver.Marks[9]);
        Assert.Equal(length, packet.Length);
        Assert.Equal(length + 24, RunDataTestStep.CalculateWireSizeBytes(packet.Length));
    }

    [Fact]
    public void LateDuplicateForeignOrShortPacketDoesNotExtendTheConfirmedWindowOrUniqueCount()
    {
        var receiver = new DataTestProbeReceiver(123, 2);
        var start = Stopwatch.Frequency;
        receiver.Start(start);
        receiver.Accept(Packet(123, 1), start + Stopwatch.Frequency / 2);
        receiver.Accept(Packet(123, 1), start + Stopwatch.Frequency * 10);
        receiver.Accept(Packet(124, 0), start + Stopwatch.Frequency * 11);
        receiver.Accept(Packet(123, 0).AsSpan(0, 57), start + Stopwatch.Frequency * 12);
        Assert.Equal(1, receiver.Received);
        Assert.Equal(1, receiver.Duplicates);
        Assert.Equal(2, receiver.Unexpected);
        Assert.Equal(500, receiver.ConfirmedElapsedMs(1));
    }

    [Theory]
    [InlineData(500, 1000, 2000, 1000)]
    [InlineData(1200, 1000, 2000, 1200)]
    [InlineData(500, 1000, 1501, 501)]
    [InlineData(500, 1000, 0, 500)]
    public void DriverAcceptanceCannotShortenTheObservedReceiveWindow(long sendMs, long start, long last, long expected)
    {
        var confirmed = DataTestProbeReceiver.ConfirmedElapsedMs(sendMs, start, last, 1000);
        Assert.Equal(expected, confirmed);
        var mbps = 40637 * RunDataTestStep.CalculateWireSizeBytes(1514) * 8.0 / confirmed / 1000;
        if (expected >= 1000) Assert.True(RunDataTestStep.CalculateTxDeficitPercent(1000, mbps) >= 49);
    }

    [Fact]
    public void QueueWaitSleepsMostOfWindowAndKeepsOversleepVisible()
    {
        double time = 0;
        double paused = 0;
        DataTestDeadlineWait.Wait(10, () => time, delay =>
        {
            paused += delay;
            time += delay + 0.5; // Scheduling delay is real elapsed time.
        }, default);
        Assert.Equal(9.75, paused);
        Assert.Equal(10.25, time);
    }

    [Fact]
    public void SleepingQueueWaitChecksCancellationBeforeAndAfterPause()
    {
        using var stop = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => DataTestDeadlineWait.Wait(10,
            () => 0, _ => stop.Cancel(), stop.Token));
    }

    [WindowsTimerFact]
    public void HighResolutionTimerCanBeCreatedWaitedAndDisposedOnWindows()
    {
        using var wait = new DataTestDeadlineWait();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var clock = Stopwatch.StartNew();
        wait.Wait(10, () => clock.Elapsed.TotalMilliseconds, stop.Token);
        Assert.True(clock.Elapsed.TotalMilliseconds >= 10);
    }

    [Fact]
    public async Task PrefillWaitsForSeveralPreparedQueuesAndDoesNotConsumeWholeTest()
    {
        var channel = Channel.CreateBounded<int>(4);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var producer = Task.Run(async () =>
        {
            for (var i = 0; i < 100; i++) await channel.Writer.WriteAsync(i, stop.Token);
            channel.Writer.Complete();
        });
        var ready = DataTestQueuePrefill.Read(channel.Reader, 4, _ => { }, stop.Token);
        Assert.Equal(new[] { 0, 1, 2, 3 }, ready);
        Assert.False(producer.IsCompleted);
        stop.Cancel(); // A producer blocked by the bounded reserve must be joinable.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => producer);
    }

    [Fact]
    public void ShortCompletedTestDoesNotDeadlockWaitingForFullReserve()
    {
        var channel = Channel.CreateUnbounded<int>();
        channel.Writer.TryWrite(7);
        channel.Writer.Complete();
        Assert.Equal(new[] { 7 }, DataTestQueuePrefill.Read(channel.Reader, 4, _ => { }, default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedOrCancelledPreparationDisposesItsPartialReserve(bool fault)
    {
        var channel = Channel.CreateUnbounded<int>();
        channel.Writer.TryWrite(7);
        var disposed = new List<int>();
        using var stop = new CancellationTokenSource();
        if (fault) channel.Writer.Complete(new InvalidOperationException("queue preparation failed"));
        else stop.Cancel();
        if (fault) Assert.Throws<InvalidOperationException>(() => DataTestQueuePrefill.Read(channel.Reader, 4, disposed.Add, stop.Token));
        else Assert.ThrowsAny<OperationCanceledException>(() => DataTestQueuePrefill.Read(channel.Reader, 4, disposed.Add, stop.Token));
        // Cancellation before reading leaves ownership in the channel; faults
        // after reading dispose the reserve owned by the prefill operation.
        if (fault) Assert.Equal(new[] { 7 }, disposed);
        else Assert.Empty(disposed);
    }

    [Fact]
    public async Task TwoSendersMarkOneCommonMeasurementStartAfterBothArePrepared()
    {
        var marks = 0;
        var clock = new Stopwatch();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var gate = new DataTestSendStartGate(clock, () => Interlocked.Increment(ref marks));
        var first = DataTestWorker.Run(() => { gate.Wait(stop.Token); return marks; });
        Assert.False(clock.IsRunning);
        gate.Wait(stop.Token);
        Assert.Equal(1, await first.WaitAsync(stop.Token));
        Assert.Equal(1, marks);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SecondSenderCanBeChosenForReceiveHealthEvenWhenFirstTxReachesTarget(bool hostDrops)
    {
        var calls = new List<int>();
        var chosen = await DataTestSenderCalibration.SelectAsync((lanes, _) =>
        {
            calls.Add(lanes);
            return Task.FromResult(new PacedSendResult(40637, 500, "native")
            {
                ReceiveQuality = new DataTestReceiveQuality(lanes == 1 ? 40000 : 40637, 500, lanes == 1 && hostDrops)
            });
        }, 40637, 1514, 1000, 2, true, _ => { }, default);
        Assert.Equal(2, chosen);
        Assert.Equal(new[] { 1, 2 }, calls);
    }

    [Fact]
    public async Task FasterTxWithCaptureDropsDoesNotWinOverSingleSenderWithoutHostErrors()
    {
        var selected = await DataTestSenderCalibration.SelectAsync((lanes, _) =>
            Task.FromResult(new PacedSendResult(40637, lanes == 1 ? 509 : 500, "native")
            {
                ReceiveQuality = new DataTestReceiveQuality(lanes == 1 ? 40000 : 40637, 509, lanes == 2)
            }), 40637, 1514, 1000, 2, true, _ => { }, default);
        Assert.Equal(1, selected); // Ordinary path loss remains for the strict main measurement.
    }

    [Fact]
    public async Task HealthySingleSenderDoesNotNeedSecondProbe()
    {
        var calls = 0;
        var selected = await DataTestSenderCalibration.SelectAsync((_, _) =>
        {
            calls++;
            return Task.FromResult(new PacedSendResult(40637, 500, "native")
                { ReceiveQuality = new DataTestReceiveQuality(40637, 520, false) });
        }, 40637, 1514, 1000, 2, true, _ => { }, default);
        Assert.Equal(1, selected);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task NeitherHostFailedWarmupCanBeAcceptedOrDeferred()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DataTestSenderCalibration.SelectAsync((_, _) =>
            Task.FromResult(new PacedSendResult(40637, 500, "native")
                { ReceiveQuality = new DataTestReceiveQuality(40637, 500, true) }),
            40637, 1514, 1000, 2, true, _ => { }, default, qualifyInLongerMeasurement: true));
        Assert.Contains("ошибки захвата/карт", error.Message);
    }

    private sealed class WindowsTimerFactAttribute : FactAttribute
    {
        public WindowsTimerFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Requires Windows waitable timer."; }
    }
}
