using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

public class PartialGraphRunTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SelectedEntry_SkipsPredecessors_AndOptionallySuccessors(bool single)
    {
        var start = new StartNodeViewModel();
        var before = new DelayNodeViewModel { Milliseconds = 0 };
        var selected = new DelayNodeViewModel { Milliseconds = 0 };
        var after = new DelayNodeViewModel { Milliseconds = 0 };
        var graph = Graph(start, before, selected, after);
        Connect(graph, start, before); Connect(graph, before, selected); Connect(graph, selected, after);
        var normal = Compile(graph);
        Assert.Same(start, normal.StartNode.Source);
        var observer = new Observer();
        var status = await Execute(Compile(graph, selected, single), observer);
        Assert.Equal(ExecutionStatus.Completed, status);
        Assert.Equal(single ? new NodeViewModel[] { selected } : new NodeViewModel[] { selected, after }, observer.Nodes);
        Assert.Equal(4, graph.Nodes.Count);
        Assert.Equal(3, graph.Connections.Count);
        Assert.Same(before, normal.StartNode.Next!.Source);
    }

    [Fact]
    public async Task SingleCondition_DoesNotFollowFalseEdge_AndReportsFailure()
    {
        var check = new CheckVariableEqualityNodeViewModel { VariableName = "missing" };
        var recovery = new DelayNodeViewModel { Milliseconds = 0 };
        var graph = Graph(check, recovery);
        graph.Connections.Add(new(check.FalseOut, recovery.In));
        var observer = new Observer();
        Assert.Equal(ExecutionStatus.Failed, await Execute(Compile(graph, check, true), observer));
        Assert.Equal(new NodeViewModel[] { check }, observer.Nodes);
        observer.Nodes.Clear();
        Assert.Equal(ExecutionStatus.Completed, await Execute(Compile(graph, check, false), observer));
        Assert.Equal(new NodeViewModel[] { check, recovery }, observer.Nodes);
    }

    [Fact]
    public async Task StartFromFork_PreservesParallelBranches_AndExecutesJoinOnce()
    {
        var selected = new DelayNodeViewModel { Milliseconds = 0 };
        var a = new DelayNodeViewModel { Milliseconds = 0 };
        var b = new DelayNodeViewModel { Milliseconds = 0 };
        var join = new DelayNodeViewModel { Milliseconds = 0 };
        var graph = Graph(selected, a, b, join);
        Connect(graph, selected, a); Connect(graph, selected, b); Connect(graph, a, join); Connect(graph, b, join);
        var observer = new Observer();
        Assert.Equal(ExecutionStatus.Completed, await Execute(Compile(graph, selected), observer));
        Assert.Equal(4, observer.Nodes.Count);
        Assert.Equal(1, observer.Nodes.Count(n => n == join));
        Assert.Same(join, observer.Nodes.Last());
        observer.Nodes.Clear();
        await Execute(Compile(graph, a), observer);
        Assert.Equal(new NodeViewModel[] { a, join }, observer.Nodes);
    }

    [Fact]
    public async Task SingleComposite_ExecutesItsBody_ButNotParentSuccessor()
    {
        var subtest = new SubtestNodeViewModel();
        var bodyStart = subtest.BodyGraph.Nodes.OfType<StartNodeViewModel>().Single();
        var bodyEnd = subtest.BodyGraph.Nodes.OfType<EndNodeViewModel>().Single();
        Connect(subtest.BodyGraph, bodyStart, bodyEnd);
        var after = new DelayNodeViewModel { Milliseconds = 0 };
        var graph = Graph(subtest, after);
        graph.Connections.Add(new(subtest.SuccessOut, after.In));
        var observer = new Observer();
        await Execute(Compile(graph, subtest, true), observer);
        Assert.Equal(new NodeViewModel[] { subtest, bodyStart, bodyEnd }, observer.Nodes);
        observer.Nodes.Clear();
        await Execute(Compile(subtest.BodyGraph, bodyEnd), observer);
        Assert.Equal(new NodeViewModel[] { bodyEnd }, observer.Nodes);
    }

    [Fact]
    public async Task SingleNode_CancellationAndPauseStillApply()
    {
        var node = new DelayNodeViewModel { Milliseconds = 10000 };
        var graph = Compile(Graph(node), node, true);
        var paused = false;
        using var cts = new CancellationTokenSource();
        var context = new TestContext(new RegisterState()) { WaitIfPausedAsync = _ => { paused = true; cts.Cancel(); return Task.CompletedTask; } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TestExecutor().ExecuteAsync(graph.StartNode, context, cts.Token));
        Assert.True(paused);
    }

    [Fact]
    public void InvalidUnrelatedBody_DoesNotBlockPartialRun_AndForeignEntryIsRejected()
    {
        var node = new DelayNodeViewModel();
        var invalid = new SubtestNodeViewModel();
        invalid.BodyGraph.Clear();
        var graph = Graph(new StartNodeViewModel(), node, invalid);
        Assert.Same(node, Compile(graph, node).StartNode.Source);
        Assert.Throws<InvalidOperationException>(() => Compile(graph));
        Assert.Throws<InvalidOperationException>(() => Compile(graph, new DelayNodeViewModel(), true));
    }

    private static Task<ExecutionStatus> Execute(CompiledGraph graph, Observer observer) =>
        new TestExecutor().ExecuteAsync(graph.StartNode, new TestContext(new RegisterState()) { ExecutionObserver = observer }, CancellationToken.None);
    private static CompiledGraph Compile(GraphWorkspaceViewModel graph, NodeViewModel? entry = null, bool single = false)
    {
        using var modbus = new ModbusService();
        using var http = new HttpRequestService();
        using var compiler = new GraphCompiler(modbus, http, NullLogger.Instance);
        return compiler.Compile(graph, entry, single);
    }
    private static GraphWorkspaceViewModel Graph(params NodeViewModel[] nodes)
    {
        var graph = new GraphWorkspaceViewModel();
        foreach (var node in nodes) graph.Nodes.Add(node);
        return graph;
    }
    private static void Connect(GraphWorkspaceViewModel graph, NodeViewModel source, NodeViewModel target) =>
        graph.Connections.Add(new(source.Output[0], target.Input[0]));
    private sealed class Observer : IExecutionObserver
    {
        public List<NodeViewModel> Nodes { get; } = new();
        public Task NodeStartedAsync(TestNode node, TestContext context, CancellationToken token) { lock (Nodes) Nodes.Add((NodeViewModel)node.Source!); return Task.CompletedTask; }
        public Task NodeFinishedAsync(TestNode node, TestContext context, CancellationToken token) => Task.CompletedTask;
        public Task NodeFailedAsync(TestNode node, TestContext context, CancellationToken token) => Task.CompletedTask;
    }
}
