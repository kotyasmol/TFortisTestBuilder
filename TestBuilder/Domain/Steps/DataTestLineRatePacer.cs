using System;
using System.Diagnostics;
using System.Threading;

namespace TestBuilder.Domain.Steps;

// At full link speed the NIC serializes frames. Bound each injection to 2 ms
// of wire time and pace queue boundaries, rather than delaying every frame
// again in Npcap. A slow native call remains visible in the elapsed time.
internal sealed class DataTestLineRatePacer(
    double packetsPerSecond, Func<double> elapsedMs, Action idle, CancellationToken cancellationToken)
{
    internal const int WindowMs = 2;

    internal static bool CanUse(int targetMbps, double linkMbps) =>
        targetMbps > 100 && targetMbps == linkMbps;

    private double _nextQueueMs;
    private double _batchStartMs;

    internal DataTestLineRatePacer(double packetsPerSecond, Stopwatch clock, CancellationToken cancellationToken)
        : this(packetsPerSecond, () => clock.Elapsed.TotalMilliseconds, () => Thread.SpinWait(32), cancellationToken) { }

    internal void BeginBatch()
    {
        Drain();
        // After a scheduler stall, do not replay overdue windows in a burst.
        // Keep the stall in the measured duration and restart at the current time.
        _batchStartMs = elapsedMs();
    }

    internal void EndBatch(int packets) => _nextQueueMs = _batchStartMs + packets * 1000.0 / packetsPerSecond;

    internal void Drain() => WaitUntil(_nextQueueMs, elapsedMs, idle, cancellationToken);

    // No Task.Delay: Windows timer quantization can exceed an entire window.
    internal static void WaitUntil(double deadlineMs, Func<double> elapsedMs, Action idle,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (elapsedMs() < deadlineMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            idle();
        }
    }
}
