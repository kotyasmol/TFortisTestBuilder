using TestBuilder.Services.Http;

namespace TestBuilder.Tests.StepTests;

public class BrowserAttemptCoordinatorTests
{
    [Fact]
    public async Task ConcurrentAttempts_WaitUntilPreviousCleanupFinishes()
    {
        var coordinator = new BrowserAttemptCoordinator();
        var cleaning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = false;
        var cleanupFinished = false;
        var first = coordinator.RunAsync(TimeSpan.FromSeconds(5), CancellationToken.None, async _ =>
        {
            try { return 1; }
            finally { cleaning.SetResult(); await release.Task; cleanupFinished = true; }
        });
        await cleaning.Task;
        var second = coordinator.RunAsync(TimeSpan.FromSeconds(5), CancellationToken.None, _ =>
        {
            secondStarted = true;
            Assert.True(cleanupFinished);
            return Task.FromResult(2);
        });
        Assert.False(secondStarted);
        release.SetResult();
        Assert.Equal(new[] { 1, 2 }, await Task.WhenAll(first, second));
    }

    [Fact]
    public async Task CancellationAndDeadlineInQueue_DoNotLaunchAnotherAttempt()
    {
        var coordinator = new BrowserAttemptCoordinator();
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = coordinator.RunAsync(TimeSpan.FromSeconds(5), CancellationToken.None, _ => release.Task);
        using var stop = new CancellationTokenSource();
        var starts = 0;
        Task<int> Unexpected(TimeSpan _) { starts++; return Task.FromResult(0); }
        var cancelled = coordinator.RunAsync(TimeSpan.FromSeconds(5), stop.Token, Unexpected);
        stop.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
            await Assert.ThrowsAsync<TimeoutException>(() => coordinator.RunAsync(TimeSpan.FromMilliseconds(30), CancellationToken.None, Unexpected));
            Assert.Equal(0, starts);
        }
        finally { release.SetResult(1); await active; }
        Assert.Equal(0, await coordinator.RunAsync(TimeSpan.FromSeconds(1), CancellationToken.None, Unexpected));
    }

    [Fact]
    public async Task UnconfirmedCleanup_BlocksQueuedAndFutureAttempts()
    {
        var coordinator = new BrowserAttemptCoordinator();
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = coordinator.RunAsync<int>(TimeSpan.FromSeconds(5), CancellationToken.None, async _ =>
        {
            await release.Task;
            throw new BrowserCleanupException("still running", new TimeoutException());
        });
        var starts = 0;
        Task<int> Unexpected(TimeSpan _) { starts++; return Task.FromResult(0); }
        var queued = coordinator.RunAsync(TimeSpan.FromSeconds(5), CancellationToken.None, Unexpected);
        release.SetResult(1);
        await Assert.ThrowsAsync<BrowserCleanupException>(() => active);
        await Assert.ThrowsAsync<BrowserCleanupException>(() => queued);
        await Assert.ThrowsAsync<BrowserCleanupException>(() => coordinator.RunAsync(TimeSpan.FromSeconds(1), CancellationToken.None, Unexpected));
        Assert.Equal(0, starts);
    }

    [Fact]
    public async Task OrdinaryFailure_ReleasesSlotForRetry()
    {
        var coordinator = new BrowserAttemptCoordinator();
        await Assert.ThrowsAsync<IOException>(() => coordinator.RunAsync<int>(TimeSpan.FromSeconds(1), CancellationToken.None,
            _ => throw new IOException("failed to start")));
        Assert.Equal(7, await coordinator.RunAsync(TimeSpan.FromSeconds(1), CancellationToken.None, _ => Task.FromResult(7)));
    }
}
