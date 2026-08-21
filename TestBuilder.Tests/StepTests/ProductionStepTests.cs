using System.Net;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels.StepVM;

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
        Assert.Equal(12345, context.GetVariable<int>("NetTest.SerialNumber"));
        Assert.Equal("12345", context.GetVariable<string>("SerialNumberText"));
        Assert.True(context.GetVariable<bool>("SerialNumberReceived"));
        Assert.Contains("devType=PSW%2BUPS-Box%208x2Pro", service.LastUrl);
        Assert.Contains("cpuId=CPU%201", service.LastUrl);
    }

    [Fact]
    public async Task GetSerialNumberFromServerStep_AcceptsBareHostLikeLegacyQtCode()
    {
        var service = new CapturingHttpService(HttpRequestResult.Success(200, "321", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());

        var step = new GetSerialNumberFromServerStep(
            service,
            NullLogger.Instance,
            "stand-server.local",
            "PSW+UPS-Box 8x2Pro",
            "Dut.cpu_id",
            1000,
            0,
            0,
            "SerialNumber",
            failOnError: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(321, context.GetVariable<int>("SerialNumber"));
        Assert.Equal("http://stand-server.local/api/api.svc/getSerialNum?devType=PSW%2BUPS-Box%208x2Pro", service.LastUrl);
    }

    [Fact]
    public async Task GetSerialNumberFromServerStep_ReusesFullEndpointUrl()
    {
        var service = new CapturingHttpService(HttpRequestResult.Success(200, "\uFEFF654\r\n", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        context.SetVariable("Dut.cpu_id", "ABC+123");

        var step = new GetSerialNumberFromServerStep(
            service,
            NullLogger.Instance,
            "https://server/api/api.svc/getSerialNum",
            "PSW+UPS-Box 8x2Pro",
            "Dut.cpu_id",
            1000,
            0,
            0,
            "ServerSerial",
            failOnError: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(654, context.GetVariable<int>("ServerSerial"));
        Assert.Equal(654, context.GetVariable<int>("SerialNumber"));
        Assert.Equal("https://server/api/api.svc/getSerialNum?devType=PSW%2BUPS-Box%208x2Pro&cpuId=ABC%2B123", service.LastUrl);
    }

    [Fact]
    public async Task GetSerialNumberFromServerStep_RejectsPlaceholderWithoutDnsRequest()
    {
        var service = new CapturingHttpService(HttpRequestResult.Success(200, "123", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());

        var step = new GetSerialNumberFromServerStep(
            service,
            NullLogger.Instance,
            "http://SERVER_BASE_URL",
            "PSW+UPS-Box 8x2Pro",
            "Dut.cpu_id",
            1000,
            0,
            0,
            "SerialNumber",
            failOnError: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.Equal(0, service.Calls);
        Assert.False(context.GetVariable<bool>("SerialNumberReceived"));
        Assert.Contains("ServerBaseUrl", context.GetVariable<string>("SerialNumberError"));
    }

    [Fact]
    public async Task GetSerialNumberNode_UsesSettingsServerForPlaceholder()
    {
        var previousServerBaseUrl = AppSettings.Instance.ServerBaseUrl;

        try
        {
            AppSettings.Instance.ServerBaseUrl = "serial-server.local";
            var service = new CapturingHttpService(HttpRequestResult.Success(200, "777", TimeSpan.FromMilliseconds(1)));
            var context = new TestContext(new RegisterState());
            var node = new GetSerialNumberFromServerNodeViewModel
            {
                ServerBaseUrl = "http://SERVER_BASE_URL",
                RetryCount = 0
            };

            var result = await node
                .CreateStep(service, NullLogger.Instance)
                .ExecuteAsync(context, CancellationToken.None);

            Assert.Equal(StepResult.True, result);
            Assert.Equal(777, context.GetVariable<int>("SerialNumber"));
            Assert.Equal("http://serial-server.local/api/api.svc/getSerialNum?devType=PSW%2BUPS-Box%208x2Pro", service.LastUrl);
        }
        finally
        {
            AppSettings.Instance.ServerBaseUrl = previousServerBaseUrl;
        }
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
    public void RunDataTestStep_BuildsLegacyEthernetUdpPacket()
    {
        var sourceMac = new byte[] { 0x10, 0xFF, 0xE0, 0x68, 0xFE, 0x24 };
        var destinationMac = new byte[] { 0x00, 0xFF, 0x03, 0x0A, 0xDC, 0x84 };

        var packet = RunDataTestStep.BuildPacket(
            sourceMac,
            destinationMac,
            IPAddress.Parse("192.168.0.3"),
            IPAddress.Parse("192.168.0.2"),
            1514,
            43962);

        Assert.Equal(1514, packet.Length);
        Assert.Equal("00FF030ADC84", Convert.ToHexString(packet[0..6]));
        Assert.Equal("10FFE068FE24", Convert.ToHexString(packet[6..12]));
        Assert.Equal("0800", Convert.ToHexString(packet[12..14]));
        Assert.Equal(0x45, packet[14]);
        Assert.Equal("05DC", Convert.ToHexString(packet[16..18]));
        Assert.Equal(17, packet[23]);
        Assert.Equal("C0A80003", Convert.ToHexString(packet[26..30]));
        Assert.Equal("C0A80002", Convert.ToHexString(packet[30..34]));
        Assert.Equal("ABBAABBA05C80000", Convert.ToHexString(packet[34..42]));
        Assert.All(packet[42..], value => Assert.Equal(0x41, value));
    }

    [Fact]
    public void RunDataTestStep_Calculates100MbitPacketCount()
    {
        var packets = RunDataTestStep.CalculateExpectedPackets(100, 1514, 5000);

        Assert.Equal(41281, packets);
    }

    [Fact]
    public void RunDataTestStep_Calculates1GbitPacketCount()
    {
        var packets = RunDataTestStep.CalculateExpectedPackets(1000, 1514, 5000);

        Assert.Equal(412814, packets);
    }

    [Fact]
    public async Task RunDataTestStep_RejectsUnsupportedModeBeforeOpeningPcap()
    {
        var context = new TestContext(new RegisterState());
        var step = new RunDataTestStep(
            NullLogger.Instance,
            "Bercut",
            10000,
            1514,
            43962,
            15000,
            100,
            5000,
            500,
            5000,
            1.0,
            new[] { new DataTestPortConfig("port0-1", "192.168.0.2", "192.168.0.3") },
            "DataTest",
            failOnError: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.False(context.GetVariable<bool>("DataTest.Passed"));
        Assert.Contains("SoftwarePcap", context.GetVariable<string>("DataTest.Error"));
    }

    private sealed class CapturingHttpService : IHttpRequestService
    {
        private readonly HttpRequestResult _result;

        public CapturingHttpService(HttpRequestResult result)
        {
            _result = result;
        }

        public string LastUrl { get; private set; } = string.Empty;
        public int Calls { get; private set; }

        public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls++;
            LastUrl = url;
            return Task.FromResult(_result);
        }
    }
}
