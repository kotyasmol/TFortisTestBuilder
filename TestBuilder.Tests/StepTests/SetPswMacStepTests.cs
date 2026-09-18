using System.Net;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class SetPswMacStepTests
{
    [Fact]
    public async Task SendsExactLegacyPacketWithStandSourcePort()
    {
        var context = Context("C0:11:A6:06:01:AC");
        var step = Create(async (local, remote, packet, token) =>
        {
            Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.0.2"), 6123), local);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.0.1"), 43962), remote);
            Assert.Equal("434F4E464947000000006D77C011A60601AC4B7232", Convert.ToHexString(packet));
            await Task.Yield();
            return packet.Length;
        });

        Assert.Equal(StepResult.True, await step.ExecuteAsync(context, CancellationToken.None));
        Assert.True(context.GetVariable<bool>("SetMac.Sent"));
        Assert.Equal("C0:11:A6:06:01:AC", context.GetVariable<string>("SetMac.Mac"));
        Assert.False(context.Variables.ContainsKey("SetMac.Verified"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-mac")]
    [InlineData("C0:11:A6:06:01")]
    [InlineData("FF:FF:FF:FF:FF:FF")]
    [InlineData("00:00:00:00:00:00")]
    public async Task RejectsInvalidMacBeforeSending(string mac)
    {
        var step = Create((_, _, _, _) => throw new InvalidOperationException("Must not send"));
        var context = Context(mac);
        Assert.Equal(StepResult.False, await step.ExecuteAsync(context, CancellationToken.None));
        Assert.False(context.GetVariable<bool>("SetMac.Sent"));
        Assert.DoesNotContain("Must not send", context.GetVariable<string>("SetMac.Error"));
    }

    [Fact]
    public async Task TimeoutAndSocketFailureDoNotPass()
    {
        var context = Context("C0:11:A6:06:01:AC");
        var timeout = Create(async (_, _, _, token) => { await Task.Delay(Timeout.Infinite, token); return 21; });
        Assert.Equal(StepResult.False, await timeout.ExecuteAsync(context, CancellationToken.None));
        Assert.False(context.GetVariable<bool>("SetMac.Sent"));
        Assert.Contains("таймаут", context.GetVariable<string>("SetMac.Error"));

        var failure = Create((_, _, _, _) => throw new InvalidOperationException("port busy"));
        Assert.Equal(StepResult.False, await failure.ExecuteAsync(context, CancellationToken.None));
        Assert.Contains("port busy", context.GetVariable<string>("SetMac.Error"));
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        var step = Create((_, _, _, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult(21); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => step.ExecuteAsync(Context("C0:11:A6:06:01:AC"), cancellation.Token));
    }

    [Fact]
    public async Task PartialDatagramDoesNotPassAndResetsPreviousSuccess()
    {
        var context = Context("C0:11:A6:06:01:AC");
        context.SetVariable("SetMac.Sent", true);
        context.SetVariable("SetMac.Success", true);
        Assert.Equal(StepResult.False, await Create((_, _, _, _) => Task.FromResult(20)).ExecuteAsync(context, CancellationToken.None));
        Assert.False(context.GetVariable<bool>("SetMac.Sent"));
        Assert.False(context.GetVariable<bool>("SetMac.Success"));
    }

    private static TestContext Context(string mac)
    {
        var context = new TestContext(new RegisterState());
        context.SetVariable("Dut.NewMac", mac);
        return context;
    }

    private static SetPswMacStep Create(Func<IPEndPoint, IPEndPoint, byte[], CancellationToken, Task<int>> sender) =>
        new(NullLogger.Instance, "192.168.0.1", "192.168.0.2", 6123, 43962, "Dut.NewMac", 50, true, sender);
}
