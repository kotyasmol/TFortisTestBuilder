using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Services.Http;

// One coordinator for the application, not one per step, graph or retry.
internal sealed class BrowserAttemptCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Exception? _cleanupFailure;

    public async Task<T> RunAsync<T>(TimeSpan timeout, CancellationToken token,
        Func<TimeSpan, Task<T>> attempt)
    {
        var clock = Stopwatch.StartNew();
        if (!await _gate.WaitAsync(timeout, token).ConfigureAwait(false))
            throw new TimeoutException("Истекло время ожидания освобождения браузера selftest.");
        try
        {
            token.ThrowIfCancellationRequested();
            if (_cleanupFailure != null)
                throw new BrowserCleanupException("Новые запуски Chrome заблокированы: завершение предыдущей группы процессов не подтверждено. Перезапустите приложение.", _cleanupFailure);
            var remaining = timeout - clock.Elapsed;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException("Истекло время ожидания освобождения браузера selftest.");
            try { return await attempt(remaining).ConfigureAwait(false); }
            catch (BrowserCleanupException ex) { _cleanupFailure = ex; throw; }
        }
        finally { _gate.Release(); }
    }
}

internal sealed class BrowserCleanupException : Domain.Execution.ExecutionCleanupException
{
    public BrowserCleanupException(string message, Exception inner) : base(message, inner) { }
}
