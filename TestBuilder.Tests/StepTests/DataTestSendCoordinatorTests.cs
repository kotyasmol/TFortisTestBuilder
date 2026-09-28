using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Steps;
using Xunit;

namespace TestBuilder.Tests.StepTests;

public class DataTestSendCoordinatorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(406372)]
    [InlineData(406373)]
    public void LanesCoverEverySequenceExactlyOnce(int total)
    {
        var sequences = Enumerable.Range(0, 2).SelectMany(lane =>
            Enumerable.Range(0, DataTestSendCoordinator.CalculateLanePacketCount(total, lane, 2))
                .Select(index => index * 2 + lane)).ToArray();
        Assert.Equal(total, sequences.Length);
        Assert.Equal(Enumerable.Range(0, total), sequences.OrderBy(value => value));
    }

    [Fact]
    public void PartitionDoesNotOverflowAtMaximumPacketCount()
    {
        Assert.Equal(1073741824, DataTestSendCoordinator.CalculateLanePacketCount(int.MaxValue, 0, 2));
        Assert.Equal(1073741823, DataTestSendCoordinator.CalculateLanePacketCount(int.MaxValue, 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DataTestSendCoordinator.CalculateLanePacketCount(5, 2, 2));
    }

    [Fact]
    public async Task FastCompletionStillObservesRequestedDuration()
    {
        var clock = Stopwatch.StartNew();
        var result = await DataTestSendCoordinator.RunParallelSendersAsync(
            (lane, _) => Task.FromResult(new PacedSendResult(lane == 0 ? 2 : 1, 1, "fake")),
            3, 30, clock, CancellationToken.None);
        Assert.Equal(3, result.SentPackets);
        Assert.True(result.ElapsedMs >= 30);
        Assert.True(clock.ElapsedMilliseconds >= result.ElapsedMs);
    }

    [Fact]
    public async Task SlowSenderIsIncludedInActualElapsedTime()
    {
        var clock = Stopwatch.StartNew();
        var result = await DataTestSendCoordinator.RunParallelSendersAsync(async (lane, token) =>
        {
            if (lane == 1) await Task.Delay(60, token);
            return new PacedSendResult(1, 1, "fake");
        }, 2, 10, clock, CancellationToken.None);
        Assert.True(result.ElapsedMs >= 60);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureOrShortSendCancelsPeerAndWaitsForItsCleanup(bool shortSend)
    {
        var peerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var peerCleanedUp = false;
        var run = DataTestSendCoordinator.RunParallelSendersAsync(async (lane, token) =>
        {
            if (lane == 0)
            {
                await peerStarted.Task;
                if (shortSend) return new PacedSendResult(0, 1, "fake");
                throw new InvalidOperationException("injection failed");
            }
            peerStarted.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
                return new PacedSendResult(1, 1, "fake");
            }
            finally
            {
                // Cleanup must finish before the coordinator returns to close handles.
                await Task.Delay(10);
                peerCleanedUp = true;
            }
        }, 2, 10, Stopwatch.StartNew(), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(peerCleanedUp);
    }

    [Fact]
    public async Task UserCancellationStopsAndJoinsBothSenders()
    {
        using var stop = new CancellationTokenSource();
        var started = 0;
        var cleanedUp = 0;
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = DataTestSendCoordinator.RunParallelSendersAsync(async (_, token) =>
        {
            if (Interlocked.Increment(ref started) == 2) bothStarted.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
                return new PacedSendResult(1, 1, "fake");
            }
            finally { Interlocked.Increment(ref cleanedUp); }
        }, 2, 10, Stopwatch.StartNew(), stop.Token);
        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(2, cleanedUp);
    }
}
