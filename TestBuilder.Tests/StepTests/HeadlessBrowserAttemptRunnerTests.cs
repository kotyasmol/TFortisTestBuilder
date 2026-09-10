using System.Diagnostics;
using TestBuilder.Services.Http;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class HeadlessBrowserAttemptRunnerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockingStartupOrCleanupDoesNotBlockTheCallingUiThread(bool blockInCleanup)
    {
        var runner = new HeadlessBrowserAttemptRunner(NullLogger.Instance);
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationReturned = new TaskCompletionSource<Task<HttpRequestResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callerThreadId = 0;
        var workerThreadId = 0;
        SynchronizationContext? workerContext = null;

        async Task<HttpRequestResult> Operation(CancellationToken token, Action<string> reportStage)
        {
            if (blockInCleanup)
                await Task.Yield();
            try
            {
                if (!blockInCleanup)
                    Block();
                return HttpRequestResult.Success(0, "page", TimeSpan.Zero);
            }
            finally
            {
                if (blockInCleanup)
                    Block();
            }

            void Block()
            {
                workerThreadId = Environment.CurrentManagedThreadId;
                workerContext = SynchronizationContext.Current;
                reportStage(blockInCleanup ? "закрытие браузера" : "запуск браузера");
                started.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Test did not release the simulated native call.");
            }
        }

        var caller = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            callerThreadId = Environment.CurrentManagedThreadId;
            var execution = runner.RunAsync(Operation, TimeSpan.FromSeconds(4), CancellationToken.None);
            // A UI callback must regain control even while a native operation is blocked.
            invocationReturned.TrySetResult(execution);
        }) { IsBackground = true };

        try
        {
            caller.Start();
            var execution = await invocationReturned.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.NotEqual(callerThreadId, workerThreadId);
            Assert.Null(workerContext);
            Assert.False(execution.IsCompleted);
            release.Set();
            var result = await execution.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("page", result.Body);
            Assert.Equal(string.Empty, result.ErrorMessage);
        }
        finally
        {
            release.Set();
            caller.Join(TimeSpan.FromSeconds(2));
        }
    }

    [Theory]
    [InlineData("запуск браузера")]
    [InlineData("удаление временного профиля браузера")]
    public async Task DeadlineBoundsUncooperativeNativeCallAndPreventsOverlappingBrowsers(string stage)
    {
        var runner = new HeadlessBrowserAttemptRunner(NullLogger.Instance);
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        Task<HttpRequestResult> Operation(CancellationToken token, Action<string> reportStage)
        {
            using var registration = token.Register(() => cancellationObserved.TrySetResult());
            Interlocked.Increment(ref calls);
            reportStage(stage);
            started.TrySetResult();
            try
            {
                release.Wait(TimeSpan.FromSeconds(5)); // Native calls need not obey cancellation.
                return Task.FromResult(HttpRequestResult.Success(0, "late page", TimeSpan.Zero));
            }
            finally
            {
                finished.TrySetResult();
            }
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var execution = runner.RunAsync(Operation, TimeSpan.FromMilliseconds(400), CancellationToken.None);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var result = await execution.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.InRange(stopwatch.ElapsedMilliseconds, 250, 2000);
            Assert.Contains("Таймаут", result.ErrorMessage);
            Assert.Contains(stage, result.ErrorMessage);
            Assert.Equal(string.Empty, result.Body); // Never use a late DOM after the timeout.
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var next = await runner.RunAsync(Operation, TimeSpan.FromMilliseconds(400), CancellationToken.None);
            Assert.Contains("ещё завершается", next.ErrorMessage);
            Assert.Equal(1, calls);
        }
        finally
        {
            release.Set();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task StopReturnsWhileCleanupIsBlockedAndCancelsTheWorker()
    {
        var runner = new HeadlessBrowserAttemptRunner(NullLogger.Instance);
        using var cancellation = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var execution = runner.RunAsync((token, reportStage) =>
        {
            using var registration = token.Register(() => cancellationObserved.TrySetResult());
            reportStage("закрытие браузера");
            started.TrySetResult();
            try
            {
                release.Wait(TimeSpan.FromSeconds(5));
                return Task.FromResult(HttpRequestResult.Success(0, "late page", TimeSpan.Zero));
            }
            finally
            {
                finished.TrySetResult();
            }
        }, TimeSpan.FromSeconds(4), cancellation.Token);

        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution.WaitAsync(TimeSpan.FromSeconds(2)));
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(finished.Task.IsCompleted);
        }
        finally
        {
            release.Set();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task BrowserStartupFailureIncludesStageAndAllowsAnotherAttempt()
    {
        var runner = new HeadlessBrowserAttemptRunner(NullLogger.Instance);
        var first = await runner.RunAsync((_, reportStage) =>
        {
            reportStage("запуск браузера");
            throw new InvalidOperationException("process could not start");
        }, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Contains("запуск браузера", first.ErrorMessage);
        Assert.Contains("process could not start", first.ErrorMessage);

        var expected = HttpRequestResult.Success(0, "page", TimeSpan.Zero);
        var second = await runner.RunAsync((_, _) => Task.FromResult(expected), TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.Same(expected, second);
    }

    [Fact]
    public async Task PreCancelledRunDoesNotStartBrowser()
    {
        var runner = new HeadlessBrowserAttemptRunner(NullLogger.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var called = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync((_, _) =>
        {
            called = true;
            return Task.FromResult(HttpRequestResult.Success(0, "page", TimeSpan.Zero));
        }, TimeSpan.FromSeconds(2), cancellation.Token));

        Assert.False(called);
    }
}
