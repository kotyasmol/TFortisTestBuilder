using System.Text;
using System.Text.Json;
using Avalonia.Media;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Models;
using TestBuilder.Services;
using TestBuilder.Services.Graph;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.StepTests;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

[Collection(AppSettingsTestCollection.Name)]
public class ProfileModelAdaptationTests
{
    const string Pro = "PSW+UPS-Box 8x2Pro · Параллельная проверка с SFP · 11.11.26.json";
    static NodeViewModel[] Nodes(TestViewModel vm) => ProfileModelAdaptation.All(vm.RootGraph).ToArray();
    static string Save(TestViewModel vm) => GraphSerializer.Serialize(vm, vm.CurrentProfileName);
    static string Geometry(GraphWorkspaceViewModel graph)
    {
        var values = new List<string>();
        void Visit(GraphWorkspaceViewModel g, string path)
        {
            for (var i = 0; i < g.Nodes.Count; i++)
            {
                var n = g.Nodes[i]; values.Add($"{path}/{i}:{n.Location}");
                if (n is ICompositeNodeViewModel c) Visit(c.BodyGraph, path + "/" + i);
            }
            foreach (var c in g.Connections) values.Add($"{path}:{g.Nodes.IndexOf(c.Source.Parent!)}:{c.Source.Title}>{g.Nodes.IndexOf(c.Target.Parent!)}:{c.Target.Title}");
        }
        Visit(graph, "root");
        for (var i = 0; i < graph.QuickActions.Count; i++)
        {
            Visit(graph.QuickActions[i].Graph, "action/" + i);
            Visit(graph.QuickActions[i].OffGraph, "off/" + i);
        }
        return string.Join("\n", values);
    }

    [Fact]
    public void ChangingModelUpdatesIdentityPreservesTopologyAndUndoRestoresTheDocument()
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
        GraphSerializer.Deserialize(File.ReadAllText(TestProfilePaths.Find(Pro)), vm);
        var original = Save(vm);
        var layout = Geometry(vm.RootGraph);
        var hardware = JsonSerializer.Serialize(vm.RootGraph.Hardware);
        var traffic = Nodes(vm).OfType<RunDataTestNodeViewModel>().Select(n => n.PortsText).ToArray();
        var selftest = Nodes(vm).OfType<SelfTestCheckNodeViewModel>().Select(n => (n.Url, n.ValidationRules)).ToArray();
        vm.SelectedEquipmentModel = vm.EquipmentModels.Single(m => m.Id == "psw-plus-box-8x2pro");
        Assert.Equal("PSW+Box 8x2Pro", vm.RootGraph.DeviceModel);
        Assert.True(vm.RootGraph.UseCatalogSerials);
        Assert.True(vm.HasModelReview);
        Assert.All(Nodes(vm).OfType<GetSerialNumberFromServerNodeViewModel>(), n =>
        {
            Assert.Equal("PSW+Box 8x2Pro", n.DeviceType);
            Assert.False(n.UseFixedSerialNumber); Assert.Equal(0, n.FixedSerialNumber); Assert.Empty(n.ExpectedSerialPrefix);
        });
        var range = Nodes(vm).OfType<CheckVariableRangeNodeViewModel>().Single(n => n.VariableName == "SerialNumber");
        Assert.Equal(3300001, range.Min); Assert.Equal(3365535, range.Max);
        var mac = Assert.Single(Nodes(vm).OfType<BuildMacFromSerialNodeViewModel>());
        Assert.Equal(3300000, mac.SerialOffset); Assert.Equal("C0:11:A6:21", mac.MacPrefix);
        Assert.Null(mac.ModelReview); // Known catalog identity is already bound.
        Assert.Equal("PSW+Box 8x2Pro", Assert.Single(Nodes(vm).OfType<SetProMacNodeViewModel>()).BoardVersion);
        Assert.Equal(layout, Geometry(vm.RootGraph));
        Assert.Equal(hardware, JsonSerializer.Serialize(vm.RootGraph.Hardware));
        Assert.Equal(traffic, Nodes(vm).OfType<RunDataTestNodeViewModel>().Select(n => n.PortsText));
        Assert.Equal(selftest, Nodes(vm).OfType<SelfTestCheckNodeViewModel>().Select(n => (n.Url, n.ValidationRules)));
        Assert.All(Nodes(vm).OfType<SelfTestCheckNodeViewModel>(), n => Assert.NotNull(n.ModelReview));
        Assert.All(Nodes(vm).OfType<RunDataTestNodeViewModel>(), n => Assert.NotNull(n.ModelReview));
        Assert.All(Nodes(vm).OfType<PrintLabelNodeViewModel>(), n =>
        {
            Assert.NotNull(n.ModelReview);
            Assert.Null(n.LabelTemplate); // Global layout remains dynamic.
            Assert.False(n.UseLastTestLabel); Assert.False(n.UseManualSerialNumber);
        });
        using var compiler = new GraphCompiler(bus, NullLogger.Instance);
        Assert.NotNull(compiler.Compile(vm.RootGraph)); // Including the original parallel boot/heater fork.
        var adapted = Save(vm);
        vm.UndoCommand.Execute(null);
        Assert.Equal(original, Save(vm)); Assert.False(vm.HasModelReview);
        vm.RedoCommand.Execute(null);
        Assert.Equal(adapted, Save(vm)); Assert.True(vm.HasModelReview);
        GraphSerializer.Deserialize(adapted, vm);
        Assert.Equal(adapted, Save(vm)); Assert.True(vm.HasModelReview);
    }

    [Theory]
    [InlineData("psw-plus-box-8x2pro", 3300579, "C0:11:A6:21:02:43", "003300579")]
    [InlineData("psw-2g8f-plus-box", 2600579, "C0:11:A6:1A:02:43", "002600579")]
    public async Task RetargetedLabelsUseTheNewModelSerialMacAndBarcode(string id, int serial, string mac, string barcode)
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
        GraphSerializer.Deserialize(File.ReadAllText(TestProfilePaths.Find(Pro)), vm);
        var print = Nodes(vm).OfType<PrintLabelNodeViewModel>().First();
        print.UseLastTestLabel = true; print.ManualSerialNumber = "3200428"; print.ManualMacAddress = "C0:11:A6:20:01:AC";
        vm.SelectedEquipmentModel = vm.EquipmentModels.Single(m => m.Id == id);
        Assert.False(print.UseLastTestLabel); Assert.False(print.UseManualSerialNumber);
        Assert.Empty(print.ManualSerialNumber); Assert.Empty(print.ManualMacAddress);
        var model = vm.EquipmentModels.Single(m => m.Id == id);
        var context = new TestContext(new RegisterState()) { ProductionModel = model, DeviceModel = model.Name };
        context.SetVariable("SerialNumber", serial); context.SetVariable("SerialNumber.Assigned", serial);
        context.SetVariable("Dut.default_mac", mac);
        var printer = new CapturePrinter();
        Assert.Equal(StepResult.True, await print.CreateStep(NullLogger.Instance, printer).ExecuteAsync(context, default));
        Assert.Equal(model.Name, context.GetVariable<string>("PrintLabel.DeviceName"));
        Assert.Equal(barcode, context.GetVariable<string>("PrintLabel.Barcode"));
        var command = Encoding.Latin1.GetString(printer.Bytes);
        Assert.Contains(model.Name, command); Assert.Contains(serial.ToString(), command);
        Assert.DoesNotContain("PSW+UPS-Box 8x2Pro", command); Assert.DoesNotContain("3200428", command);
    }

    [Fact]
    public void CustomLabelTextIsParameterizedWithoutReplacingOtherCommandsOrGraphData()
    {
        const string name = "PSW+UPS-Box 8x2Pro";
        var command = "^XA^FD" + name + "^FS^FO10,20^FD{{Serial}}^FS^XZ\n# path/" + name;
        var result = ProfileModelAdaptation.ParameterizeNames(command, [name]);
        Assert.Contains("^FD{{Name}}^FS", result);
        Assert.Contains("# path/" + name, result);
        Assert.Equal("A10,20,0,3,1,1,N,\"{{Name}}\"", ProfileModelAdaptation.ParameterizeNames("A10,20,0,3,1,1,N,\"" + name + "\"", [name]));
    }

    [Fact]
    public void LegacyHiddenHardwareChecksAreRemovedOnlyByAnExplicitModelChangeAndUndoRestoresThem()
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
        GraphSerializer.Deserialize(File.ReadAllText(TestProfilePaths.Find("PSW-2G6F+ · Полная проверка · 11.11.26.json")), vm);
        var before = Save(vm);
        Assert.Contains(Nodes(vm).OfType<SelfTestCheckNodeViewModel>(), n => n.Psw2G6FHardwareChecks);
        vm.SelectedEquipmentModel = vm.EquipmentModels.Single(m => m.Id == "psw-2g8f-plus-box");
        Assert.All(Nodes(vm).OfType<SelfTestCheckNodeViewModel>(), n => Assert.False(n.Psw2G6FHardwareChecks));
        Assert.Contains(Nodes(vm).Select(n => n.ModelReview?.Message ?? ""), text => text.Contains("Скрытые проверки"));
        vm.UndoCommand.Execute(null); Assert.Equal(before, Save(vm));
    }

    [Fact]
    public void ReviewCanBeConfirmedPerStagePersistsAndDoesNotInvalidatePreparation()
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
        var stage = new SubtestNodeViewModel();
        var child = new DelayNodeViewModel { ModelReview = new("m", "Модель", "Проверьте время") };
        stage.BodyGraph.Nodes.Add(child); vm.Nodes.Add(stage);
        Assert.True(stage.HasModelReview); Assert.True(vm.HasModelReview);
        var before = Save(vm); var key = FirstRunProfileKey.Create(null, before);
        stage.AcknowledgeModelReviewCommand.Execute(null);
        Assert.Null(child.ModelReview); Assert.False(stage.HasModelReview); Assert.False(vm.HasModelReview);
        Assert.Equal(key, FirstRunProfileKey.Create(null, Save(vm)));
        vm.UndoCommand.Execute(null); Assert.Equal(before, Save(vm));
        vm.RedoCommand.Execute(null); Assert.False(vm.HasModelReview);
        GraphSerializer.Deserialize(Save(vm), vm); Assert.False(vm.HasModelReview);
    }

    [Fact]
    public void CopyKeepsReviewMetadataAndChangingBackToAnotherModelReplacesTheReviewTarget()
    {
        using var bus = new ModbusService();
        using var source = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
        GraphSerializer.Deserialize(File.ReadAllText(TestProfilePaths.Find(Pro)), source);
        var bytes = Save(source);
        using var copy = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
        GraphSerializer.Deserialize(bytes, copy);
        copy.SelectedEquipmentModel = copy.EquipmentModels.Single(m => m.Id == "psw-plus-box-8x2pro");
        Assert.Equal(bytes, Save(source));
        GraphSerializer.Deserialize(Save(copy), copy);
        Assert.True(copy.HasModelReview);
        copy.SelectedEquipmentModel = copy.EquipmentModels.Single(m => m.Id == "psw-ups-box-8x2pro");
        Assert.All(Nodes(copy).Where(n => n.ModelReview != null), n => Assert.Equal("psw-ups-box-8x2pro", n.ModelReview!.ModelId));
    }

    [Fact]
    public void FailurePathsAreRedNormalAndSharedJoinAreBlueAndReviewIsYellow()
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
        var start = new StartNodeViewModel(); var check = new CheckVariableEqualityNodeViewModel();
        var error = new DelayNodeViewModel(); var end = new EndNodeViewModel();
        var cleanup = new SubtestNodeViewModel { RunOnFailure = true };
        var off = new ModbusWriteNodeViewModel(); cleanup.BodyGraph.Nodes.Add(off);
        foreach (var n in new NodeViewModel[] { start, check, error, end, cleanup }) vm.Nodes.Add(n);
        vm.Connections.Add(new(start.Output[0], check.Input[0]));
        vm.Connections.Add(new(check.Output[0], end.Input[0]));
        vm.Connections.Add(new(check.Output[1], error.Input[0]));
        vm.Connections.Add(new(error.Output[0], end.Input[0]));
        Color Is(NodeViewModel n) => ((ISolidColorBrush)n.NodeColorBrush).Color;
        Assert.Equal(Color.Parse("#38BDF8"), Is(check)); Assert.Equal(Is(check), Is(end));
        Assert.Equal(Color.Parse("#EF4444"), Is(error)); Assert.Equal(Is(error), Is(off));
        error.ModelReview = new("m", "Модель", "Проверьте паузу");
        Assert.Equal(Color.Parse("#EAB308"), Is(error));
        error.AcknowledgeModelReviewCommand.Execute(null); Assert.Equal(Color.Parse("#EF4444"), Is(error));
        GraphSerializer.Deserialize("""{"nodes":[{"id":"0","type":"Delay","color":"purple"}],"connections":[]}""", vm);
        Assert.Equal("purple", vm.Nodes[0].NodeColor); Assert.Equal(Color.Parse("#38BDF8"), Is(vm.Nodes[0]));
        vm.Nodes[0].SelectColorCommand.Execute("green"); Assert.Equal(Color.Parse("#22C55E"), Is(vm.Nodes[0]));
        vm.UndoCommand.Execute(null); Assert.Equal(Color.Parse("#38BDF8"), Is(vm.Nodes[0]));
    }

    [Theory]
    [InlineData("rtl-poe")]
    [InlineData("rtl-sw")]
    public void BoardModelAndBothButtonGraphsUseTheirOwnIdentityWithoutGuessedMacData(string id)
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
        GraphSerializer.Deserialize(File.ReadAllText(TestProfilePaths.Find(Pro)), vm);
        var action = new ProfileActionDefinition();
        var onReport = new SendTestReportNodeViewModel();
        var offPrint = new PrintLabelNodeViewModel { UseQtProZplFormat = true, UseLastTestLabel = true };
        action.Graph.Nodes.Add(onReport); action.OffGraph.Nodes.Add(offPrint);
        vm.RootGraph.QuickActions.Add(action);
        var original = Save(vm);
        vm.SelectedEquipmentModel = vm.EquipmentModels.Single(m => m.Id == id);
        Assert.Equal("Board", vm.RootGraph.ProductKind);
        Assert.Equal(RtlTestReport.Endpoint, onReport.Endpoint);
        Assert.False(offPrint.UseLastTestLabel); Assert.False(offPrint.UseQtProZplFormat);
        Assert.NotNull(offPrint.ModelReview);
        Assert.All(Nodes(vm).OfType<BuildMacFromSerialNodeViewModel>(), n =>
        {
            Assert.Equal(0, n.SerialOffset); Assert.Empty(n.MacPrefix); Assert.NotNull(n.ModelReview);
        });
        Assert.All(Nodes(vm).OfType<PrintLabelNodeViewModel>(), n => Assert.False(n.UseQtProZplFormat));
        if (id == "rtl-poe")
            Assert.All(Nodes(vm).OfType<GetSerialNumberFromServerNodeViewModel>(), n => Assert.Contains("ответа на отчёт", n.ModelReview!.Message));
        vm.UndoCommand.Execute(null); Assert.Equal(original, Save(vm));
    }

    [Fact]
    public async Task IncompleteNewCardDoesNotKeepOldIdentityAndCannotPrintItsLabel()
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
        GraphSerializer.Deserialize(File.ReadAllText(TestProfilePaths.Find(Pro)), vm);
        var model = new EquipmentModel("new-card", "Новое изделие");
        var serial = Nodes(vm).OfType<GetSerialNumberFromServerNodeViewModel>().Single();
        serial.UseFixedSerialNumber = true; serial.FixedSerialNumber = 3200999;
        vm.EquipmentModels.Add(model); vm.SelectedEquipmentModel = model;
        Assert.Empty(serial.DeviceType); Assert.False(serial.UseFixedSerialNumber); Assert.Equal(0, serial.FixedSerialNumber);
        Assert.NotNull(serial.ModelReview); Assert.True(vm.UseCatalogSerials);
        var print = Nodes(vm).OfType<PrintLabelNodeViewModel>().First();
        var context = new TestContext(new RegisterState()) { ProductionModel = model, DeviceModel = model.Name };
        context.SetVariable("SerialNumber", 3200999); context.SetVariable("Dut.default_mac", "C0:11:A6:20:03:E7");
        var printer = new CapturePrinter();
        Assert.Equal(StepResult.False, await print.CreateStep(NullLogger.Instance, printer).ExecuteAsync(context, default));
        Assert.Empty(printer.Bytes);
    }

    [Fact]
    public void UnknownReviewMetadataSurvivesRoundTrip()
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        GraphSerializer.Deserialize("""{"nodes":[{"id":"0","type":"Delay","modelReview":{"modelId":"new","modelName":"Новая","message":"Проверить","future":{"keep":[1,2]}}}],"connections":[]}""", vm);
        using var json = JsonDocument.Parse(Save(vm));
        Assert.Equal(2, json.RootElement.GetProperty("nodes")[0].GetProperty("modelReview").GetProperty("future").GetProperty("keep")[1].GetInt32());
    }

    sealed class CapturePrinter : IRawLabelPrinter
    {
        public byte[] Bytes { get; private set; } = [];
        public RawLabelPrintResult Print(string printerName, byte[] bytes, CancellationToken token)
        { Bytes = bytes; return RawLabelPrintResult.Ok(); }
    }
}
