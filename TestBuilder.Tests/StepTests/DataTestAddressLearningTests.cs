using TestBuilder.Domain.Steps;
using Xunit;

namespace TestBuilder.Tests.StepTests;

public class DataTestAddressLearningTests
{
    private static readonly byte[] Source = { 0x02, 1, 2, 3, 4, 5 };
    private static readonly byte[] Destination = { 0x02, 6, 7, 8, 9, 10 };

    [Fact]
    public async Task ReceiveOnlyPeerIsLearnedBeforeFirstForwardFrame()
    {
        var learned = new HashSet<string>();
        var forwardFrames = 0;
        await DataTestAddressLearning.PrepareAsync(Source, Destination, frame =>
        {
            Assert.Contains(Convert.ToHexString(frame.AsSpan(0, 6)), learned);
            learned.Add(Convert.ToHexString(frame.AsSpan(6, 6)));
            forwardFrames++;
        }, frame => learned.Add(Convert.ToHexString(frame.AsSpan(6, 6))), default);
        Assert.Equal(2, forwardFrames);
        Assert.Contains(Convert.ToHexString(Source), learned);
        Assert.Contains(Convert.ToHexString(Destination), learned);
    }

    [Fact]
    public void LearningFrameIsEthernetOnlyAndDoesNotChangeInputAddresses()
    {
        var source = Source.ToArray();
        var destination = Destination.ToArray();
        var frame = DataTestAddressLearning.CreateFrame(source, destination);
        Assert.Equal(60, frame.Length);
        Assert.Equal(destination, frame[..6]);
        Assert.Equal(source, frame[6..12]);
        Assert.Equal(new byte[] { 0x88, 0xB5 }, frame[12..14]);
        frame[0] ^= 0xFF;
        Assert.Equal(Source, source);
        Assert.Equal(Destination, destination);
    }

    [Fact]
    public async Task FailedReverseInjectionCannotProceedToMeasurementPreparation()
    {
        var forwardCalled = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DataTestAddressLearning.PrepareAsync(Source, Destination, _ => forwardCalled = true,
                _ => throw new InvalidOperationException("injection failed"), default));
        Assert.False(forwardCalled);
    }

    [Fact]
    public async Task CancellationBetweenFramesStopsFurtherInjection()
    {
        using var stop = new CancellationTokenSource();
        var forwardCalled = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DataTestAddressLearning.PrepareAsync(Source, Destination, _ => forwardCalled = true,
                _ => stop.Cancel(), stop.Token));
        Assert.False(forwardCalled);
    }
}
