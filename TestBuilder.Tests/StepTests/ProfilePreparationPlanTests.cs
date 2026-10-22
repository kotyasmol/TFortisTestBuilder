using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services;
using TestBuilder.Services.Graph;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.StepTests;

public class ProfilePreparationPlanTests
{
    [Fact]
    public void Plan_UsesConnectionsAndStopsBeforeOrdinaryTesting()
    {
        var first = PreparationDelay();
        var second = new SubtestNodeViewModel { RunOnlyOnFirstRun = true };
        var test = new DelayNodeViewModel();
        var graph = Chain(first, second, test);
        graph.Nodes.Move(2, 0); // Collection order is unrelated to execution order.

        var plan = ProfilePreparationPlan.Create(graph);

        Assert.Equal(new NodeViewModel[] { first, second }, plan.Nodes);
        Assert.False(plan.RequiresStandConnection);
    }

    [Fact]
    public void Plan_AllowsNormalTestingToForkAfterPreparation()
    {
        var prep = PreparationDelay();
        var firstTest = new DelayNodeViewModel();
        var graph = Chain(prep, firstTest);
        var secondTest = new DelayNodeViewModel();
        graph.Nodes.Add(secondTest);
        graph.Connections.Add(new(prep.Output[0], secondTest.Input[0]));

        Assert.Equal(new[] { prep }, ProfilePreparationPlan.Create(graph).Nodes);
    }

    [Theory]
    [InlineData("after-test")]
    [InlineData("disconnected")]
    [InlineData("error-only")]
    public void Plan_RejectsPreparationOutsideInitialSuccessSequence(string location)
    {
        var prep = PreparationDelay();
        GraphWorkspaceViewModel graph;
        if (location == "after-test") graph = Chain(new DelayNodeViewModel(), prep);
        else
        {
            var test = new CheckVariableEqualityNodeViewModel();
            graph = Chain(test);
            graph.Nodes.Add(prep);
            if (location == "error-only") graph.Connections.Add(new(test.FalseOut, prep.Input[0]));
        }

        var error = Assert.Throws<InvalidOperationException>(() => ProfilePreparationPlan.Create(graph));
        Assert.Contains("подряд после", error.Message);
    }

    [Fact]
    public void Plan_RejectsPreparationForksAndCycles()
    {
        var first = PreparationDelay();
        var second = PreparationDelay();
        var graph = Chain(first, second);
        graph.Connections.Add(new(graph.Nodes.OfType<StartNodeViewModel>().Single().Output[0], second.Input[0]));
        Assert.Contains("последовательно", Assert.Throws<InvalidOperationException>(() => ProfilePreparationPlan.Create(graph)).Message);

        graph = Chain(first, second);
        graph.Connections.RemoveAt(graph.Connections.Count - 1);
        graph.Connections.Add(new(second.Output[0], first.Input[0]));
        Assert.Contains("цикл", Assert.Throws<InvalidOperationException>(() => ProfilePreparationPlan.Create(graph)).Message);
    }

    [Fact]
    public void Plan_RejectsSuccessConnectionToOutputBeforeExecutingPreparation()
    {
        var prep = PreparationDelay();
        var graph = Chain(prep);
        graph.Connections[0] = new(graph.Nodes.OfType<StartNodeViewModel>().Single().Output[0], prep.Output[0]);

        Assert.Contains("со входом", Assert.Throws<InvalidOperationException>(() => ProfilePreparationPlan.Create(graph)).Message);
    }

    [Fact]
    public void Plan_RejectsNestedFlagsUnlessEntireRootCompositePrepares()
    {
        var subtest = new SubtestNodeViewModel();
        subtest.BodyGraph.Nodes.Add(PreparationDelay());
        var graph = Chain(subtest);

        Assert.Contains("вложенном графе", Assert.Throws<InvalidOperationException>(() => ProfilePreparationPlan.Create(graph)).Message);
        subtest.RunOnlyOnFirstRun = true;
        Assert.Equal(new[] { subtest }, ProfilePreparationPlan.Create(graph).Nodes);
    }

    [Fact]
    public void Plan_RejectsMarkedEmergencyCleanup()
    {
        var cleanup = new SubtestNodeViewModel { RunOnlyOnFirstRun = true, RunOnFailure = true };

        Assert.Contains("аварийном завершении", Assert.Throws<InvalidOperationException>(() => ProfilePreparationPlan.Create(Chain(cleanup))).Message);
    }

    [Fact]
    public void Plan_RequiresStandOnlyForActivePreparationActions()
    {
        var prep = new SubtestNodeViewModel { RunOnlyOnFirstRun = true };
        prep.BodyGraph.Nodes.Add(new ModbusWriteNodeViewModel());
        var graph = Chain(prep);
        Assert.True(ProfilePreparationPlan.Create(graph).RequiresStandConnection);
        prep.IsEnabled = false;
        Assert.False(ProfilePreparationPlan.Create(graph).RequiresStandConnection);
    }

    [Fact]
    public async Task IsolatedPreparation_ExecutesCompleteBodyAndCannotReachDeviceCheck()
    {
        using var bus = new ModbusService();
        using var compiler = new GraphCompiler(bus, NullLogger.Instance);
        var prep = new SubtestNodeViewModel { Name = "Настройка", RunOnlyOnFirstRun = true };
        var nestedAction = PreparationDelay();
        CopyGraph(Chain(nestedAction), prep.BodyGraph);
        var deviceCheck = new CheckVariableEqualityNodeViewModel { VariableName = "device-data-missing" };
        var root = Chain(prep, deviceCheck);
        var context = new TestContext(new RegisterState());
        var plan = ProfilePreparationPlan.Create(root);

        foreach (var node in plan.Nodes)
        {
            var graph = compiler.Compile(root, node, singleNode: true);
            Assert.Equal(ExecutionStatus.Completed, await new TestExecutor().ExecuteAsync(graph.StartNode, context, default));
        }

        var entry = Assert.Single(context.ReportEntries);
        Assert.Equal("Настройка", entry.Name);
        Assert.True(entry.IsSuccess);
    }

    [Fact]
    public async Task FailedPreparation_DoesNotFollowDeviceErrorBranch()
    {
        using var bus = new ModbusService();
        using var compiler = new GraphCompiler(bus, NullLogger.Instance);
        var prep = new CheckVariableEqualityNodeViewModel { VariableName = "missing", RunOnlyOnFirstRun = true };
        var next = new DelayNodeViewModel();
        var root = Chain(prep, next);
        root.Connections.Add(new(prep.FalseOut, next.Input[0]));
        var context = new TestContext(new RegisterState());

        var node = Assert.Single(ProfilePreparationPlan.Create(root).Nodes);
        var graph = compiler.Compile(root, node, singleNode: true);

        Assert.Equal(ExecutionStatus.Failed, await new TestExecutor().ExecuteAsync(graph.StartNode, context, default));
        Assert.NotNull(context.Failure);
    }

    private static DelayNodeViewModel PreparationDelay() => new() { Milliseconds = 0, RunOnlyOnFirstRun = true };

    private static GraphWorkspaceViewModel Chain(params NodeViewModel[] operations)
    {
        var graph = new GraphWorkspaceViewModel();
        NodeViewModel previous = new StartNodeViewModel();
        graph.Nodes.Add(previous);
        foreach (var node in operations.Append<NodeViewModel>(new EndNodeViewModel()))
        {
            graph.Nodes.Add(node);
            graph.Connections.Add(new(previous.Output[0], node.Input[0]));
            previous = node;
        }
        return graph;
    }

    private static void CopyGraph(GraphWorkspaceViewModel source, GraphWorkspaceViewModel target)
    {
        target.Clear();
        foreach (var node in source.Nodes) target.Nodes.Add(node);
        foreach (var edge in source.Connections) target.Connections.Add(edge);
    }
}
