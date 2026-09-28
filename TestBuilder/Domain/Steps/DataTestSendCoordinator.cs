using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Domain.Steps;

// Two independently opened injection handles keep the NIC fed while either
// native transmit call waits for completion. Each lane owns disjoint sequences.
internal static class DataTestSendCoordinator
{
    internal static int CalculateLanePacketCount(int totalPackets, int lane, int laneCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalPackets);
        ArgumentOutOfRangeException.ThrowIfLessThan(laneCount, 1);
        if (lane < 0 || lane >= laneCount) throw new ArgumentOutOfRangeException(nameof(lane));
        return (int)(((long)totalPackets + laneCount - 1 - lane) / laneCount);
    }

    internal static async Task<PacedSendResult> RunParallelSendersAsync(
        Func<int, CancellationToken, Task<PacedSendResult>> sendLane,
        int expectedPackets, int durationMs, Stopwatch clock, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<PacedSendResult> StartLane(int lane) => Task.Run(async () =>
        {
            try
            {
                var result = await sendLane(lane, stop.Token);
                var expected = CalculateLanePacketCount(expectedPackets, lane, 2);
                if (result.SentPackets != expected)
                    throw new InvalidOperationException(
                        $"DataTest: очередь {lane + 1} передала {result.SentPackets} из {expected} пакетов.");
                return result;
            }
            catch
            {
                stop.Cancel();
                throw;
            }
        }, CancellationToken.None);

        // Await both lanes before disposing either handle, including on failure.
        var results = await Task.WhenAll(StartLane(0), StartLane(1));
        await WaitForSendDeadlineAsync(clock, durationMs, cancellationToken);
        return new PacedSendResult(results[0].SentPackets + results[1].SentPackets,
            Math.Max(1, clock.ElapsedMilliseconds), "PcapParallelQueues");
    }

    internal static async Task WaitForSendDeadlineAsync(
        Stopwatch clock, double dueMs, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        double remaining;
        while ((remaining = dueMs - clock.Elapsed.TotalMilliseconds) > 0)
            await Task.Delay((int)Math.Max(1, Math.Ceiling(remaining)), cancellationToken);
    }
}
