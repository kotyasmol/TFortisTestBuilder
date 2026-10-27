using System.IO.Ports;
using System.Text.Json.Nodes;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

public class ConnectionProfileTests
{
    internal static HardwareConfiguration Hardware() => new()
    {
        PortName = "SIM-MODBUS", BaudRate = 19200,
        Devices = new() { new() { SlaveId = 42, Name = "Новый датчик", Registers = new() { new() { Address = 700, Area = ModbusArea.Input } } } },
        ConsoleDevices = new() { new() { Id = "processor", Name = "Новый процессор", PortName = "SIM-UART", BaudRate = 57600,
            Parity = Parity.Even, DataBits = 7, StopBits = StopBits.Two, EncodingName = "us-ascii", LineEnding = "\r\n", DtrEnable = true, RtsEnable = true } }
    };

    [Fact]
    public void StandSearchIgnoresSavedComNumberAndExcludesConsolePorts()
    {
        var hardware = Hardware();
        hardware.PortName = "COM-OLD";
        hardware.ConsoleDevices[0].PortName = "COM2";
        Assert.Equal(new[] { "COM1", "COM3" }, TestViewModel.GetStandPortCandidates(hardware,
            new[] { "COM3", "COM2", "COM1" }));
    }

    [Fact]
    public void SaveLoadPreservesBothConnections_AndRejectsDanglingReferencesAtomically()
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        vm.RootGraph.Hardware = Hardware();
        vm.RootGraph.Nodes.Add(new StartNodeViewModel());
        vm.RootGraph.Nodes.Add(new SerialConsoleNodeViewModel { DeviceId = "processor", Command = "version" });
        var json = GraphSerializer.Serialize(vm, "Новый прибор · Проверка");
        Assert.Contains("Start (connections v1)", json);
        GraphSerializer.Deserialize(json, vm);
        Assert.Equal("SIM-MODBUS", vm.RootGraph.Hardware!.PortName);
        Assert.Equal(Hardware().ConsoleDevices.Single(), vm.RootGraph.Hardware.ConsoleDevices.Single());
        Assert.Equal("processor", vm.RootGraph.Nodes.OfType<SerialConsoleNodeViewModel>().Single().DeviceId);
        var broken = JsonNode.Parse(json)!;
        broken["hardware"]!["ConsoleDevices"] = new JsonArray();
        var before = vm.RootGraph.Nodes.ToArray();
        Assert.Throws<InvalidOperationException>(() => GraphSerializer.Deserialize(broken.ToJsonString(), vm));
        Assert.Equal(before, vm.RootGraph.Nodes);
    }

    [Theory]
    [InlineData("duplicate-id")][InlineData("same-port")][InlineData("modbus-port")]
    [InlineData("encoding")][InlineData("line-ending")][InlineData("null-port")]
    public void InvalidConnectionMapsAreRejected(string error)
    {
        var config = Hardware(); var console = config.ConsoleDevices.Single();
        switch (error)
        {
            case "duplicate-id": config.ConsoleDevices.Add(console with { PortName = "ANOTHER" }); break;
            case "same-port": config.ConsoleDevices.Add(console with { Id = "other" }); break;
            case "modbus-port": console.PortName = config.PortName!; break;
            case "encoding": console.EncodingName = "unknown"; break;
            case "line-ending": console.LineEnding = "command"; break;
            case "null-port": console.PortName = null!; break;
        }
        Assert.Throws<InvalidOperationException>(config.Validate);
    }

    [Fact]
    public void PhysicalPortReceivesAllProfileSerialSettingsWithoutOpeningIt()
    {
        var definition = Hardware().ConsoleDevices.Single();
        using var port = definition.CreatePort();
        Assert.False(port.IsOpen); Assert.Equal(definition.PortName, port.PortName);
        Assert.Equal(57600, port.BaudRate); Assert.Equal(7, port.DataBits);
        Assert.Equal(Parity.Even, port.Parity); Assert.Equal(StopBits.Two, port.StopBits);
        Assert.Equal("us-ascii", port.Encoding.WebName); Assert.True(port.DtrEnable); Assert.True(port.RtsEnable);
    }

    [Fact]
    public async Task PartialNestedRunResolvesDeviceFromRoot_AndKeepsCompiledSnapshot()
    {
        var hardware = Hardware();
        var body = new GraphWorkspaceViewModel();
        var console = new SerialConsoleNodeViewModel { DeviceId = "processor", Command = "version", OutputVariableName = "Reply" };
        body.Nodes.Add(console);
        var transport = new RecordingConsole(); using var bus = new ModbusService(); using var http = new HttpRequestService();
        using var compiler = new GraphCompiler(bus, http, NullLogger.Instance, transport) { ProfileHardware = hardware };
        var graph = compiler.Compile(body, console, true);
        hardware.ConsoleDevices[0].PortName = "CHANGED-AFTER-COMPILE";
        var ctx = new TestContext(new RegisterState());
        Assert.Equal(ExecutionStatus.Completed, await new TestExecutor().ExecuteAsync(graph.StartNode, ctx, default));
        Assert.Equal("SIM-UART", Assert.Single(transport.Calls).PortName);
        Assert.Equal("ok", ctx.GetVariable<string>("Reply"));
        Assert.Equal("\r\n", transport.Calls[0].LineEnding);
    }

    [Fact]
    public async Task MissingNamedDeviceNeverFallsBackToGlobalConsolePort()
    {
        var console = new RecordingConsole(); var ctx = new TestContext(new RegisterState());
        ctx.SetVariable("Reply", "old");
        var step = new SerialConsoleStep(new() { DeviceId = "missing", OutputVariableName = "Reply" }, console, NullLogger.Instance);
        Assert.Equal(StepResult.False, await step.ExecuteAsync(ctx, default));
        Assert.Empty(console.Calls); Assert.False(ctx.Variables.ContainsKey("Reply"));
    }

    [Fact]
    public void UnassignedOrConflictingDutPortStopsPreflight_ButDisabledStagesDoNot()
    {
        var hardware = Hardware(); var graph = new GraphWorkspaceViewModel();
        var sub = new SubtestNodeViewModel(); graph.Nodes.Add(sub);
        sub.BodyGraph.Nodes.Add(new SerialConsoleNodeViewModel { DeviceId = "processor" });
        hardware.ConsoleDevices[0].PortName = "";
        hardware.Validate(); // Templates remain editable before choosing local ports.
        Assert.Throws<InvalidOperationException>(() => GraphCompiler.ValidateRunConnections(graph, hardware, "SIM-MODBUS"));
        sub.IsEnabled = false; GraphCompiler.ValidateRunConnections(graph, hardware, "SIM-MODBUS");
        sub.IsEnabled = true; hardware.PortName = ""; hardware.ConsoleDevices[0].PortName = "ACTUAL-MODBUS";
        Assert.Throws<InvalidOperationException>(() => GraphCompiler.ValidateRunConnections(graph, hardware, "ACTUAL-MODBUS"));
    }

    [Fact]
    public void OldHardwareAndConsoleNodesLoadWithLegacyDefaults()
    {
        var config = System.Text.Json.JsonSerializer.Deserialize<HardwareConfiguration>("{\"Devices\":[{\"Registers\":[{\"Address\":0}]}]}")!;
        config.Validate(); Assert.Null(config.PortName); Assert.Empty(config.ConsoleDevices);
        var options = System.Text.Json.JsonSerializer.Deserialize<SerialConsoleOptions>("{\"portName\":\"COM7\",\"baudRate\":9600}")!;
        Assert.Equal("", options.DeviceId); Assert.Equal("COM7", options.PortName); Assert.Equal(9600, options.BaudRate);
    }

    private sealed class RecordingConsole : ISerialConsoleSession
    {
        public List<ConsoleDeviceDefinition> Calls { get; } = new();
        public Task<string> ExchangeAsync(ConsoleDeviceDefinition device, string command, string terminator, int quietMs, int timeoutMs, CancellationToken token)
        { Calls.Add(device with { }); return Task.FromResult("ok"); }
        public Task<string> ExchangeAsync(string port, int baudRate, string command, string terminator, int quietMs, int timeoutMs, CancellationToken token)
            => throw new InvalidOperationException("A named device must not use legacy connection settings.");
    }
}
