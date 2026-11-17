using System;
using System.Diagnostics;
using System.Threading;

namespace TestBuilder.Domain.Steps;

// Prepare both lanes before timing the first injection. Once released, the
// shared clock includes every native wait, queue starvation and sender stall.
internal sealed class DataTestSendStartGate(Stopwatch clock) : IDisposable
{
    private readonly Barrier _ready = new(2, _ => clock.Start());

    internal void Wait(CancellationToken cancellationToken) => _ready.SignalAndWait(cancellationToken);

    public void Dispose() => _ready.Dispose();
}
