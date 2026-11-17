using System;
using SharpPcap.LibPcap;

namespace TestBuilder.Domain.Steps;

// SharpPcap 6.3.1 starts its capture loop with Task.Run. Keep the same
// native loop and StopCapture lifecycle, on a dedicated worker instead.
internal sealed class DataTestCaptureDevice(PcapInterface adapter) : LibPcapLiveDevice(adapter)
{
    public override void StartCapture()
    {
        if (Started) return;
        if (!Opened) throw new InvalidOperationException("Карта захвата DataTest не открыта.");
        var token = threadCancellationTokenSource.Token;
        captureThread = DataTestWorker.Run(() =>
        {
            CaptureThread(token);
            return true;
        });
    }

    internal void ThrowIfFailed()
    {
        if (captureThread?.IsFaulted == true) captureThread.GetAwaiter().GetResult();
        if (captureThread?.IsCompleted == true)
            throw new InvalidOperationException("Захват DataTest завершился до окончания измерения.");
    }
}
