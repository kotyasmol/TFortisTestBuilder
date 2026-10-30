using System.Text.Json;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Models;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.ProfileActions;

public class ProfileActionTests
{
    [Fact]
    public async Task OneToggle_AlternatesOnOff_AndNeverDuplicatesAnActiveRun()
    {
        var calls = new List<bool>();
        var gate = new TaskCompletionSource<ProfileActionResult>();
        var button = new ProfileActionViewModel(new() { Display = ProfileActionDisplay.Toggle },
            (_, off, _, _) => { calls.Add(off); return gate.Task; }, () => true, () => { });
        var first = button.RunCommand.ExecuteAsync(null);
        Assert.True(button.IsRunning);
        Assert.False(button.RunCommand.CanExecute(null));
        await button.RunCommand.ExecuteAsync(null);
        Assert.Single(calls);
        gate.SetResult(new(true, "OK")); await first;
        Assert.True(button.IsStateKnown); Assert.True(button.IsOn);
        await button.RunCommand.ExecuteAsync(null);
        Assert.False(button.IsOn); Assert.True(button.IsStateKnown);
        await button.RunCommand.ExecuteAsync(null);
        Assert.Equal(new[] { false, true, false }, calls);
    }

    [Fact]
    public async Task FailureAndCancellation_DoNotClaimToggleState()
    {
        var fail = false;
        var directions = new List<bool>();
        var button = new ProfileActionViewModel(new() { Display = ProfileActionDisplay.Toggle },
            (_, off, _, _) => { directions.Add(off); return Task.FromResult(new ProfileActionResult(!fail, "не выполнено")); }, () => true, () => { });
        await button.RunCommand.ExecuteAsync(null); Assert.True(button.IsOn);
        fail = true; await button.RunCommand.ExecuteAsync(null);
        Assert.False(button.IsStateKnown); Assert.Contains("Ошибка", button.StateText);
        await button.RunCommand.ExecuteAsync(null);
        Assert.Equal(new[] { false, true, true }, directions);
        var waiting = new ProfileActionViewModel(new() { Display = ProfileActionDisplay.Toggle },
            async (_, _, _, token) => { await Task.Delay(Timeout.Infinite, token); return new(true, "OK"); }, () => true, () => { });
        var task = waiting.RunCommand.ExecuteAsync(null);
        waiting.Cancel(); await task;
        Assert.False(waiting.IsRunning); Assert.False(waiting.IsStateKnown);
        Assert.Contains("Остановлено", waiting.StateText);
    }

    [Theory]
    [InlineData("#15803D")]
    [InlineData(ProfileActionDefinition.SystemColor)]
    public void Serialization_CopiesBothGraphs_AndOldProfilesClearButtons(string color)
    {
        using var bus = new ModbusService();
        using var source = new TestViewModel(bus, new SlaveManager(bus));
        LoadEmpty(source);
        source.RootGraph.QuickActions.Add(new() { Name = "Питание", Display = ProfileActionDisplay.Toggle, Color = color, Graph = DelayGraph(12), OffGraph = DelayGraph(34) });
        var json = GraphSerializer.Serialize(source, "Копия");
        Assert.Contains("Start (actions v1)", json);
        using var copy = new TestViewModel(bus, new SlaveManager(bus));
        GraphSerializer.Deserialize(json, copy);
        var button = Assert.Single(copy.RootGraph.QuickActions);
        Assert.Equal("Питание", button.Name); Assert.Equal(color, button.Color); Assert.Equal(ProfileActionDisplay.Toggle, button.Display);
        Assert.Equal(12, button.Graph.Nodes.OfType<DelayNodeViewModel>().Single().Milliseconds);
        Assert.Equal(34, button.OffGraph.Nodes.OfType<DelayNodeViewModel>().Single().Milliseconds);
        Assert.NotSame(source.RootGraph.QuickActions[0].Graph.Nodes[0], button.Graph.Nodes[0]);
        Assert.False(copy.QuickActions[0].IsStateKnown);
        LoadEmpty(copy); Assert.Empty(copy.QuickActions); Assert.Empty(copy.RootGraph.QuickActions);
    }

    [Theory]
    [InlineData("display", "unknown")]
    [InlineData("color", "invalid")]
    [InlineData("name", "")]
    public void BadButton_DoesNotReplaceOpenProfile(string field, string value)
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        LoadEmpty(vm); vm.RootGraph.QuickActions.Add(new() { Name = "Сохранённая" });
        var original = vm.RootGraph.Nodes[0]; var action = vm.RootGraph.QuickActions[0];
        var dto = System.Text.Json.Nodes.JsonNode.Parse(GraphSerializer.Serialize(vm, "invalid"))!;
        dto["quickActions"]![0]![field] = value;
        Assert.Throws<InvalidOperationException>(() => GraphSerializer.Deserialize(dto.ToJsonString(), vm));
        Assert.Same(original, vm.RootGraph.Nodes[0]); Assert.Same(action, Assert.Single(vm.RootGraph.QuickActions));
    }

    [Fact]
    public void DuplicateIds_AreRejected()
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        vm.RootGraph.QuickActions.Add(new() { Id = "same" }); vm.RootGraph.QuickActions.Add(new() { Id = "same" });
        Assert.Throws<InvalidOperationException>(() => GraphSerializer.Serialize(vm, "test"));
    }

    [Fact]
    public async Task Actions_RunDuringMainTest_WithIndependentContexts_AndDoNotAllowProfileReplacement()
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        LoadEmpty(vm);
        vm.RootGraph.QuickActions.Add(new() { Name = "Пауза 1", Graph = DelayGraph(100) });
        vm.RootGraph.QuickActions.Add(new() { Name = "Пауза 2", Graph = DelayGraph(100) });
        vm.IsTestRunning = true;
        var one = vm.QuickActions[0]; var two = vm.QuickActions[1];
        Assert.True(one.RunCommand.CanExecute(null)); Assert.True(two.RunCommand.CanExecute(null));
        var first = one.RunCommand.ExecuteAsync(null); var second = two.RunCommand.ExecuteAsync(null);
        Assert.True(vm.HasRunningQuickActions); Assert.False(vm.CanChooseProfile); Assert.False(vm.CanEditGraph);
        Assert.Throws<InvalidOperationException>(() => LoadEmpty(vm));
        await Task.WhenAll(first, second);
        Assert.Equal("Выполнено", one.Status); Assert.Equal("Выполнено", two.Status);
        Assert.Equal(3, one.CompletedSteps); Assert.Equal(3, two.CompletedSteps);
        Assert.Equal(3, one.ExpectedSteps); Assert.Equal(1, one.Progress);
        Assert.True(vm.IsTestRunning); vm.IsTestRunning = false;
        Assert.True(vm.CanChooseProfile);
        var a = new ContextObserver(); var b = new ContextObserver();
        await Task.WhenAll(vm.ExecuteProfileActionAsync(one.Definition, false, a, default), vm.ExecuteProfileActionAsync(two.Definition, false, b, default));
        Assert.NotNull(a.Context); Assert.NotNull(b.Context); Assert.NotSame(a.Context, b.Context);
        Assert.NotSame(a.Context!.Variables, b.Context!.Variables);
        Assert.NotSame(a.Context.ReportEntries, b.Context.ReportEntries);
    }

    [Fact]
    public async Task MissingStand_IsReportedWithoutExecutingHardware()
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        var def = new ProfileActionDefinition();
        def.Graph.Nodes.Add(new ModbusWriteNodeViewModel());
        vm.RootGraph.QuickActions.Add(def);
        await vm.QuickActions[0].RunCommand.ExecuteAsync(null);
        Assert.Equal("Ошибка", vm.QuickActions[0].Status);
        Assert.Contains("Подключитесь", vm.QuickActions[0].ErrorMessage);
        Assert.False(bus.IsConnected);
    }

    internal static void LoadEmpty(TestViewModel vm) => GraphSerializer.Deserialize(JsonSerializer.Serialize(GraphSerializer.ExportActionGraph(ProfileActionDefinition.EmptyGraph("Тест"))), vm);
    internal static ViewModels.Graphs.GraphWorkspaceViewModel DelayGraph(int ms)
    {
        var graph = ProfileActionDefinition.EmptyGraph("Действие");
        var start = graph.Nodes[0]; var end = graph.Nodes[1];
        var delay = new DelayNodeViewModel { Milliseconds = ms, Location = new Avalonia.Point(450, 80) };
        end.Location = new Avalonia.Point(950, 80);
        graph.Nodes.Add(delay); graph.Connections.Clear();
        graph.Connections.Add(new(start.Output[0], delay.In)); graph.Connections.Add(new(delay.Out, end.Input[0]));
        return graph;
    }
    private sealed class ContextObserver : IExecutionObserver
    {
        public TestContext? Context;
        public Task NodeStartedAsync(TestNode n, TestContext c, CancellationToken t) { Context = c; return Task.CompletedTask; }
        public Task NodeFinishedAsync(TestNode n, TestContext c, CancellationToken t) => Task.CompletedTask;
        public Task NodeFailedAsync(TestNode n, TestContext c, CancellationToken t) => Task.CompletedTask;
    }
}
