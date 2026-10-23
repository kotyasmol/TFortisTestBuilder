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
    public void EmptyNamePreservesDefaults_AndCustomNameIsNotTranslated()
    {
        NodeViewModel[] nodes = [new ForEachSlaveNodeViewModel(), new DelayNodeViewModel(),
            new SubtestNodeViewModel { Name = "Питание" }, new OperatorActionNodeViewModel()];
        foreach (var node in nodes)
        {
            var title = node.DisplayTitle;
            var stationName = StationDashboardViewModel.DisplayName(node);
            node.CustomName = "  Delay  ";
            Assert.Equal("Delay", node.DisplayTitle);
            Assert.Equal("Delay", StationDashboardViewModel.DisplayName(node));
            node.CustomName = "  ";
            Assert.Equal(title, node.DisplayTitle);
            Assert.Equal(stationName, StationDashboardViewModel.DisplayName(node));
        }
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
        loop.CustomName = "Проверка портов";
        delay.CustomName = "Стабилизация питания";
        vm.UndoCommand.Execute(null);
        Assert.Empty(delay.CustomName);
        vm.RedoCommand.Execute(null);
        Assert.Equal("Стабилизация питания", delay.CustomName);
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
            Assert.Equal("Стабилизация питания", copy.BodyGraph.Nodes.OfType<DelayNodeViewModel>().Single().CustomName);
        });
        GraphSerializer.Deserialize(original, vm);
        Assert.Empty(Assert.Single(vm.Nodes).CustomName);
    }

    [Fact]
    public void DashboardUsesNamesForStageAndNestedLoop_RefreshPreservesResult()
    {
        var graph = new GraphWorkspaceViewModel();
        var start = new StartNodeViewModel();
        var outer = new ForEachSlaveNodeViewModel { CustomName = "Проверка коммутаторов" };
        var inner = new ForEachSlaveNodeViewModel { CustomName = "Проверка портов" };
        graph.Nodes.Add(start); graph.Nodes.Add(outer);
        graph.Connections.Add(new(start.Output[0], outer.Input[0]));
        outer.BodyGraph.Nodes.Clear(); outer.BodyGraph.Connections.Clear();
        var bodyStart = new StartNodeViewModel();
        outer.BodyGraph.Nodes.Add(bodyStart); outer.BodyGraph.Nodes.Add(inner);
        outer.BodyGraph.Connections.Add(new(bodyStart.Output[0], inner.Input[0]));
        var dashboard = new StationDashboardViewModel();
        var context = new TestContext(new RegisterState());
        dashboard.BeginRun(graph);
        var stage = Assert.Single(dashboard.Stages);
        Assert.Equal(outer.CustomName, stage.Caption);
        dashboard.NodeStarted(outer, context);
        dashboard.NodeStarted(inner, context);
        Assert.Equal("Шаг 1 из 1 — Проверка портов", stage.StepProgressText);
        dashboard.NodeCompleted(outer, StepResult.True, context);
        outer.CustomName = "Питание";
        dashboard.RefreshStageNames();
        Assert.Same(stage, Assert.Single(dashboard.Stages));
        Assert.Equal("Питание", stage.Caption);
        Assert.Equal(StationStageState.Completed, stage.State);
    }
}
