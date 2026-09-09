using System.Net;
using System.Net.Sockets;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class SendUdpSetMacPacketStepTests
{
    private const string Mac = "C0:11:A6:20:01:AC";
    private const string PacketHex = "434F4E464947000000006D77C011A62001AC4B7232";

    [Theory]
    [InlineData("")]
    [InlineData("127.0.0.1")]
    public async Task ExecuteAsync_SendsLegacyPacketAndWaitsForDelayedAcknowledgement(string localIp)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var device = CreateDevice();
        var context = CreateContext();
        var step = CreateStep(GetPort(device), localIp: localIp);

        var execution = step.ExecuteAsync(context, deadline.Token);
        var request = await device.ReceiveAsync(deadline.Token);

        Assert.Equal(Convert.FromHexString(PacketHex), request.Buffer);
        Assert.Equal(IPAddress.Loopback, request.RemoteEndPoint.Address);
        Assert.InRange(request.RemoteEndPoint.Port, 1, 65535);

        // Firmware replies asynchronously, after the datagram has left the host.
        await Task.Delay(80, deadline.Token);
        Assert.False(execution.IsCompleted);

        var acknowledgement = CreateAcknowledgement();
        await device.SendAsync(acknowledgement, request.RemoteEndPoint, deadline.Token);
        var result = await execution;

        Assert.Equal(StepResult.True, result);
        Assert.True(context.GetVariable<bool>("SetMac.PacketSent"));
        Assert.True(context.GetVariable<bool>("SetMac.Acknowledged"));
        Assert.True(context.GetVariable<bool>("SetMac.Success"));
        Assert.Equal(1, context.GetVariable<int>("SetMac.Attempts"));
        Assert.Equal(request.RemoteEndPoint.Address.ToString(), context.GetVariable<string>("SetMac.LocalIp"));
        Assert.Equal(request.RemoteEndPoint.Port, context.GetVariable<int>("SetMac.LocalPort"));
        Assert.Equal(Mac, context.GetVariable<string>("SetMac.Mac"));
        Assert.Equal(PacketHex, context.GetVariable<string>("SetMac.PacketHex"));
        Assert.Equal(Convert.ToHexString(acknowledgement), context.GetVariable<string>("SetMac.ResponseHex"));
        Assert.Equal(device.Client.LocalEndPoint!.ToString(), context.GetVariable<string>("SetMac.ResponseEndpoint"));
        Assert.Equal(string.Empty, context.GetVariable<string>("SetMac.Error"));
    }

    [Fact]
    public async Task ExecuteAsync_IgnoresShortAndWrongCommandRepliesUntilMrArrives()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var device = CreateDevice();
        var context = CreateContext();
        var step = CreateStep(GetPort(device));

        var execution = step.ExecuteAsync(context, deadline.Token);
        var request = await device.ReceiveAsync(deadline.Token);

        var invalidReplies = new[]
        {
            new byte[] { (byte)'m', (byte)'r' },
            new byte[11],
            request.Buffer // An echoed write command is not a write acknowledgement.
        };
        foreach (var reply in invalidReplies)
        {
            await device.SendAsync(reply, request.RemoteEndPoint, deadline.Token);
            await Task.Delay(40, deadline.Token);
            Assert.False(execution.IsCompleted);
        }

        await device.SendAsync(CreateAcknowledgement(), request.RemoteEndPoint, deadline.Token);

        Assert.Equal(StepResult.True, await execution);
        Assert.True(context.GetVariable<bool>("SetMac.Acknowledged"));
        Assert.True(context.GetVariable<bool>("SetMac.Success"));
        Assert.Equal(1, context.GetVariable<int>("SetMac.Attempts"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteAsync_WithoutReplyContinuesToReadbackWithoutReportingAcknowledgedSuccess(
        bool failOnSendError)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var device = CreateDevice();
        var context = CreateContext();
        var step = CreateStep(GetPort(device), timeoutMs: 150, repeatCount: 2, failOnSendError: failOnSendError);

        var execution = step.ExecuteAsync(context, deadline.Token);
        await device.ReceiveAsync(deadline.Token);
        await device.ReceiveAsync(deadline.Token);
        var result = await execution;

        Assert.Equal(StepResult.True, result);
        Assert.True(context.GetVariable<bool>("SetMac.PacketSent"));
        Assert.False(context.GetVariable<bool>("SetMac.Acknowledged"));
        Assert.False(context.GetVariable<bool>("SetMac.Success"));
        Assert.Equal(2, context.GetVariable<int>("SetMac.Attempts"));
        Assert.False(string.IsNullOrWhiteSpace(context.GetVariable<string>("SetMac.Error")));
    }

    [Theory]
    [InlineData(SocketError.ConnectionReset, true)]
    [InlineData(SocketError.ConnectionReset, false)]
    [InlineData(SocketError.ConnectionRefused, true)]
    [InlineData(SocketError.ConnectionRefused, false)]
    public async Task ExecuteAsync_PortUnreachableOnReceiveRetriesAndContinuesToReadback(
        SocketError socketError,
        bool failOnSendError)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var device = CreateDevice();
        var context = CreateContext();
        var step = CreateStep(GetPort(device), repeatCount: 3, failOnSendError: failOnSendError);
        var receiveError = new SocketException((int)socketError);
        var receiveCalls = 0;

        var execution = step.ExecuteAsync(context, (_, _) =>
        {
            receiveCalls++;
            return ValueTask.FromException<UdpReceiveResult>(receiveError);
        }, deadline.Token);

        IPEndPoint? source = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var request = await device.ReceiveAsync(deadline.Token);
            Assert.Equal(Convert.FromHexString(PacketHex), request.Buffer);
            source ??= request.RemoteEndPoint;
            Assert.Equal(source, request.RemoteEndPoint);
        }

        Assert.Equal(StepResult.True, await execution);
        Assert.Equal(3, receiveCalls);
        Assert.Equal(3, context.GetVariable<int>("SetMac.Attempts"));
        Assert.True(context.GetVariable<bool>("SetMac.PacketSent"));
        Assert.False(context.GetVariable<bool>("SetMac.Acknowledged"));
        Assert.False(context.GetVariable<bool>("SetMac.Success"));
        Assert.Equal(socketError.ToString(), context.GetVariable<string>("SetMac.ReceiveSocketError"));
        Assert.Equal(receiveError.NativeErrorCode, context.GetVariable<int>("SetMac.ReceiveNativeErrorCode"));
        Assert.Contains("ICMP Port Unreachable", context.GetVariable<string>("SetMac.ReceiveError"));
        Assert.Contains("ICMP Port Unreachable", context.GetVariable<string>("SetMac.Error"));
    }

    [Theory]
    [InlineData(SocketError.ConnectionReset)]
    [InlineData(SocketError.ConnectionRefused)]
    public async Task ExecuteAsync_AcknowledgesRetryAfterReceivePortUnreachable(SocketError socketError)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var device = CreateDevice();
        var context = CreateContext();
        var step = CreateStep(GetPort(device), repeatCount: 3);
        var receiveCalls = 0;
        var execution = step.ExecuteAsync(context, (client, token) =>
            ++receiveCalls == 1
                ? ValueTask.FromException<UdpReceiveResult>(new SocketException((int)socketError))
                : client.ReceiveAsync(token), deadline.Token);

        var firstRequest = await device.ReceiveAsync(deadline.Token);
        var retry = await device.ReceiveAsync(deadline.Token);
        Assert.Equal(firstRequest.Buffer, retry.Buffer);
        Assert.Equal(firstRequest.RemoteEndPoint, retry.RemoteEndPoint);
        await device.SendAsync(CreateAcknowledgement(), retry.RemoteEndPoint, deadline.Token);

        Assert.Equal(StepResult.True, await execution);
        Assert.Equal(2, context.GetVariable<int>("SetMac.Attempts"));
        Assert.True(context.GetVariable<bool>("SetMac.Acknowledged"));
        Assert.True(context.GetVariable<bool>("SetMac.Success"));
        Assert.Equal(string.Empty, context.GetVariable<string>("SetMac.Error"));
        // Preserve the transient error for diagnostics even if a later attempt succeeds.
        Assert.Equal(socketError.ToString(), context.GetVariable<string>("SetMac.ReceiveSocketError"));
    }

    [Theory]
    [InlineData(true, StepResult.False)]
    [InlineData(false, StepResult.True)]
    public async Task ExecuteAsync_OtherReceiveSocketErrorStillHonorsFailOnSendError(
        bool failOnSendError,
        StepResult expectedResult)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var device = CreateDevice();
        var context = CreateContext();
        var step = CreateStep(GetPort(device), repeatCount: 3, failOnSendError: failOnSendError);
        var receiveCalls = 0;
        var execution = step.ExecuteAsync(context, (_, _) =>
        {
            receiveCalls++;
            return ValueTask.FromException<UdpReceiveResult>(new SocketException((int)SocketError.AccessDenied));
        }, deadline.Token);
        await device.ReceiveAsync(deadline.Token);

        Assert.Equal(expectedResult, await execution);
        Assert.Equal(1, receiveCalls);
        Assert.True(context.GetVariable<bool>("SetMac.PacketSent"));
        Assert.False(context.GetVariable<bool>("SetMac.Success"));
        Assert.False(context.GetVariable<bool>("SetMac.Acknowledged"));
        Assert.Equal(string.Empty, context.GetVariable<string>("SetMac.ReceiveSocketError"));
        Assert.False(string.IsNullOrWhiteSpace(context.GetVariable<string>("SetMac.Error")));
    }

    [Fact]
    public async Task ExecuteAsync_CancellationTakesPriorityOverReceivePortUnreachable()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var device = CreateDevice();
        var context = CreateContext();
        var step = CreateStep(GetPort(device));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => step.ExecuteAsync(context, (_, _) =>
        {
            cancellation.Cancel();
            return ValueTask.FromException<UdpReceiveResult>(new SocketException((int)SocketError.ConnectionReset));
        }, cancellation.Token));

        Assert.True(context.GetVariable<bool>("SetMac.PacketSent"));
        Assert.False(context.GetVariable<bool>("SetMac.Acknowledged"));
        Assert.False(context.GetVariable<bool>("SetMac.Success"));
    }

    [Theory]
    [InlineData(true, StepResult.False)]
    [InlineData(false, StepResult.True)]
    public async Task ExecuteAsync_OccupiedLocalPortHonorsFailOnSendError(
        bool failOnSendError,
        StepResult expectedResult)
    {
        using var occupiedSocket = new UdpClient(AddressFamily.InterNetwork);
        occupiedSocket.ExclusiveAddressUse = true;
        occupiedSocket.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var context = CreateContext();
        var step = CreateStep(
            GetPort(occupiedSocket),
            localPort: GetPort(occupiedSocket),
            failOnSendError: failOnSendError);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(expectedResult, result);
        Assert.False(context.GetVariable<bool>("SetMac.PacketSent"));
        Assert.False(context.GetVariable<bool>("SetMac.Acknowledged"));
        Assert.False(context.GetVariable<bool>("SetMac.Success"));
        Assert.Equal(0, context.GetVariable<int>("SetMac.Attempts"));
        Assert.Contains("занят", context.GetVariable<string>("SetMac.Error"));
    }

    [Fact]
    public async Task ExecuteAsync_RetriesUnansweredCommandUsingTheSameSocketAndPacket()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var device = CreateDevice();
        var context = CreateContext();
        var step = CreateStep(GetPort(device), timeoutMs: 200, repeatCount: 2);

        var execution = step.ExecuteAsync(context, deadline.Token);
        var firstRequest = await device.ReceiveAsync(deadline.Token);
        // Simulate a lost UDP command/response by not replying to the first attempt.
        var secondRequest = await device.ReceiveAsync(deadline.Token);

        Assert.Equal(Convert.FromHexString(PacketHex), firstRequest.Buffer);
        Assert.Equal(firstRequest.Buffer, secondRequest.Buffer);
        Assert.Equal(firstRequest.RemoteEndPoint, secondRequest.RemoteEndPoint);
        await device.SendAsync(CreateAcknowledgement(), secondRequest.RemoteEndPoint, deadline.Token);

        Assert.Equal(StepResult.True, await execution);
        Assert.True(context.GetVariable<bool>("SetMac.Acknowledged"));
        Assert.True(context.GetVariable<bool>("SetMac.Success"));
        Assert.Equal(2, context.GetVariable<int>("SetMac.Attempts"));
    }

    [Fact]
    public async Task ExecuteAsync_CancellationWhileWaitingForReplyPropagates()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        using var device = CreateDevice();
        var context = CreateContext();
        var step = CreateStep(GetPort(device), repeatCount: 3);

        var execution = step.ExecuteAsync(context, cancellation.Token);
        await device.ReceiveAsync(deadline.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.True(context.GetVariable<bool>("SetMac.PacketSent"));
        Assert.False(context.GetVariable<bool>("SetMac.Acknowledged"));
        Assert.False(context.GetVariable<bool>("SetMac.Success"));
        Assert.Equal(1, context.GetVariable<int>("SetMac.Attempts"));
    }

    [Fact]
    public async Task ExecuteAsync_InvalidMacClearsDiagnosticsFromPreviousSuccessfulRun()
    {
        var context = CreateContext();
        context.SetVariable("Dut.NewMac", "invalid");
        context.SetVariable("SetMac.PacketSent", true);
        context.SetVariable("SetMac.Acknowledged", true);
        context.SetVariable("SetMac.Success", true);
        context.SetVariable("SetMac.Attempts", 3);
        context.SetVariable("SetMac.PacketHex", PacketHex);
        context.SetVariable("SetMac.Mac", Mac);
        context.SetVariable("SetMac.ResponseHex", "previous response");
        context.SetVariable("SetMac.ResponseEndpoint", "previous endpoint");
        context.SetVariable("SetMac.ReceiveError", "previous receive error");
        context.SetVariable("SetMac.ReceiveSocketError", "ConnectionReset");
        context.SetVariable("SetMac.ReceiveNativeErrorCode", 10054);

        var result = await CreateStep(43962).ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.False(context.GetVariable<bool>("SetMac.PacketSent"));
        Assert.False(context.GetVariable<bool>("SetMac.Acknowledged"));
        Assert.False(context.GetVariable<bool>("SetMac.Success"));
        Assert.Equal(0, context.GetVariable<int>("SetMac.Attempts"));
        Assert.Equal(string.Empty, context.GetVariable<string>("SetMac.PacketHex"));
        Assert.Equal(string.Empty, context.GetVariable<string>("SetMac.Mac"));
        Assert.Equal(string.Empty, context.GetVariable<string>("SetMac.ResponseHex"));
        Assert.Equal(string.Empty, context.GetVariable<string>("SetMac.ResponseEndpoint"));
        Assert.Equal(string.Empty, context.GetVariable<string>("SetMac.ReceiveError"));
        Assert.Equal(string.Empty, context.GetVariable<string>("SetMac.ReceiveSocketError"));
        Assert.Equal(0, context.GetVariable<int>("SetMac.ReceiveNativeErrorCode"));
        Assert.False(string.IsNullOrWhiteSpace(context.GetVariable<string>("SetMac.Error")));
    }

    private static TestContext CreateContext()
    {
        var context = new TestContext(new RegisterState());
        context.SetVariable("Dut.NewMac", Mac);
        return context;
    }

    private static UdpClient CreateDevice() => new(new IPEndPoint(IPAddress.Loopback, 0));

    private static int GetPort(UdpClient device) => ((IPEndPoint)device.Client.LocalEndPoint!).Port;

    private static byte[] CreateAcknowledgement()
    {
        // Qt accepts the mr command at offsets 10/11; neither CONFIG nor a MAC echo is required.
        var acknowledgement = new byte[12];
        acknowledgement[10] = (byte)'m';
        acknowledgement[11] = (byte)'r';
        return acknowledgement;
    }

    private static SendUdpSetMacPacketStep CreateStep(
        int targetPort,
        int timeoutMs = 2000,
        int repeatCount = 1,
        bool failOnSendError = true,
        string localIp = "127.0.0.1",
        int localPort = 0) => new(
            NullLogger.Instance,
            targetIp: "127.0.0.1",
            targetPort: targetPort,
            localPort: localPort,
            macVariableName: "Dut.NewMac",
            timeoutMs: timeoutMs,
            repeatCount: repeatCount,
            delayBetweenRepeatsMs: 10,
            failOnSendError: failOnSendError,
            localIp: localIp);
}
