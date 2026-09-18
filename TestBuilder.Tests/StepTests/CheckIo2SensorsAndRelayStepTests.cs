using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class CheckIo2SensorsAndRelayStepTests
{
    [Fact]
    public async Task CheckIo2SensorsAndRelayStep_ChecksBothSensorsAndRelay_ThenRestoresSafeState()
    {
        var http = new QueueHttpService(
            HttpRequestResult.Success(200, "off", TimeSpan.FromMilliseconds(1)),
            HttpRequestResult.Success(200, Selftest("sensor_1", 1), TimeSpan.FromMilliseconds(1)),
            HttpRequestResult.Success(200, Selftest("sensor_2", 1), TimeSpan.FromMilliseconds(1)),
            HttpRequestResult.Success(200, "off", TimeSpan.FromMilliseconds(1)),
            HttpRequestResult.Success(200, "on", TimeSpan.FromMilliseconds(1)),
            HttpRequestResult.Success(200, "off", TimeSpan.FromMilliseconds(1)),
            HttpRequestResult.Success(200, "off", TimeSpan.FromMilliseconds(1)));
        var modbus = new FakeModbusService(
            type: 5,
            relayInputValues: new ushort[] { 0, 0, 1, 0 });
        var step = new CheckIo2SensorsAndRelayStep(
            modbus,
            http,
            NullLogger.Instance,
            io2SlaveId: 25,
            baseUrl: "http://192.168.0.1",
            selftestEndpoint: "/selftest.xml",
            relayEndpointTemplate: "/test.shtml?set_mb_output={state}",
            requestTimeoutMs: 1000,
            stateTimeoutMs: 1000,
            pollIntervalMs: 1,
            useBrowserForSelftest: false);
        var context = new TestContext(new RegisterState());

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.True(context.GetVariable<bool>("InOut.Ok"));
        Assert.True(context.GetVariable<bool>("InOut.Sensor1.Passed"));
        Assert.True(context.GetVariable<bool>("InOut.Sensor2.Passed"));
        Assert.True(context.GetVariable<bool>("InOut.Relay.Passed"));
        Assert.True(context.GetVariable<bool>("InOut.CleanupOk"));
        Assert.Equal(25, context.GetVariable<byte>("InOut.Io2SlaveId"));
        Assert.Equal(
            new (ushort Register, ushort Value)[]
            {
                (1500, 1), (1500, 0), (1501, 1), (1501, 0), (1500, 0), (1501, 0)
            },
            modbus.Writes.Select(call => (call.Address, call.Value)));
        Assert.Equal(
            new[]
            {
                "http://192.168.0.1/test.shtml?set_mb_output=0",
                "http://192.168.0.1/selftest.xml",
                "http://192.168.0.1/selftest.xml",
                "http://192.168.0.1/test.shtml?set_mb_output=0",
                "http://192.168.0.1/test.shtml?set_mb_output=1",
                "http://192.168.0.1/test.shtml?set_mb_output=0",
                "http://192.168.0.1/test.shtml?set_mb_output=0"
            },
            http.RequestedUrls);
    }

    [Fact]
    public async Task CheckIo2SensorsAndRelayStep_WhenRelayDisabled_ChecksSensorsWithoutRelayCommands()
    {
        var http = new QueueHttpService(
            HttpRequestResult.Success(200, Selftest("sensor_1", 1), TimeSpan.FromMilliseconds(1)),
            HttpRequestResult.Success(200, Selftest("sensor_2", 1), TimeSpan.FromMilliseconds(1)));
        var modbus = new FakeModbusService(type: 5, relayInputValues: Array.Empty<ushort>());
        var step = new CheckIo2SensorsAndRelayStep(
            modbus,
            http,
            NullLogger.Instance,
            io2SlaveId: 25,
            baseUrl: "http://192.168.0.1",
            selftestEndpoint: "/selftest.xml",
            relayEndpointTemplate: "/test.shtml?set_mb_output={state}",
            requestTimeoutMs: 1000,
            stateTimeoutMs: 1000,
            pollIntervalMs: 1,
            useBrowserForSelftest: false,
            checkRelay: false);
        var context = new TestContext(new RegisterState());

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.True(context.GetVariable<bool>("InOut.Sensor1.Passed"));
        Assert.True(context.GetVariable<bool>("InOut.Sensor2.Passed"));
        Assert.True(context.GetVariable<bool>("InOut.Relay.Skipped"));
        Assert.True(context.GetVariable<bool>("InOut.CleanupOk"));
        Assert.Equal(new[] { "http://192.168.0.1/selftest.xml", "http://192.168.0.1/selftest.xml" }, http.RequestedUrls);
        Assert.DoesNotContain(modbus.Writes, call => call.Address == 1507);
    }

    private static string Selftest(string sensorName, int value) => $"""
        <selftest>
          <default_mac>00:11:22:33:44:55</default_mac>
          <init_ok>1</init_ok>
          <dev_type>0</dev_type>
          <firmvare_vers>1119</firmvare_vers>
          <boot_vers>0</boot_vers>
          <{sensorName}>{value}</{sensorName}>
        </selftest>
        """;

    private sealed class QueueHttpService : IHttpRequestService
    {
        private readonly Queue<HttpRequestResult> _results;

        public QueueHttpService(params HttpRequestResult[] results)
        {
            _results = new Queue<HttpRequestResult>(results);
        }

        public List<string> RequestedUrls { get; } = new();

        public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout, CancellationToken cancellationToken)
        {
            RequestedUrls.Add(url);
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class FakeModbusService : IModbusService
    {
        private readonly ushort _type;
        private readonly Queue<ushort> _relayInputValues;

        public FakeModbusService(ushort type, IEnumerable<ushort> relayInputValues)
        {
            _type = type;
            _relayInputValues = new Queue<ushort>(relayInputValues);
        }

        public List<(byte SlaveId, ushort Address, ushort Value)> Writes { get; } = new();

        public Task<ushort[]> ReadRegistersAsync(byte slaveId, ushort address, ushort count, CancellationToken cancellationToken = default)
        {
            if (address == 0)
            {
                return Task.FromResult(new[] { _type });
            }

            if (address == 1507)
            {
                return Task.FromResult(new[] { _relayInputValues.Dequeue() });
            }

            throw new InvalidOperationException($"Unexpected read address {address}.");
        }

        public Task<bool> WriteRegisterAsync(byte slaveId, ushort address, ushort value, bool verify = true, CancellationToken cancellationToken = default)
        {
            Writes.Add((slaveId, address, value));
            return Task.FromResult(true);
        }

        public Task<bool> CheckPortAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public void SubscribeRegister(byte slaveId, ushort address, Action<ushort[]> callback)
        {
        }
    }
}
