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
            relayInputValues: new ushort[] { 0, 0, 0, 0, 1, 0, 0 });
        http.BeforeGet = _ => Assert.Equal(
            new[] { "write:1500=0", "read:1500", "write:1501=0", "read:1501" },
            modbus.Operations.Where(op => !op.StartsWith("read:0")).Take(4));
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
        Assert.True(context.GetVariable<bool>("InOut.InitialOutputsOff"));
        Assert.True(context.GetVariable<bool>("InOut.Sensor1.Passed"));
        Assert.True(context.GetVariable<bool>("InOut.Sensor2.Passed"));
        Assert.True(context.GetVariable<bool>("InOut.Relay.Passed"));
        Assert.True(context.GetVariable<bool>("InOut.CleanupOk"));
        Assert.Equal(25, context.GetVariable<byte>("InOut.Io2SlaveId"));
        Assert.Equal(
            new (ushort Register, ushort Value)[]
            {
                (1500, 0), (1501, 0), (1507, 0),
                (1500, 1), (1500, 0), (1501, 1), (1501, 0),
                (1507, 0), (1507, 0), (1500, 0), (1501, 0), (1507, 0)
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
        Assert.True(context.GetVariable<bool>("InOut.InitialOutputsOff"));
        Assert.Equal(new (ushort, ushort)[] { (1500, 0), (1501, 0) },
            modbus.Writes.Take(2).Select(call => (call.Address, call.Value)));
        Assert.Equal(new[] { "http://192.168.0.1/selftest.xml", "http://192.168.0.1/selftest.xml" }, http.RequestedUrls);
        Assert.DoesNotContain(modbus.Writes, call => call.Address == 1507);
        Assert.DoesNotContain("read:1507", modbus.Operations);
    }

    [Theory]
    [InlineData(1500)]
    [InlineData(1501)]
    public async Task FailedInitialResetPreventsAnyActivationButStillResetsBothOutputs(int failedAddress)
    {
        var modbus = new FakeModbusService(5, Array.Empty<ushort>())
        {
            OnWrite = (address, _) => address != failedAddress
        };
        var http = new QueueHttpService();
        var context = new TestContext(new RegisterState());

        var result = await CreateSensorsStep(modbus, http).ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.False(context.GetVariable<bool>("InOut.InitialOutputsOff"));
        Assert.Contains("Исходное состояние", context.GetVariable<string>("InOut.Error"));
        Assert.Empty(http.RequestedUrls);
        Assert.All(modbus.Writes, write => Assert.Equal(0, write.Value));
        Assert.Equal(new ushort[] { 1500, 1501, 1500, 1501 }, modbus.Writes.Select(w => w.Address));
    }

    [Fact]
    public async Task InitialResetWaitsForReadbackBeforeEnablingSensors()
    {
        var reads = 0;
        var modbus = new FakeModbusService(5, Array.Empty<ushort>())
        {
            OnRead = (address, _) => address == 1500 && ++reads == 1 ? (ushort)1 : null
        };
        var http = SensorResponses();
        var context = new TestContext(new RegisterState());

        Assert.Equal(StepResult.True, await CreateSensorsStep(modbus, http).ExecuteAsync(context, CancellationToken.None));
        Assert.Equal(2, context.GetVariable<int>("InOut.Initial.Sensor1.WriteAttempts"));
        Assert.Equal(new[] { "write:1500=0", "read:1500", "read:1500", "write:1501=0", "read:1501", "write:1500=1" },
            modbus.Operations.Where(op => !op.StartsWith("read:0")).Take(6));
    }

    [Fact]
    public async Task StuckHighOutputDoesNotPassPreparationOrSendHttp()
    {
        var modbus = new FakeModbusService(5, Array.Empty<ushort>())
        {
            OnRead = (address, _) => address == 1500 ? (ushort)1 : null
        };
        var http = new QueueHttpService();
        var context = new TestContext(new RegisterState());

        Assert.Equal(StepResult.False, await CreateSensorsStep(modbus, http, timeoutMs: 30).ExecuteAsync(context, CancellationToken.None));
        Assert.False(context.GetVariable<bool>("InOut.InitialOutputsOff"));
        Assert.False(context.GetVariable<bool>("InOut.CleanupOk"));
        Assert.All(modbus.Writes, write => Assert.Equal(0, write.Value));
        Assert.Empty(http.RequestedUrls);
    }

    [Fact]
    public async Task CancellationDuringResetStillTurnsBothOutputsOffWithoutEnablingThem()
    {
        using var cts = new CancellationTokenSource();
        var cancelled = false;
        var modbus = new FakeModbusService(5, Array.Empty<ushort>())
        {
            OnRead = (address, token) =>
            {
                if (address == 1500 && !cancelled)
                {
                    cancelled = true;
                    cts.Cancel();
                    token.ThrowIfCancellationRequested();
                }
                return null;
            }
        };
        var http = new QueueHttpService();
        var context = new TestContext(new RegisterState());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateSensorsStep(modbus, http).ExecuteAsync(context, cts.Token));
        Assert.All(modbus.Writes, write => Assert.Equal(0, write.Value));
        Assert.Equal(new ushort[] { 1500, 1501 }, modbus.Writes.TakeLast(2).Select(w => w.Address));
        Assert.True(context.GetVariable<bool>("InOut.CleanupOk"));
        Assert.Empty(http.RequestedUrls);
    }

    [Fact]
    public async Task LatchedRelayInputIsClearedBeforeEachNewRelayTest()
    {
        var modbus = new FakeModbusService(5, Array.Empty<ushort>());
        modbus.Registers[1507] = 1;
        var relayEnabled = false;
        var http = new QueueHttpService(
            HttpRequestResult.Success(200, "off", TimeSpan.Zero),
            HttpRequestResult.Success(200, Selftest("sensor_1", 1), TimeSpan.Zero),
            HttpRequestResult.Success(200, Selftest("sensor_2", 1), TimeSpan.Zero),
            HttpRequestResult.Success(200, "off", TimeSpan.Zero),
            HttpRequestResult.Success(200, "on", TimeSpan.Zero),
            HttpRequestResult.Success(200, "off", TimeSpan.Zero),
            HttpRequestResult.Success(200, "off", TimeSpan.Zero));
        http.BeforeGet = url =>
        {
            if (url.EndsWith("set_mb_output=1"))
            {
                Assert.Equal(0, modbus.Registers[1507]);
                modbus.Registers[1507] = 1;
                relayEnabled = true;
            }
            // Turning the DUT relay off does not itself clear a captured input.
        };
        var step = new CheckIo2SensorsAndRelayStep(modbus, http, NullLogger.Instance, 25,
            "http://192.168.0.1", "/selftest.xml", "/test.shtml?set_mb_output={state}",
            1000, 1000, 1, false, checkRelay: true);
        var context = new TestContext(new RegisterState());

        Assert.Equal(StepResult.True, await step.ExecuteAsync(context, CancellationToken.None));
        Assert.True(relayEnabled);
        Assert.Equal(0, modbus.Registers[1507]);
        Assert.All(modbus.Writes.Where(w => w.Address == 1507), w => Assert.Equal(0, w.Value));
    }

    [Fact]
    public async Task FailedInputResetPreventsSensorAndRelayActivation()
    {
        var modbus = new FakeModbusService(5, Array.Empty<ushort>())
        {
            OnWrite = (address, _) => address != 1507
        };
        modbus.Registers[1507] = 1;
        var http = new QueueHttpService(
            HttpRequestResult.Success(200, "off", TimeSpan.Zero),
            HttpRequestResult.Success(200, "off", TimeSpan.Zero));
        var step = new CheckIo2SensorsAndRelayStep(modbus, http, NullLogger.Instance, 25,
            "http://192.168.0.1", "/selftest.xml", "/test.shtml?set_mb_output={state}",
            1000, 1000, 1, false, checkRelay: true);
        var context = new TestContext(new RegisterState());

        Assert.Equal(StepResult.False, await step.ExecuteAsync(context, CancellationToken.None));
        Assert.True(context.GetVariable<bool>("InOut.InitialOutputsOff"));
        Assert.False(context.GetVariable<bool>("InOut.Ok"));
        Assert.All(modbus.Writes, write => Assert.Equal(0, write.Value));
        Assert.All(http.RequestedUrls, url => Assert.EndsWith("set_mb_output=0", url));
        Assert.Equal(1, modbus.Registers[1507]);
    }

    private static QueueHttpService SensorResponses() => new(
        HttpRequestResult.Success(200, Selftest("sensor_1", 1), TimeSpan.Zero),
        HttpRequestResult.Success(200, Selftest("sensor_2", 1), TimeSpan.Zero));

    private static CheckIo2SensorsAndRelayStep CreateSensorsStep(IModbusService modbus, IHttpRequestService http, int timeoutMs = 1000) =>
        new(modbus, http, NullLogger.Instance, 25, "http://192.168.0.1", "/selftest.xml",
            "/test.shtml?set_mb_output={state}", 1000, timeoutMs, 1, false, checkRelay: false);

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
        public Action<string>? BeforeGet { get; set; }

        public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout, CancellationToken cancellationToken)
        {
            RequestedUrls.Add(url);
            BeforeGet?.Invoke(url);
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
        public List<string> Operations { get; } = new();
        public Dictionary<ushort, ushort> Registers { get; } = new() { [1500] = 1, [1501] = 1, [1507] = 0 };
        public Func<ushort, ushort, bool>? OnWrite { get; init; }
        public Func<ushort, CancellationToken, ushort?>? OnRead { get; init; }

        public Task<ushort[]> ReadRegistersAsync(byte slaveId, ushort address, ushort count, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add($"read:{address}");
            if (OnRead?.Invoke(address, cancellationToken) is ushort supplied)
                return Task.FromResult(new[] { supplied });
            if (address == 0)
            {
                return Task.FromResult(new[] { _type });
            }

            if (address == 1507 && _relayInputValues.Count > 0)
            {
                return Task.FromResult(new[] { _relayInputValues.Dequeue() });
            }

            if (Registers.TryGetValue(address, out var value)) return Task.FromResult(new[] { value });

            throw new InvalidOperationException($"Unexpected read address {address}.");
        }

        public Task<bool> WriteRegisterAsync(byte slaveId, ushort address, ushort value, bool verify = true, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Writes.Add((slaveId, address, value));
            Operations.Add($"write:{address}={value}");
            var result = OnWrite?.Invoke(address, value) ?? true;
            if (result) Registers[address] = value;
            return Task.FromResult(result);
        }

        public Task<bool> CheckPortAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public void SubscribeRegister(byte slaveId, ushort address, Action<ushort[]> callback)
        {
        }
    }
}
