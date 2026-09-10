using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Services.Logging;

namespace TestBuilder.Services.Http;

/// <summary>
/// Runs the entire browser lifecycle off the caller's thread, including synchronous
/// process startup, tree termination and temporary-profile deletion.
/// </summary>
internal sealed class HeadlessBrowserAttemptRunner
{
    private readonly ILogger _logger;
    private Task<HttpRequestResult>? _inFlight;
    private string _stage = string.Empty;

    public HeadlessBrowserAttemptRunner(ILogger logger) => _logger = logger;

    public async Task<HttpRequestResult> RunAsync(
        Func<CancellationToken, Action<string>, Task<HttpRequestResult>> operation,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // A timed-out native call may still be returning or cleaning up. Do not
        // accumulate browser processes while that previous lifecycle is unfinished.
        if (_inFlight is { IsCompleted: false })
        {
            return HttpRequestResult.Failure(
                $"Предыдущая попытка браузера ещё завершается: {Volatile.Read(ref _stage)}. " +
                "Новый браузер пока не запускается.", TimeSpan.Zero);
        }

        var stopwatch = Stopwatch.StartNew();
        var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptCts.CancelAfter(timeout);
        Volatile.Write(ref _stage, "запуск фоновой задачи");
        var work = Task.Run(() => operation(attemptCts.Token, ReportStage), CancellationToken.None);
        _inFlight = work;
        _ = ObserveCompletionAsync(work, attemptCts);

        try
        {
            // CancellationToken alone cannot interrupt synchronous OS calls. Bound
            // the wait as well, so Stop/timeout returns even during process cleanup.
            return await work.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            return HttpRequestResult.Failure(
                $"Таймаут попытки браузера: {(int)timeout.TotalMilliseconds} мс. " +
                $"Этап: {Volatile.Read(ref _stage)}. Завершение браузера продолжается в фоне.",
                stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            return HttpRequestResult.Failure(
                $"Ошибка браузера на этапе '{Volatile.Read(ref _stage)}': {ex.Message}",
                stopwatch.Elapsed);
        }
    }

    private void ReportStage(string stage)
    {
        Volatile.Write(ref _stage, stage);
        _logger.Info($"[INFO] Selftest browser: {stage}.");
    }

    private static async Task ObserveCompletionAsync(Task<HttpRequestResult> work, CancellationTokenSource attemptCts)
    {
        try
        {
            await work.ConfigureAwait(false);
        }
        catch
        {
            // The caller may have already returned on timeout/cancellation. Observe
            // a late fault without replacing that outcome or raising an unobserved fault.
        }
        finally
        {
            // The token must remain usable by a late-starting worker until it exits.
            attemptCts.Dispose();
        }
    }
}
