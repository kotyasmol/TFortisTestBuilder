using System;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Domain.Steps;

internal static class DataTestAddressLearning
{
    // A receive-only peer may never have sent a frame since the DUT booted.
    // Teach both source MACs before the timed load, otherwise unknown unicast
    // can be flooded into slower ports and backpressure the gigabit sender.
    internal static async Task PrepareAsync(byte[] sourceMac, byte[] destinationMac,
        Action<byte[]> sendForward, Action<byte[]> sendReverse, CancellationToken cancellationToken)
    {
        var forward = CreateFrame(sourceMac, destinationMac);
        var reverse = CreateFrame(destinationMac, sourceMac);
        for (var pass = 0; pass < 2; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sendReverse(reverse);
            cancellationToken.ThrowIfCancellationRequested();
            sendForward(forward);
            if (pass == 0) await Task.Delay(10, cancellationToken);
        }
    }

    internal static byte[] CreateFrame(byte[] sourceMac, byte[] destinationMac)
    {
        if (sourceMac is null || sourceMac.Length != 6)
            throw new ArgumentException("Source MAC must contain six bytes.", nameof(sourceMac));
        if (destinationMac is null || destinationMac.Length != 6)
            throw new ArgumentException("Destination MAC must contain six bytes.", nameof(destinationMac));
        // Minimum Ethernet frame without FCS. Local experimental EtherType:
        // no UDP reply, ARP cache change, or dependency on the host firewall.
        var frame = new byte[60];
        destinationMac.CopyTo(frame, 0);
        sourceMac.CopyTo(frame, 6);
        frame[12] = 0x88;
        frame[13] = 0xB5;
        return frame;
    }
}
