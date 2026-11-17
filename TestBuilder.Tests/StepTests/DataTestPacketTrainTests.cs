using TestBuilder.Domain.Steps;

namespace TestBuilder.Tests.StepTests;

public class DataTestPacketTrainTests
{
    [Fact]
    public void PacketIntervalsAndFinalWireTimeArePreserved()
    {
        double time = 0;
        var sent = new List<(int Sequence, double Time)>();
        DataTestPacketTrain.Send(4, 1000, sequence =>
        {
            sent.Add((sequence, time));
            time += 0.125; // Fast native completion is not the end of wire time.
        }, () => time, () => time += 0.125, default);
        Assert.Equal(new[] { (0, 0.0), (1, 1.0), (2, 2.0), (3, 3.0) }, sent);
        Assert.Equal(4, time);
    }

    [Fact]
    public void SchedulerStallRemainsInDurationWithoutACatchUpBurst()
    {
        double time = 0;
        var starts = new List<double>();
        DataTestPacketTrain.Send(4, 1000, sequence =>
        {
            starts.Add(time);
            if (sequence == 0) time += 20;
        }, () => time, () => time += 0.125, default);
        Assert.Equal(new[] { 0.0, 20.0, 21.0, 22.0 }, starts);
        Assert.Equal(23, time);
    }

    [Fact]
    public void NativeFailureStopsTheTrain()
    {
        var sent = 0;
        Assert.Throws<IOException>(() => DataTestPacketTrain.Send(4, 1000, _ =>
        {
            sent++;
            throw new IOException("native send failed");
        }, () => 0, () => { }, default));
        Assert.Equal(1, sent);
    }

    [Fact]
    public void CancellationDuringPacketIntervalDoesNotSendAnotherPacket()
    {
        using var stop = new CancellationTokenSource();
        var sent = 0;
        Assert.Throws<OperationCanceledException>(() => DataTestPacketTrain.Send(4, 1000,
            _ => sent++, () => 0, stop.Cancel, stop.Token));
        Assert.Equal(1, sent);
    }
}
