using System.Diagnostics;
using TestBuilder.Domain.Steps;
using Xunit;

namespace TestBuilder.Tests.StepTests;

public class DataTestSendStartGateTests
{
    [Fact]
    public async Task ClockRemainsStoppedUntilBothSendersArePrepared()
    {
        var clock = new Stopwatch();
        using var gate = new DataTestSendStartGate(clock);
        using var firstReady = new ManualResetEventSlim();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var first = DataTestWorker.Run(() =>
        {
            firstReady.Set();
            gate.Wait(stop.Token);
            return clock.IsRunning;
        });
        firstReady.Wait(stop.Token);
        Assert.False(clock.IsRunning);
        Assert.Equal(0, clock.ElapsedTicks);
        gate.Wait(stop.Token);
        Assert.True(await first.WaitAsync(stop.Token));
        Assert.True(clock.IsRunning);
    }

    [Fact]
    public async Task CancelledPreparationReleasesTheWaitingSender()
    {
        var clock = new Stopwatch();
        using var gate = new DataTestSendStartGate(clock);
        using var firstReady = new ManualResetEventSlim();
        using var stop = new CancellationTokenSource();
        var waiting = DataTestWorker.Run(() =>
        {
            firstReady.Set();
            gate.Wait(stop.Token);
            return true;
        });
        Assert.True(firstReady.Wait(TimeSpan.FromSeconds(3)));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.False(clock.IsRunning);
    }
}
