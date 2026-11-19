using System.Text.Json.Nodes;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Graph;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

public class BranchRecoveryGraphTests
{
    internal static (CheckRegisterRangeNodeViewModel First, CheckRegisterRangeNodeViewModel Second, ConnectionViewModel Link)
        Fill(GraphWorkspaceViewModel graph, bool parallel = false)
    {
        graph.Clear();
        var start = new StartNodeViewModel { Location = new(0, 100) };
        var first = new CheckRegisterRangeNodeViewModel { CustomName = "Исходная проверка", SlaveId = 1, Address = 50, Min = 1, Max = 1, ReportName = "Результат проверки", Location = new(200, 100) };
        var restore = new DelayNodeViewModel { Milliseconds = 0, CustomName = "Восстановление", Location = new(200, 800) };
        var second = new CheckRegisterRangeNodeViewModel { CustomName = "Повторная проверка", SlaveId = 1, Address = 50, Min = 1, Max = 1, ReportName = "Результат проверки", Location = new(800, 800) };
        var resume = new DelayNodeViewModel { Milliseconds = 0, CustomName = "Продолжение теста", Location = new(1400, 100) };
        var end = new EndNodeViewModel { Location = new(1800, 100) };
        foreach (var node in new NodeViewModel[] { start, first, restore, second, resume, end }) graph.Nodes.Add(node);
        graph.Connections.Add(new(start.Output[0], first.Input[0]));
        graph.Connections.Add(new(first.TrueOut, resume.Input[0]));
        graph.Connections.Add(new(first.FalseOut, restore.Input[0]));
        graph.Connections.Add(new(restore.Output[0], second.Input[0]));
        var link = new ConnectionViewModel(second.TrueOut, resume.Input[0]) { RecoversFailureOf = first };
        graph.Connections.Add(link); graph.Connections.Add(new(second.FalseOut, resume.Input[0]));
        graph.Connections.Add(new(resume.Output[0], end.Input[0]));
        if (parallel)
        {
            var other = new DelayNodeViewModel { Milliseconds = 0 }; graph.Nodes.Add(other);
            graph.Connections.Add(new(start.Output[0], other.Input[0])); graph.Connections.Add(new(other.Output[0], resume.Input[0]));
        }
        return (first, second, link);
    }

    [Theory]
    [InlineData(true, false, false)] [InlineData(false, false, false)] [InlineData(true, true, false)] [InlineData(false, true, false)]
    [InlineData(true, false, true)] [InlineData(false, false, true)]
    public async Task SavedGraphRecoversOnlyAfterTheLinkedSuccessfulTransition(bool retryPasses, bool parallel, bool parallelRecovery)
    {
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        var (_, second, _) = Fill(vm.RootGraph, parallel);
        if (parallelRecovery)
        {
            var fork = vm.Nodes.OfType<DelayNodeViewModel>().First(); var sibling = new DelayNodeViewModel { Milliseconds = 0 };
            vm.Nodes.Add(sibling); vm.Connections.Add(new(fork.Output[0], sibling.Input[0]));
            vm.Connections.Add(new(sibling.Output[0], second.Input[0]));
        }
        var json = GraphSerializer.Serialize(vm, "Ветвление");
        Assert.Contains("Branch Recovery (v1): Check Register Range", json);
        GraphSerializer.Deserialize(json, vm);
        var marked = Assert.Single(vm.Connections.Where(c => c.RecoversFailureOf != null));
        Assert.Equal("Исходная проверка", marked.RecoversFailureOf!.CustomName);
        var context = new TestContext(new RegisterState()) { ExecutionObserver = new Observer(retryPasses) };
        using var compiler = new GraphCompiler(bus, NullLogger.Instance);
        Assert.Equal(retryPasses ? ExecutionStatus.Completed : ExecutionStatus.Failed,
            await new TestExecutor().ExecuteAsync(compiler.Compile(vm.RootGraph).StartNode, context, default));
        context.SetVariable("SerialNumber", 3300001);
        var report = new BuildTestReportStep(NullLogger.Instance, "Report", "Stand", "SerialNumber", "", "production", false);
        Assert.Equal(StepResult.True, await report.ExecuteAsync(context, default));
        Assert.Contains("test_result=true=" + (retryPasses ? "1" : "0"), context.GetVariable<string>("Report"));
        if (retryPasses) Assert.True(Assert.Single(context.ReportEntries).IsSuccess);
        else Assert.Equal(2, context.ReportEntries.Count(e => !e.IsSuccess));
    }

    [Theory]
    [InlineData("false-output")] [InlineData("linear-output")] [InlineData("foreign-node")] [InlineData("wrong-branch")]
    public void InvalidRecoveryIsRejectedBeforeExecution(string problem)
    {
        var graph = new GraphWorkspaceViewModel(); var (first, second, link) = Fill(graph);
        graph.Connections.Remove(link);
        var invalid = problem switch
        {
            "false-output" => new ConnectionViewModel(second.FalseOut, link.Target) { RecoversFailureOf = first },
            "linear-output" => new ConnectionViewModel(graph.Nodes.OfType<DelayNodeViewModel>().First().Output[0], link.Target) { RecoversFailureOf = first },
            "foreign-node" => new ConnectionViewModel(link.Source, link.Target) { RecoversFailureOf = new CheckRegisterRangeNodeViewModel() },
            _ => new ConnectionViewModel(first.TrueOut, link.Target) { RecoversFailureOf = second }
        };
        graph.Connections.Add(invalid);
        Assert.Throws<InvalidOperationException>(() => BranchRecoveryValidation.Validate(graph));
    }

    [Fact]
    public void RecoveryCannotCrossIntoAnotherParallelExecutionScope()
    {
        var graph = new GraphWorkspaceViewModel(); var (first, second, _) = Fill(graph);
        var fork = graph.Nodes.OfType<DelayNodeViewModel>().First();
        var sibling = new DelayNodeViewModel { Milliseconds = 0 }; graph.Nodes.Add(sibling);
        graph.Connections.Add(new(fork.Output[0], sibling.Input[0]));
        graph.Connections.Add(new(sibling.Output[0], graph.Nodes.OfType<DelayNodeViewModel>().Last(n => n != sibling).Input[0]));
        using var bus = new ModbusService(); using var compiler = new GraphCompiler(bus, NullLogger.Instance);
        Assert.Contains("одной параллельной ветви", Assert.Throws<InvalidOperationException>(() => compiler.Compile(graph)).Message);
    }

    [Fact]
    public void MissingRecoveryReferenceInJsonIsNotSilentlyIgnored()
    {
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        Fill(vm.RootGraph);
        var json = JsonNode.Parse(GraphSerializer.Serialize(vm, "Ветвление"))!;
        var marked = json["connections"]!.AsArray().OfType<JsonObject>().Single(c => c["recoversFailureOf"] != null);
        marked["recoversFailureOf"] = "missing";
        Assert.Contains("отсутствующую ноду", Assert.Throws<InvalidOperationException>(() => GraphSerializer.Deserialize(json.ToJsonString(), vm)).Message);
    }

    [Fact]
    public void EditingCopyingAndDeletingPreserveTheMeaningOfTheRecoveryLink()
    {
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus)); vm.IsOperatorView = false;
        var (first, second, link) = Fill(vm.RootGraph); link.RecoversFailureOf = null;
        vm.SetBranchRecovery(link, first); Assert.Same(first, link.RecoversFailureOf);
        vm.UndoCommand.Execute(null); Assert.Null(link.RecoversFailureOf);
        vm.RedoCommand.Execute(null); Assert.Same(first, link.RecoversFailureOf);
        foreach (var node in vm.Nodes.Where(n => n is not StartNodeViewModel and not EndNodeViewModel)) vm.SelectedNodes.Add(node);
        Assert.True(vm.CopyNodes()); vm.PasteNodes();
        var copy = vm.Connections.Last(c => c.RecoversFailureOf != null);
        Assert.NotSame(first, copy.RecoversFailureOf); Assert.NotSame(second, copy.Source.Parent);
        Assert.Equal(first.CustomName, copy.RecoversFailureOf!.CustomName);
        vm.SelectedNodes.Clear(); vm.SelectedNodes.Add(first); vm.DeleteSelectedNodesCommand.Execute(null);
        Assert.Null(link.RecoversFailureOf); // The ordinary B → main transition remains.
        Assert.Contains(link, vm.Connections);
        vm.UndoCommand.Execute(null); Assert.Same(first, link.RecoversFailureOf);
        vm.SelectedNodes.Clear(); vm.SelectedNodes.Add(second); vm.SelectedNodes.Add(link.Target.Parent!);
        Assert.False(vm.CopyNodes()); Assert.Contains("выделите также", vm.StatusMessage);
    }

    [Fact]
    public async Task FalseBranchMayReturnToTheSameCheckAndItsSuccessfulExitConfirmsRecovery()
    {
        using var bus = new ModbusService(); using var compiler = new GraphCompiler(bus, NullLogger.Instance);
        var graph = new GraphWorkspaceViewModel(); var (first, second, _) = Fill(graph);
        foreach (var connection in graph.Connections.Where(c => c.Source.Parent == second || c.Target.Parent == second).ToArray())
            graph.Connections.Remove(connection);
        graph.Nodes.Remove(second);
        graph.Connections.Add(new(graph.Nodes.OfType<DelayNodeViewModel>().First().Output[0], first.Input[0]));
        var exit = graph.Connections.Single(c => c.Source == first.TrueOut); exit.RecoversFailureOf = first;
        var observer = new SequenceObserver(); var context = new TestContext(new RegisterState()) { ExecutionObserver = observer };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.Equal(ExecutionStatus.Completed, await new TestExecutor().ExecuteAsync(compiler.Compile(graph).StartNode, context, cts.Token));
        Assert.Equal(2, observer.Calls); Assert.True(Assert.Single(context.ReportEntries).IsSuccess);
    }

    [Fact]
    public void NestedClipboardAndVersionWrappersKeepTheirOwnRecoveryReferences()
    {
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus)); vm.IsOperatorView = false;
        var subtest = new SubtestNodeViewModel { Name = "Восстановление" };
        var (_, second, link) = Fill(subtest.BodyGraph);
        second.RunOnlyOnFirstRun = true; second.ResultVariableName = "RepeatCheck";
        vm.Nodes.Clear(); vm.Nodes.Add(subtest); vm.SelectedNodes.Add(subtest);
        Assert.True(vm.CopyNodes()); vm.PasteNodes();
        var body = vm.Nodes.OfType<SubtestNodeViewModel>().Last().BodyGraph;
        var copied = Assert.Single(body.Connections.Where(c => c.RecoversFailureOf != null));
        Assert.NotSame(link.RecoversFailureOf, copied.RecoversFailureOf);
        Assert.Contains(copied.RecoversFailureOf!, body.Nodes);
        var json = GraphSerializer.Serialize(vm, "Вложенный граф");
        Assert.Contains("Profile Open (v2): Branch Recovery (v1): Check Register Range (capture v1)", json);
        GraphSerializer.Deserialize(json, vm);
        foreach (var restored in vm.Nodes.OfType<SubtestNodeViewModel>())
        {
            var marked = Assert.Single(restored.BodyGraph.Connections.Where(c => c.RecoversFailureOf != null));
            var confirm = Assert.IsType<CheckRegisterRangeNodeViewModel>(marked.Source.Parent);
            Assert.True(confirm.RunOnlyOnFirstRun); Assert.Equal("RepeatCheck", confirm.ResultVariableName);
            Assert.Contains(marked.RecoversFailureOf!, restored.BodyGraph.Nodes);
        }
    }

    private sealed class SequenceObserver : IExecutionObserver
    {
        public int Calls { get; private set; }
        public Task NodeStartedAsync(TestNode node, TestContext context, CancellationToken token)
        {
            if (node.Source is CheckRegisterRangeNodeViewModel) context.RegisterState.Update(1, 50, (ushort)(++Calls > 1 ? 1 : 0));
            return Task.CompletedTask;
        }
        public Task NodeFinishedAsync(TestNode node, TestContext context, CancellationToken token) => Task.CompletedTask;
        public Task NodeFailedAsync(TestNode node, TestContext context, CancellationToken token) => Task.CompletedTask;
    }

    private sealed class Observer(bool retryPasses) : IExecutionObserver
    {
        public Task NodeStartedAsync(TestNode node, TestContext context, CancellationToken token)
        {
            if (node.Source is CheckRegisterRangeNodeViewModel check)
                context.RegisterState.Update(1, 50, (ushort)(check.CustomName == "Повторная проверка" && retryPasses ? 1 : 0));
            return Task.CompletedTask;
        }
        public Task NodeFinishedAsync(TestNode node, TestContext context, CancellationToken token) => Task.CompletedTask;
        public Task NodeFailedAsync(TestNode node, TestContext context, CancellationToken token) => Task.CompletedTask;
    }
}
