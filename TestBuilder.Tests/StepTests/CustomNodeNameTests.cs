using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.StepTests;

public class CustomNodeNameTests
{
    [Fact]
    public void EditorAndOperatorNamesAreIndependent_AndEmptyNamesRestoreDefaults()
    {
        NodeViewModel[] nodes = [new ForEachSlaveNodeViewModel(), new DelayNodeViewModel(),
            new SubtestNodeViewModel { Name = "Питание" }, new OperatorActionNodeViewModel()];
        foreach (var node in nodes)
        {
            var title = node.DisplayTitle;
            var stationName = StationDashboardViewModel.DisplayName(node);
            node.CustomName = "  Delay  ";
            Assert.Equal("Delay", node.DisplayTitle);
            Assert.Equal(stationName, StationDashboardViewModel.DisplayName(node));
            node.OperatorName = "  Print Label  ";
            Assert.Equal("Print Label", StationDashboardViewModel.DisplayName(node));
            Assert.Equal("Delay", node.DisplayTitle);
            node.CustomName = "  ";
            Assert.Equal(title, node.DisplayTitle);
            Assert.Equal("Print Label", StationDashboardViewModel.DisplayName(node));
            node.OperatorName = "  ";
            Assert.Equal(stationName, StationDashboardViewModel.DisplayName(node));
        }
    }

    [Fact]
    public void LegacySharedNameIsRetained_AndClearingOperatorNameSurvivesSave()
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        const string legacy = """
            {"name":"Профиль","nodes":[{"id":"0","type":"Delay","customName":"Ожидание питания"}],"connections":[]}
            """;
        GraphSerializer.Deserialize(legacy, vm);
        var node = Assert.IsType<DelayNodeViewModel>(Assert.Single(vm.Nodes));
        Assert.Equal("Ожидание питания", node.DisplayTitle);
        Assert.Equal("Ожидание питания", StationDashboardViewModel.DisplayName(node));
        node.CustomName = "Таймер";
        Assert.Equal("Ожидание питания", StationDashboardViewModel.DisplayName(node));
        node.OperatorName = "  ";
        GraphSerializer.Deserialize(GraphSerializer.Serialize(vm, "Профиль"), vm);
        node = Assert.IsType<DelayNodeViewModel>(Assert.Single(vm.Nodes));
        Assert.Equal("Таймер", node.DisplayTitle);
        Assert.Equal("Задержка", StationDashboardViewModel.DisplayName(node));
    }

    [Fact]
    public void NestedNames_RoundTripCopyUndo_AndDoNotInvalidatePreparation()
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        var loop = new ForEachSlaveNodeViewModel();
        var delay = new DelayNodeViewModel();
        loop.BodyGraph.Nodes.Add(delay);
        vm.Nodes.Add(loop);
        var original = GraphSerializer.Serialize(vm, "Профиль");
        Assert.DoesNotContain("customName", original);
        Assert.DoesNotContain("operatorName", original);
        loop.CustomName = "Проверка портов";
        delay.CustomName = "Стабилизация питания";
        vm.UndoCommand.Execute(null);
        Assert.Empty(delay.CustomName);
        vm.RedoCommand.Execute(null);
        Assert.Equal("Стабилизация питания", delay.CustomName);
        delay.OperatorName = "Ждём готовности устройства";
        vm.UndoCommand.Execute(null);
        Assert.Empty(delay.OperatorName);
        vm.RedoCommand.Execute(null);
        var json = GraphSerializer.Serialize(vm, "Профиль");
        Assert.Equal(FirstRunProfileKey.Create(null, original), FirstRunProfileKey.Create(null, json));
        GraphSerializer.Deserialize(json, vm);
        loop = Assert.IsType<ForEachSlaveNodeViewModel>(Assert.Single(vm.Nodes));
        vm.SelectedNodes.Add(loop);
        vm.CopyNodes(); vm.PasteNodes();
        Assert.Equal(2, vm.Nodes.Count);
        Assert.All(vm.Nodes.OfType<ForEachSlaveNodeViewModel>(), copy =>
        {
            Assert.Equal("Проверка портов", copy.CustomName);
            Assert.Empty(copy.OperatorName);
            var copiedDelay = copy.BodyGraph.Nodes.OfType<DelayNodeViewModel>().Single();
            Assert.Equal("Стабилизация питания", copiedDelay.CustomName);
            Assert.Equal("Ждём готовности устройства", copiedDelay.OperatorName);
        });
        GraphSerializer.Deserialize(original, vm);
        Assert.Empty(Assert.Single(vm.Nodes).CustomName);
    }

    [Fact]
    public void DashboardUsesNamesForStageAndNestedLoop_RefreshPreservesResult()
    {
        var graph = new GraphWorkspaceViewModel();
        var start = new StartNodeViewModel();
        var outer = new ForEachSlaveNodeViewModel { CustomName = "Внешний цикл", OperatorName = "Проверка коммутаторов" };
        var inner = new ForEachSlaveNodeViewModel { OperatorName = "Проверка портов" };
        var delay = new DelayNodeViewModel { OperatorName = "Ждём готовности порта" };
        graph.Nodes.Add(start); graph.Nodes.Add(outer);
        graph.Connections.Add(new(start.Output[0], outer.Input[0]));
        outer.BodyGraph.Nodes.Clear(); outer.BodyGraph.Connections.Clear();
        var bodyStart = new StartNodeViewModel();
        outer.BodyGraph.Nodes.Add(bodyStart); outer.BodyGraph.Nodes.Add(inner);
        outer.BodyGraph.Connections.Add(new(bodyStart.Output[0], inner.Input[0]));
        var innerStart = inner.BodyGraph.Nodes.OfType<BodyStartNodeViewModel>().Single();
        inner.BodyGraph.Nodes.Add(delay);
        inner.BodyGraph.Connections.Add(new(innerStart.Output[0], delay.Input[0]));
        var dashboard = new StationDashboardViewModel();
        var context = new TestContext(new RegisterState());
        dashboard.BeginRun(graph);
        var stage = Assert.Single(dashboard.Stages);
        Assert.Equal(outer.OperatorName, stage.Caption);
        dashboard.NodeStarted(outer, context);
        dashboard.NodeStarted(inner, context);
        Assert.Equal("Шаг 1 из 1 — Проверка портов", stage.StepProgressText);
        dashboard.NodeStarted(delay, context);
        Assert.Equal("Шаг 1 из 1 — Проверка портов → Ждём готовности порта", stage.StepProgressText);
        dashboard.NodeFinished(delay, context);
        dashboard.NodeCompleted(outer, StepResult.True, context);
        outer.OperatorName = "Питание";
        dashboard.RefreshStageNames();
        Assert.Same(stage, Assert.Single(dashboard.Stages));
        Assert.Equal("Питание", stage.Caption);
        Assert.Equal(StationStageState.Completed, stage.State);
    }
}
