using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

public class ConsoleConnectionWaitTests
{
    private static ConsoleDeviceDefinition Device() => new() { Id = "dut", Name = "Процессор", PortDescriptionContains = "CH340" };

    [Fact]
    public async Task WaitsForLateUsb_OpensWithoutWriting_ThenReusesPortForCommands()
    {
        var catalog = new Catalog(call => call < 2 ? [] : [new("COM-STAND", "CH340"), new("COM-DUT", "CH340")]);
        var port = new Port(); var opened = new List<string>();
        using var session = new SerialConsoleSession(catalog, _ => ["COM-STAND"], d => { opened.Add(d.PortName); return port; });
        var context = new TestContext(new RegisterState()); context.SetVariable("Reply", "previous");
        var step = new SerialConsoleStep(new() { DeviceId = "dut", ConnectOnly = true, TimeoutMs = 5000, OutputVariableName = "Reply" }, session, NullLogger.Instance, Device());
        Assert.Equal(StepResult.True, await step.ExecuteAsync(context, default));
        Assert.Equal(new[] { "COM-DUT" }, opened); Assert.Empty(port.Writes); Assert.Equal(0, port.DiscardCount);
        Assert.Equal("previous", context.GetVariable<string>("Reply"));
        Assert.Equal("ready", await session.ExchangeAsync(Device(), "version", "ready", 50, 500, default));
        Assert.Equal(new[] { "version\n" }, port.Writes); Assert.Single(opened); Assert.Equal(2, catalog.Calls);
    }

    [Fact]
    public async Task AppearedButNotYetOpenablePortIsRetried()
    {
        var failed = new Port { FailOpen = true }; var working = new Port(); int opens = 0;
        using var session = new SerialConsoleSession(new Catalog(_ => [new("COM-DUT", "CH340")]), _ => [], _ => ++opens == 1 ? failed : working);
        await session.ConnectAsync(Device(), 5000, default);
        Assert.True(failed.Disposed); Assert.True(working.IsOpen); Assert.Equal(2, opens); Assert.Empty(working.Writes);
    }

    [Fact]
    public async Task MissingPortTimesOut_AndCancellationRemainsCancellation()
    {
        using var session = new SerialConsoleSession(new Catalog(_ => []), _ => [], _ => throw new Exception("Must not open"));
        var error = await Assert.ThrowsAsync<TimeoutException>(() => session.ConnectAsync(Device(), 100, default));
        Assert.Contains("USB-UART не подключился", error.Message);
        using var cancel = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ConnectAsync(Device(), 10000, cancel.Token));
        // Both paths release the gate.
        await Assert.ThrowsAsync<TimeoutException>(() => session.ConnectAsync(Device(), 100, default));
    }

    [Fact]
    public async Task AmbiguousPortsFailWithoutOpening_AndTimeoutReturnsFalseFromStep()
    {
        using var ambiguous = new SerialConsoleSession(new Catalog(_ => [new("COM1", "CH340"), new("COM2", "CH340")]), _ => [], _ => throw new Exception("Must not open"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ambiguous.ConnectAsync(Device(), 5000, default));
        using var missing = new SerialConsoleSession(new Catalog(_ => []), _ => []);
        var step = new SerialConsoleStep(new() { DeviceId = "dut", ConnectOnly = true, TimeoutMs = 100 }, missing, NullLogger.Instance, Device());
        Assert.Equal(StepResult.False, await step.ExecuteAsync(new TestContext(new RegisterState()), default));
    }

    [Fact]
    public void ConnectionModeSurvivesCloneAndJson_AndPreflightDoesNotSearchForPort()
    {
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        vm.RootGraph.Hardware = ConnectionProfileTests.Hardware();
        vm.RootGraph.Hardware.ConsoleDevices = [Device()];
        var node = new SerialConsoleNodeViewModel { DeviceId = "dut", ConnectOnly = true, TimeoutMs = 120000 };
        vm.RootGraph.Nodes.Add(node);
        var clone = Assert.IsType<SerialConsoleNodeViewModel>(node.Clone()); Assert.True(clone.ConnectOnly);
        GraphCompiler.ValidateRunConnections(vm.RootGraph, vm.RootGraph.Hardware, "COM-STAND");
        var json = GraphSerializer.Serialize(vm, "Проверка"); Assert.Contains("Serial Console (connect v1)", json);
        GraphSerializer.Deserialize(json, vm);
        var loaded = Assert.Single(vm.RootGraph.Nodes.OfType<SerialConsoleNodeViewModel>());
        Assert.True(loaded.ConnectOnly); Assert.Equal(120000, loaded.TimeoutMs);
    }

    private sealed class Catalog(Func<int, IReadOnlyList<SerialPortInfo>> list) : ISerialPortCatalog
    {
        public int Calls;
        public Task<IReadOnlyList<SerialPortInfo>> GetPortsAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(list(++Calls)); }
    }
    private sealed class Port : IConsolePort
    {
        public bool IsOpen { get; private set; }
        public bool FailOpen, Disposed;
        public int DiscardCount;
        public List<string> Writes = [];
        public void Open() { if (FailOpen) throw new IOException("USB is starting"); IsOpen = true; }
        public void DiscardInBuffer() => DiscardCount++;
        public void Write(string text) => Writes.Add(text);
        public string ReadExisting() => "ready";
        public void Dispose() { IsOpen = false; Disposed = true; }
    }
}
