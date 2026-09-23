using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

public class Psw2G6FProfileTests
{
    [Theory]
    [InlineData("PSW_2G6F_plus_full_algorithm.json")]
    public void PoeLoopMatchesModelLimitsAndQtReadTiming(string file)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "profiles", file));
        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        GraphSerializer.Deserialize(File.ReadAllText(path), vm);
        var poe = vm.RootGraph.Nodes.OfType<ForEachSlaveNodeViewModel>()
            .Single(n => n.BodyGraph.Nodes.OfType<CheckRegisterRangeNodeViewModel>().Any());
        Assert.Equal((byte)1, poe.FromSlaveId);
        Assert.Equal((byte)11, poe.ToSlaveId);
        Assert.Equal((byte)2, poe.Step);
        var checks = poe.BodyGraph.Nodes.OfType<CheckRegisterRangeNodeViewModel>().ToArray();
        Assert.Equal(new ushort[] { 1403, 1402 }, checks.Select(n => n.Address));
        Assert.All(checks, n =>
        {
            Assert.True(n.UseCurrentSlaveId);
            Assert.True(n.LiveRead);
            Assert.Equal(53000, n.Min);
            Assert.Equal(56000, n.Max);
            Assert.Equal(3, n.ReadAttempts);
            Assert.Equal(600, n.ReadIntervalMs);
        });
        Assert.Equal(new[] { 200, 200 }, poe.BodyGraph.Nodes.OfType<DelayNodeViewModel>()
            .Select(n => n.Milliseconds));
        Assert.NotNull(new GraphCompiler(modbus, NullLogger.Instance).Compile(vm.RootGraph));
        GraphSerializer.Deserialize(GraphSerializer.Serialize(vm, file), vm);
        Assert.Equal(2, vm.RootGraph.Nodes.OfType<ForEachSlaveNodeViewModel>()
            .Single(n => n.BodyGraph.Nodes.OfType<CheckRegisterRangeNodeViewModel>().Any())
            .BodyGraph.Nodes.OfType<DelayNodeViewModel>().Count());
    }

    [Fact]
    public void FullProfileCompilesAndRoundTripsWithFirmwareUpdate()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "profiles", "PSW_2G6F_plus_full_algorithm.json"));
        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        var name = GraphSerializer.Deserialize(File.ReadAllText(path), vm);
        Assert.Equal("PSW_2G6F_plus_full_algorithm", name);
        AssertProfile(vm, modbus);
        var json = GraphSerializer.Serialize(vm, name);
        GraphSerializer.Deserialize(json, vm);
        AssertProfile(vm, modbus);
    }

    private static void AssertProfile(TestViewModel vm, ModbusService modbus)
    {
        Assert.NotNull(new GraphCompiler(modbus, NullLogger.Instance).Compile(vm.RootGraph));
        Assert.Equal(15, vm.RootGraph.Nodes.Count);
        Assert.Equal(12, vm.RootGraph.Connections.Count);
        Assert.Contains(vm.AvailableNodes, n => n is SetPswMacNodeViewModel);
        var main = vm.RootGraph.Nodes.Single(n => n is StartNodeViewModel);
        var mainPath = new List<NodeViewModel>();
        while (main is not EndNodeViewModel)
        {
            mainPath.Add(main);
            main = vm.RootGraph.Connections.Single(c => ReferenceEquals(c.Source.Parent, main)).Target.Parent!;
            Assert.True(mainPath.Count <= 12);
        }
        Assert.Equal(12, mainPath.Count);
        foreach (var graph in Graphs(vm.RootGraph))
        {
            // Sequential bodies must have an uninterrupted success path.
            Assert.True(graph.Connections.Count >= graph.Nodes.Count - 1 || ReferenceEquals(graph, vm.RootGraph));
        }
        var nodes = Graphs(vm.RootGraph).SelectMany(g => g.Nodes).ToArray();
        Assert.DoesNotContain(nodes, n => n is SetProMacNodeViewModel);
        Assert.DoesNotContain(nodes.OfType<CheckVariableEqualityNodeViewModel>(), n => n.VariableName == "Migration.ProductionReady");
        Assert.All(nodes.OfType<SelfTestCheckNodeViewModel>(), n =>
        {
            Assert.Equal("http://192.168.0.1/test.shtml", n.Url);
            Assert.DoesNotContain("firmvare_vers", n.ValidationRules);
        });
        Assert.DoesNotContain(nodes.OfType<ModbusWriteNodeViewModel>(), n => n.Address == 1507);
        var firmware = Assert.Single(nodes.OfType<UpdatePswFirmwareNodeViewModel>());
        Assert.Equal("0.2.8", firmware.TargetVersion);
        Assert.EndsWith("sw407-0.2.8-01.06.2021.img", firmware.FirmwarePath);
        Assert.True(firmware.ForceUpdate);
        var firmwareGraph = Graphs(vm.RootGraph).Single(g => g.Nodes.Contains(firmware));
        Assert.IsType<SelfTestCheckNodeViewModel>(firmwareGraph.Connections
            .Single(c => ReferenceEquals(c.Target.Parent, firmware)).Source.Parent);
        Assert.Equal(new[] { "Dut.sensor_1" }, nodes.OfType<WaitVariableUntilNodeViewModel>()
            .Where(n => n.VariableName.StartsWith("Dut.sensor_"))
            .Select(n => n.VariableName));
        var data = Assert.Single(nodes.OfType<RunDataTestNodeViewModel>());
        Assert.True(data.AllowGigabit);
        Assert.Equal(4, data.PortsText.Split('\n').Length);
        Assert.Contains("sfp6-7,192.168.0.8,192.168.0.9,1000", data.PortsText);
        Assert.Equal(2, data.AllowedTxDeficitPercent);
        Assert.True(data.Bidirectional);
        var serial = Assert.Single(nodes.OfType<GetSerialNumberFromServerNodeViewModel>());
        Assert.Equal("PSW-2G6F+", serial.DeviceType);
        Assert.False(serial.UseFixedSerialNumber);
        var mac = Assert.Single(nodes.OfType<BuildMacFromSerialNodeViewModel>());
        Assert.Equal(600000, mac.SerialOffset);
        Assert.Equal("C0:11:A6:06", mac.MacPrefix);
        var udp = Assert.Single(nodes.OfType<SetPswMacNodeViewModel>());
        Assert.Equal(6123, udp.LocalPort);
        Assert.Equal(43962, udp.UdpPort);
        Assert.IsType<SetPswMacNodeViewModel>(udp.Clone());
        var provisioning = Graphs(vm.RootGraph).Single(g => g.Nodes.Contains(udp));
        var sequence = new List<NodeViewModel>();
        var current = provisioning.Nodes.Single(n => n is StartNodeViewModel);
        while (current is not EndNodeViewModel)
        {
            sequence.Add(current);
            current = provisioning.Connections.Single(c => ReferenceEquals(c.Source.Parent, current)).Target.Parent!;
            Assert.True(sequence.Count < 30);
        }
        var position = sequence.IndexOf(udp);
        Assert.Equal(7000, Assert.IsType<DelayNodeViewModel>(sequence[position + 1]).Milliseconds);
        Assert.Equal(0, Assert.IsType<ModbusWriteNodeViewModel>(sequence[position + 2]).Value);
        Assert.Equal(10000, Assert.IsType<DelayNodeViewModel>(sequence[position + 3]).Milliseconds);
        var comparison = Assert.IsType<CompareVariablesNodeViewModel>(sequence[^1]);
        Assert.Equal("DutAfterMac.default_mac", comparison.LeftVariableName);
        Assert.Equal("Dut.NewMac", comparison.RightVariableName);
        Assert.Equal(VariableComparisonType.MacAddress, comparison.ComparisonType);
        var label = Assert.Single(nodes.OfType<PrintLabelNodeViewModel>());
        Assert.Equal(DeviceLabelModel.Psw2G6FPlus, label.LabelModel);
        Assert.Equal("DutAfterMac.default_mac", label.MacVariableName);
        Assert.Equal(4, label.Copies);
        Assert.True(label.UseQtProZplFormat);
        Assert.Equal(label.LabelModel, Assert.IsType<PrintLabelNodeViewModel>(label.Clone()).LabelModel);
        Assert.Equal(2, vm.RootGraph.Nodes.OfType<SubtestNodeViewModel>().Count(n => n.RunOnFailure));
        var cleanup = vm.RootGraph.Nodes.OfType<SubtestNodeViewModel>().First(n => n.RunOnFailure);
        Assert.Equal(10, cleanup.BodyGraph.Connections.Count);
        Assert.Equal(9, cleanup.BodyGraph.Nodes.OfType<ForEachSlaveNodeViewModel>().Single().BodyGraph.Connections.Count);
        Assert.All(nodes.OfType<BuildTestReportNodeViewModel>(), n => Assert.Contains("прошивка 0.2.8", n.TestType));
    }

    [Theory]
    [InlineData("\"portsText\": \"sfp,192.168.0.8,192.168.0.9,1000\"")]
    [InlineData("\"ports\": [{\"name\":\"sfp\",\"inIp\":\"192.168.0.8\",\"outIp\":\"192.168.0.9\",\"bandwidthMbps\":1000}]")]
    public void GigabitIsExplicitAndSurvivesSaveLoadAndClone(string ports)
    {
        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        var json = "{\"nodes\":[{\"id\":\"0\",\"type\":\"Run Data Test\",\"allowGigabit\":true,\"targetBandwidthMbps\":1000," + ports + "}],\"connections\":[]}";
        GraphSerializer.Deserialize(json, vm);
        GraphSerializer.Deserialize(GraphSerializer.Serialize(vm, "gigabit"), vm);
        var node = Assert.Single(vm.RootGraph.Nodes.OfType<RunDataTestNodeViewModel>());
        Assert.Equal(1000, node.TargetBandwidthMbps);
        Assert.EndsWith(",1000", node.PortsText);
        Assert.Equal(1000, Assert.IsType<RunDataTestNodeViewModel>(node.Clone()).TargetBandwidthMbps);
        node.AllowGigabit = false;
        Assert.Equal(100, node.TargetBandwidthMbps);
        Assert.Equal(100, RunDataTestStep.NormalizeBandwidth(1000));
        Assert.Equal(1000, RunDataTestStep.NormalizeBandwidth(1000, true));
        Assert.Equal(1000, RunDataTestStep.NormalizeBandwidth(10000, true));
    }

    private static IEnumerable<GraphWorkspaceViewModel> Graphs(GraphWorkspaceViewModel graph)
    {
        yield return graph;
        foreach (var child in graph.Nodes.OfType<ICompositeNodeViewModel>().SelectMany(n => Graphs(n.BodyGraph)))
            yield return child;
    }
}
