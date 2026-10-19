using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.SerializationTests;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.StepTests;

public class DutReadinessTests
{
    [Theory]
    [InlineData("late", true, 4)]
    [InlineData("silent", false, 30)]
    [InlineData("boot-log", false, 30)]
    [InlineData("bad-version", false, 1)]
    public async Task RealProfileWaitsForDutResponse_AndNeverUsesModbusForConsole(string response, bool passed, int calls)
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        var file = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../profiles/RTL_SW_board.json"));
        GraphSerializer.Deserialize(File.ReadAllText(file), vm);
        var stage = Assert.Single(vm.RootGraph.Nodes.OfType<SubtestNodeViewModel>().Where(n => n.Name == "Запуск и готовность DUT"));
        var nodes = BoardProfileTests.All(stage.BodyGraph).ToArray();
        var console = Assert.Single(nodes.OfType<SerialConsoleNodeViewModel>().Where(n => !n.ConnectOnly));
        var connect = Assert.Single(nodes.OfType<SerialConsoleNodeViewModel>().Where(n => n.ConnectOnly));
        Assert.Equal(120000, connect.TimeoutMs); Assert.Equal("dut", connect.DeviceId);
        Assert.Equal("dut", console.DeviceId);
        Assert.Equal("", vm.RootGraph.Hardware!.ConsoleDevices.Single().PortName);
        Assert.Equal(115200, console.BaudRate);
        Assert.Equal(3000, console.TimeoutMs);
        Assert.Equal("ubus call tf_hwsys getParam '{\"name\":\"SW_VERS\"}'", console.Command);
        var retry = Assert.Single(nodes.OfType<ForEachSlaveNodeViewModel>().Where(n => n.RepeatCount == 30));
        Assert.True(retry.UntilSuccess); Assert.Equal(2000, retry.RepeatDelayMs);
        Assert.Equal(new[] { 10000, 2000 }, nodes.OfType<DelayNodeViewModel>().Select(n => n.Milliseconds));
        // Scale only pauses, preserve attempt count and real command/parser/decision nodes.
        foreach (var node in nodes)
        {
            if (node is DelayNodeViewModel delay) delay.Milliseconds = 1;
            if (node is ForEachSlaveNodeViewModel loop) loop.RepeatDelayMs = 2;
        }
        vm.RootGraph.Hardware!.ConsoleDevices.Single().PortName = "SIM-DUT";
        var rig = new DutRig(response);
        using var http = new HttpRequestService();
        using var compiler = new GraphCompiler(rig, http, NullLogger.Instance, rig) { ProfileHardware = vm.RootGraph.Hardware };
        var context = new TestContext(new RegisterState());
        context.SetVariable("McuVersion", "99.0"); // Old successful value cannot make silence pass.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await new TestExecutor().ExecuteAsync(compiler.Compile(stage.BodyGraph).StartNode, context, cts.Token);
        Assert.Equal(passed, result == ExecutionStatus.Completed);
        Assert.Equal(calls, rig.Calls);
        Assert.Equal(new[] { (2301, 0), (2302, 0), (2301, 1), (2302, 1) }, rig.Writes);
        if (passed) { Assert.False(context.HasCriticalError); Assert.Equal("1.0.0", context.GetVariable<string>("McuVersion")); }
    }

    private sealed class DutRig(string mode) : IModbusService, ISerialConsoleSession
    {
        public Task ConnectAsync(ConsoleDeviceDefinition device, int timeoutMs, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.Equal(new[] { (2301, 0), (2302, 0), (2301, 1), (2302, 1) }, Writes);
            Assert.Equal(0, Calls); Assert.Equal("SIM-DUT", device.PortName);
            return Task.CompletedTask;
        }
        public int Calls { get; private set; }
        public List<(int, int)> Writes { get; } = new();
        public Task<string> ExchangeAsync(string port, int baudRate, string command, string terminator, int quietMs, int timeoutMs, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++;
            Assert.Equal("SIM-DUT", port); Assert.Equal(115200, baudRate);
            if (mode == "silent" || mode == "late" && Calls <= 2) throw new TimeoutException("DUT ещё загружается");
            if (mode == "boot-log" || mode == "late" && Calls == 3) return Task.FromResult("Booting Linux...\n");
            return Task.FromResult(mode == "bad-version" ? "{\"SW_VERS\":\"not-ready\"}" : "{\"SW_VERS\":\"1.0.0\"}");
        }
        public Task<bool> WriteRegisterAsync(byte slave, ushort address, ushort value, bool verify = true, CancellationToken cancellationToken = default)
        { Assert.Equal(1, slave); Writes.Add((address, value)); return Task.FromResult(true); }
        public Task<ushort[]> ReadRegistersAsync(byte slave, ushort address, ushort count, CancellationToken cancellationToken = default) => throw new InvalidOperationException("DUT readiness must use the serial console.");
        public Task<bool> CheckPortAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void SubscribeRegister(byte slave, ushort address, Action<ushort[]> callback) => throw new NotSupportedException();
    }
}
