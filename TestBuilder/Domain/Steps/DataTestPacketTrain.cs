using System;
using System.Threading;

namespace TestBuilder.Domain.Steps;

// Keep packet intervals in QPC instead of the driver's synchronized queue.
// A delayed thread resumes with one packet, without replaying missed slots.
internal static class DataTestPacketTrain
{
    internal static void Send(int count, double packetsPerSecond, Action<int> send,
        Func<double> elapsedMs, Action idle, CancellationToken cancellationToken, Action<double>? waitUntil = null)
    {
        using var pacer = new DataTestLineRatePacer(packetsPerSecond, elapsedMs, idle, cancellationToken, waitUntil);
        for (var sequence = 0; sequence < count; sequence++)
        {
            pacer.BeginBatch();
            send(sequence);
            pacer.EndBatch(1);
        }
        pacer.Drain();
    }
}
