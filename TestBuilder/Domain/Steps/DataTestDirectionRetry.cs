using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Domain.Steps;

// One shared budget for measured loss, sender/capture faults and timeouts.
// Configuration is validated by the caller before entering this loop.
internal static class DataTestDirectionRetry
{
    internal const int MaxAttempts = 2;

    internal static async Task<DataTestPortResult> RunAsync(
        Func<CancellationToken, Task<DataTestPortResult>> measure,
        IPAddress source, IPAddress destination, int timeoutMs,
        Action<DataTestPortResult> attemptFinished,
        Func<CancellationToken, Task> waitBeforeRetry,
        CancellationToken cancellationToken)
    {
        var attempts = new List<DataTestPortResult>();
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(timeoutMs);
            DataTestPortResult result;
            try
            {
                result = await measure(timeout.Token);
                timeout.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = DataTestPortResult.Fail($"Таймаут измерения после {timeoutMs} мс.", infrastructureFailure: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            result = result with { AttemptCount = attempt, Attempts = Array.Empty<DataTestPortResult>() };
            if (!result.Passed) result = result with { Error = result.DescribeFailure(source, destination) };
            attempts.Add(result);
            attemptFinished(result);
            if (result.Passed || attempt == MaxAttempts)
                return result with { Attempts = attempts.ToArray() };
            await waitBeforeRetry(cancellationToken);
        }
        throw new InvalidOperationException("Не выполнено ни одной попытки DataTest.");
    }
}
