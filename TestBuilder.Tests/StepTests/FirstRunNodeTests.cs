using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.StepTests;

public class FirstRunNodeTests
{
    [Theory]
    [InlineData("Delay")] [InlineData("Check")] [InlineData("Subtest")] [InlineData("For")]
    public async Task SkippedNode_FollowsNormalOutput_WithoutCheckingReportingOrMarkingSuccess(string type)
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        NodeViewModel operation = type switch
        {
            "Delay" => new DelayNodeViewModel { Milliseconds = 60000 },
            "Check" => new CheckVariableEqualityNodeViewModel { VariableName = "missing" },
            "Subtest" => new SubtestNodeViewModel(),
            _ => new ForEachSlaveNodeViewModel()
        };
        operation.RunOnlyOnFirstRun = true;
        var start = new StartNodeViewModel(); var next = new DelayNodeViewModel { Milliseconds = 0 };
        vm.Nodes.Add(start); vm.Nodes.Add(operation); vm.Nodes.Add(next);
        vm.Connections.Add(new(start.Output[0], operation.Input[0]));
        vm.Connections.Add(new(operation.Output[0], next.Input[0]));
        using var compiler = new GraphCompiler(bus, NullLogger.Instance);
        var graph = compiler.Compile(vm.RootGraph);
        var observer = new Observer();
        var context = new TestContext(new RegisterState()) { SkipFirstRunNodes = true, ExecutionObserver = observer };
        Assert.Equal(ExecutionStatus.Completed, await new TestExecutor().ExecuteAsync(graph.StartNode, context, default));
        Assert.Equal(new[] { operation }, observer.Skipped);
        Assert.Contains(next, observer.Completed);
        Assert.DoesNotContain(operation, observer.Completed);
        Assert.Empty(context.ReportEntries); Assert.Empty(context.Variables);
    }

    [Fact]
    public async Task NestedParallelBranches_KeepSkipPolicy_AndStillJoinOnce()
    {
        var observer = new Observer();
        var context = new TestContext(new RegisterState()) { SkipFirstRunNodes = true, ExecutionObserver = observer };
        int joined = 0;
        var join = new TestNode(new ActionStep(() => { joined++; return StepResult.Next; }));
        var a = new TestNode(new ActionStep(() => throw new Exception("Must be skipped")), "A") { RunOnlyOnFirstRun = true, Next = join };
        var b = new TestNode(new ActionStep(() => throw new Exception("Must be skipped")), "B") { RunOnlyOnFirstRun = true, Next = join };
        var start = new TestNode(null);
        start.ParallelTransitions[StepResult.Next] = new ParallelFork(new[] { a, b }, join);
        Assert.Equal(ExecutionStatus.Completed, await new TestExecutor().ExecuteAsync(start, context, default));
        Assert.Equal(1, joined); Assert.Equal(new object[] { "A", "B" }, observer.Skipped);
    }

    [Fact]
    public void FirstRunFlag_RoundTripsNestedNodes_RejectsOlderRuntimes_AndCopiesWithUndo()
    {
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        var subtest = new SubtestNodeViewModel(); var delay = new DelayNodeViewModel();
        subtest.BodyGraph.Nodes.Add(delay); vm.Nodes.Add(subtest);
        delay.RunOnlyOnFirstRun = true;
        vm.UndoCommand.Execute(null); Assert.False(delay.RunOnlyOnFirstRun);
        vm.RedoCommand.Execute(null); Assert.True(delay.RunOnlyOnFirstRun);
        var json = GraphSerializer.Serialize(vm, "Прогрев");
        Assert.Contains("Profile Open (v2): Delay", json);
        GraphSerializer.Deserialize(json, vm);
        subtest = Assert.IsType<SubtestNodeViewModel>(vm.RootGraph.Nodes.Single());
        Assert.True(subtest.BodyGraph.Nodes.OfType<DelayNodeViewModel>().Single().RunOnlyOnFirstRun);
        vm.SelectedNodes.Add(subtest); vm.CopyNodes(); vm.PasteNodes();
        Assert.Equal(2, vm.RootGraph.Nodes.Count);
        Assert.All(vm.RootGraph.Nodes.OfType<SubtestNodeViewModel>(), n => Assert.True(n.BodyGraph.Nodes.OfType<DelayNodeViewModel>().Single().RunOnlyOnFirstRun));
    }

    [Fact]
    public void LegacyFirstRunWrapper_LoadsAndSavesAsProfileOpenVersionTwo()
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        vm.Nodes.Add(new DelayNodeViewModel { RunOnlyOnFirstRun = true });
        var legacy = GraphSerializer.Serialize(vm, "Legacy preparation")
            .Replace("Profile Open (v2): ", "First Run (v1): ")
            .Replace("runOnlyOnFirstRun", "legacyUnusedFlag");

        GraphSerializer.Deserialize(legacy, vm);

        Assert.True(Assert.IsType<DelayNodeViewModel>(Assert.Single(vm.Nodes)).RunOnlyOnFirstRun);
        var saved = GraphSerializer.Serialize(vm, "Preparation");
        Assert.Contains("Profile Open (v2): Delay", saved);
        Assert.DoesNotContain("First Run (v1):", saved);
    }

    [Fact]
    public void PreparationKey_IgnoresLayoutAndSaveDate_ButChangesWithOperations()
    {
        const string original = """{"name":"Test","updatedAt":"2020-01-01","nodes":[{"id":"1","type":"Delay","x":1,"y":2,"color":"blue","milliseconds":10}],"connections":[]}""";
        Assert.Equal(FirstRunProfileKey.Create(null, original), FirstRunProfileKey.Create(null, original.Replace("2020", "2026").Replace("\"x\":1", "\"x\":100").Replace("blue", "red")));
        Assert.NotEqual(FirstRunProfileKey.Create(null, original), FirstRunProfileKey.Create(null, original.Replace("milliseconds\":10", "milliseconds\":20")));
        Assert.NotEqual(FirstRunProfileKey.Create("/tmp/a.json", original), FirstRunProfileKey.Create("/tmp/b.json", original));
    }

    private sealed class ActionStep(Func<StepResult> action) : ITestStep
    {
        public Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken) => Task.FromResult(action());
    }
    private sealed class Observer : IExecutionObserver
    {
        public List<object> Skipped { get; } = new(); public List<object> Completed { get; } = new();
        public Task NodeSkippedAsync(TestNode node, TestContext context, CancellationToken ct) { Skipped.Add(node.Source!); return Task.CompletedTask; }
        public Task NodeCompletedAsync(TestNode node, StepResult result, TestContext context, CancellationToken ct) { Completed.Add(node.Source!); return Task.CompletedTask; }
        public Task NodeStartedAsync(TestNode node, TestContext context, CancellationToken ct) => Task.CompletedTask;
        public Task NodeFinishedAsync(TestNode node, TestContext context, CancellationToken ct) => Task.CompletedTask;
        public Task NodeFailedAsync(TestNode node, TestContext context, CancellationToken ct) => Task.CompletedTask;
    }
}
