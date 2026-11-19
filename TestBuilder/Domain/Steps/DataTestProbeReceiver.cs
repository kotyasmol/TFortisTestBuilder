using System;
using System.Diagnostics;
using System.Threading;

namespace TestBuilder.Domain.Steps;

internal sealed class DataTestProbeReceiver(ulong runId, int expectedPackets)
{
    internal const int CapturePrefixBytes = 128;
    internal int[] Marks { get; } = new int[expectedPackets];
    private int _received, _duplicates, _unexpected;
    private long _started, _lastReceived;
    internal int Received => Volatile.Read(ref _received);
    internal int Duplicates => Volatile.Read(ref _duplicates);
    internal int Unexpected => Volatile.Read(ref _unexpected);
    internal static int CaptureLength(int frameBytes) => Math.Min(frameBytes, CapturePrefixBytes);
    internal void Start() => Start(Stopwatch.GetTimestamp());
    internal void Start(long timestamp) => Interlocked.CompareExchange(ref _started, timestamp, 0);

    internal void Accept(ReadOnlySpan<byte> packet) => Accept(packet, Stopwatch.GetTimestamp());
    internal void Accept(ReadOnlySpan<byte> packet, long timestamp)
    {
        if (!RunDataTestStep.TryReadProbeSequence(packet, runId, expectedPackets, out var sequence))
        {
            Interlocked.Increment(ref _unexpected);
            return;
        }
        if (Interlocked.Exchange(ref Marks[sequence], 1) != 0)
        {
            Interlocked.Increment(ref _duplicates);
            return;
        }
        // Native capture calls are serial on this handle. Publish the timestamp
        // before the count so draining the expected count observes its endpoint.
        Interlocked.Exchange(ref _lastReceived, timestamp);
        Interlocked.Increment(ref _received);
    }

    internal long ConfirmedElapsedMs(long senderElapsedMs) => ConfirmedElapsedMs(
        senderElapsedMs, Volatile.Read(ref _started), Volatile.Read(ref _lastReceived), Stopwatch.Frequency);

    internal static long ConfirmedElapsedMs(long senderElapsedMs, long started, long lastReceived, long frequency) =>
        Math.Max(senderElapsedMs, started > 0 && lastReceived >= started
            ? (long)Math.Ceiling((lastReceived - started) * 1000.0 / frequency) : 0);
}

internal sealed record DataTestReceiveQuality(int Received, long ConfirmedElapsedMs, bool HostErrors)
{
    internal bool IsHealthy(int sent, long sendElapsedMs) => !HostErrors && Received == sent &&
        ConfirmedElapsedMs <= sendElapsedMs + 50; // One final buffered capture delivery is allowed on warmup only.
    internal string Describe(int sent) => $"RX {Received}/{sent}, подтверждённое время {ConfirmedElapsedMs} мс" +
        (HostErrors ? ", ошибки захвата/карт" : "");
}
