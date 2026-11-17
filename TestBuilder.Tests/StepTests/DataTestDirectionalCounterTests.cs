using TestBuilder.Domain.Steps;
using Xunit;

namespace TestBuilder.Tests.StepTests;

public class DataTestDirectionalCounterTests
{
    [Fact]
    public void Apk0306SenderReceiveErrorsDoNotInvalidateForwardProbes()
    {
        // 17.11 10:29:11 and 10:29:17: 192.168.0.6 -> .7,
        // TX=RX=40637, capture drops=0, ~99.9 Mbps. Only the sender's
        // opposite (receive) direction accumulated two errors and discards.
        var sender = new DataTestHostCounters(true, 2, 2, 0, 0);
        var receiver = new DataTestHostCounters(true, 0, 0, 0, 0);
        Assert.False(sender.HasErrorsSendingTo(receiver));
        Assert.True(sender.HasErrors); // Remains visible in diagnostic logs.
        Assert.True(receiver.HasErrorsSendingTo(sender)); // Relevant for B -> A.
    }

    [Theory]
    [InlineData(1, 0, 0, 0, false)]
    [InlineData(0, 1, 0, 0, false)]
    [InlineData(0, 0, 1, 0, true)]
    [InlineData(0, 0, 0, 1, true)]
    public void SenderFailureUsesTransmitCounters(long rxDrops, long rxErrors, long txDrops, long txErrors, bool fail)
    {
        var sender = new DataTestHostCounters(true, rxDrops, rxErrors, txDrops, txErrors);
        Assert.Equal(fail, sender.HasErrorsSendingTo(new(true, 0, 0, 0, 0)));
    }

    [Theory]
    [InlineData(1, 0, 0, 0, true)]
    [InlineData(0, 1, 0, 0, true)]
    [InlineData(0, 0, 1, 0, false)]
    [InlineData(0, 0, 0, 1, false)]
    public void ReceiverFailureUsesReceiveCounters(long rxDrops, long rxErrors, long txDrops, long txErrors, bool fail)
    {
        var receiver = new DataTestHostCounters(true, rxDrops, rxErrors, txDrops, txErrors);
        Assert.Equal(fail, new DataTestHostCounters(true, 0, 0, 0, 0).HasErrorsSendingTo(receiver));
    }

    [Fact]
    public void UnavailableStatisticsDoNotHideAnAvailablePeerFailure()
    {
        var unknown = default(DataTestHostCounters);
        Assert.False(unknown.HasErrorsSendingTo(unknown));
        Assert.True(new DataTestHostCounters(true, 0, 0, 1, 0).HasErrorsSendingTo(unknown));
        Assert.True(unknown.HasErrorsSendingTo(new(true, 0, 1, 0, 0)));
    }
}
