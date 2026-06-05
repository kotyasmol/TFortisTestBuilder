using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Http;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class ProductionStepTests
{
    [Fact]
    public async Task GetSerialNumberFromServerStep_EncodesDeviceTypeAndSavesSerial()
    {
        var service = new CapturingHttpService(HttpRequestResult.Success(200, "12345", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        context.SetVariable("Dut.cpu_id", "CPU 1");

        var step = new GetSerialNumberFromServerStep(
            service,
            NullLogger.Instance,
            "http://server",
            "PSW+UPS-Box 8x2Pro",
            "Dut.cpu_id",
            1000,
            0,
            0,
            "SerialNumber",
            failOnError: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(12345, context.GetVariable<int>("SerialNumber"));
        Assert.True(context.GetVariable<bool>("SerialNumberReceived"));
        Assert.Contains("devType=PSW%2BUPS-Box%208x2Pro", service.LastUrl);
        Assert.Contains("cpuId=CPU%201", service.LastUrl);
    }

    [Fact]
    public async Task GetUpsStatusStep_ParsesIntegerResponse()
    {
        var service = new CapturingHttpService(HttpRequestResult.Success(200, "1", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        var step = new GetUpsStatusStep(service, NullLogger.Instance, "http://192.168.0.1", 1000, "Dut.ups_rez", true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(1, context.GetVariable<int>("Dut.ups_rez"));
        Assert.Equal("http://192.168.0.1/api/getUpsStatus", service.LastUrl);
    }

    [Fact]
    public async Task GetUpsVoltageStep_ParsesDoubleResponse()
    {
        var service = new CapturingHttpService(HttpRequestResult.Success(200, "24.7", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        var step = new GetUpsVoltageStep(service, NullLogger.Instance, "http://192.168.0.1", 1000, "Dut.akb_voltage", true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(24.7, context.GetVariable<double>("Dut.akb_voltage"));
        Assert.Equal("http://192.168.0.1/api/getUpsVoltage", service.LastUrl);
    }

    [Fact]
    public void SendUdpSetMacPacketStep_BuildsLegacyPacket()
    {
        Assert.True(SendUdpSetMacPacketStep.TryParseMac("AA:BB:CC:DD:EE:FF", out var mac, out var normalized));

        var packet = SendUdpSetMacPacketStep.BuildPacket(mac);

        Assert.Equal("AA:BB:CC:DD:EE:FF", normalized);
        Assert.Equal(21, packet.Length);
        Assert.Equal("434F4E464947000000006D77AABBCCDDEEFF4B7232", Convert.ToHexString(packet));
    }

    [Fact]
    public async Task RunDataTestStep_ReturnsUnsupportedSoftwarePcapResult()
    {
        var context = new TestContext(new RegisterState());
        var step = new RunDataTestStep(
            NullLogger.Instance,
            "SoftwarePcap",
            10000,
            1514,
            43962,
            15000,
            new[] { new DataTestPortConfig("Port 0", "192.168.10.1", "192.168.10.2") },
            "DataTest",
            failOnError: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.False(context.GetVariable<bool>("DataTest.Passed"));
        Assert.Contains("pcap", context.GetVariable<string>("DataTest.Error"));
        Assert.Equal("Port 0", context.GetVariable<string>("DataTest.Port0.Name"));
    }

    private sealed class CapturingHttpService : IHttpRequestService
    {
        private readonly HttpRequestResult _result;

        public CapturingHttpService(HttpRequestResult result)
        {
            _result = result;
        }

        public string LastUrl { get; private set; } = string.Empty;

        public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout, CancellationToken cancellationToken)
        {
            LastUrl = url;
            return Task.FromResult(_result);
        }
    }
}
