using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.StepTests;

public class ParallelExecutionUiTests
{
    [Fact]
    public void Progress_InterleavedSubtestsKeepTheirOwnScopeWhenFirstBranchFinishes()
    {
        var state = new ExecutionUiState();
        var parent = Guid.NewGuid();
        var firstScope = Guid.NewGuid();
        var secondScope = Guid.NewGuid();
        var first = new SubtestNodeViewModel { Name = "Загрузка" };
        var second = new SubtestNodeViewModel { Name = "Нагреватели" };
        var firstStep = new DelayNodeViewModel();
        var secondStep = new DelayNodeViewModel();
        var finalStep = new DelayNodeViewModel();
        first.BodyGraph.Nodes.Add(firstStep);
        second.BodyGraph.Nodes.Add(secondStep);
        second.BodyGraph.Nodes.Add(finalStep);

        state.NodeStarted(first, firstScope, parent);
        state.NodeStarted(second, secondScope, parent);
        state.NodeStarted(firstStep, firstScope, parent);
        state.NodeStarted(secondStep, secondScope, parent);
        Assert.Equal(1, first.CurrentStepIndex);
        Assert.Equal(1, second.CurrentStepIndex);
        Assert.True(firstStep.IsExecuting);
        Assert.True(secondStep.IsExecuting);

        state.NodeFinished(firstStep, firstScope);
        state.NodeFinished(first, firstScope);
        state.FinishScope(firstScope);
        state.NodeFinished(secondStep, secondScope);
        state.NodeStarted(finalStep, secondScope, parent);
        Assert.Equal(2, second.CurrentStepIndex);
        Assert.Contains("2/2", second.ProgressText);
        Assert.Empty(first.ProgressText);
        Assert.True(second.IsExecuting);
    }

    [Fact]
    public void Progress_BranchWithoutSubtestUpdatesItsParentScope()
    {
        var state = new ExecutionUiState();
        var parentScope = Guid.NewGuid();
        var parent = new SubtestNodeViewModel();
        var first = new DelayNodeViewModel();
        var second = new DelayNodeViewModel();
        parent.BodyGraph.Nodes.Add(first);
        parent.BodyGraph.Nodes.Add(second);
        state.NodeStarted(parent, parentScope, null);
        state.NodeStarted(first, Guid.NewGuid(), parentScope);
        state.NodeStarted(second, Guid.NewGuid(), parentScope);
        Assert.Equal(2, parent.CurrentStepIndex);
    }

    [Fact]
    public void Pause_BecomesEffectiveOnlyWhenAllAtomicOperationsFinish()
    {
        var state = new ExecutionUiState();
        var scope = Guid.NewGuid();
        var firstScope = Guid.NewGuid();
        var secondScope = Guid.NewGuid();
        var parent = new SubtestNodeViewModel();
        var first = new DelayNodeViewModel();
        var second = new DelayNodeViewModel();
        state.NodeStarted(parent, scope, null);
        state.NodeStarted(first, firstScope, scope);
        state.NodeStarted(second, secondScope, scope);
        Assert.False(state.CanPause(false));
        state.NodeFinished(first, firstScope);
        Assert.False(state.CanPause(false));
        state.NodeFinished(second, secondScope);
        Assert.True(state.CanPause(false));
        Assert.True(parent.IsExecuting);
    }

    [Fact]
    public void OperatorPrompt_DoesNotHideOtherRunningOperationsWhenPausing()
    {
        var state = new ExecutionUiState();
        var scope = Guid.NewGuid();
        var operation = new DelayNodeViewModel();
        state.NodeStarted(new OperatorActionNodeViewModel(), scope, null);
        Assert.True(state.CanPause(true));
        state.NodeStarted(operation, scope, null);
        Assert.False(state.CanPause(true));
        state.NodeFinished(operation, scope);
        Assert.True(state.CanPause(true));
    }

    [Fact]
    public void Dashboard_ShowsBothRunningStagesAndKeepsOnlyTheRemainingOneActive()
    {
        var (graph, first, second, join) = CreateGraph();
        var dashboard = new StationDashboardViewModel();
        var root = new TestContext(new RegisterState());
        var firstContext = root.CreateParallelBranch(CancellationToken.None);
        var secondContext = root.CreateParallelBranch(CancellationToken.None);
        dashboard.BeginRun(graph);
        Assert.Equal(new[] { first.Name, second.Name, join.Name }, dashboard.Stages.Select(s => s.Name));

        dashboard.NodeStarted(first, firstContext);
        dashboard.NodeStarted(second, secondContext);
        Assert.Equal(2, dashboard.Stages.Count(s => s.IsActive));
        Assert.Contains(first.Name, dashboard.CurrentStage);
        Assert.Contains(second.Name, dashboard.CurrentStage);
        dashboard.NodeCompleted(first, StepResult.True, firstContext);
        dashboard.NodeFinished(first, firstContext);
        Assert.Equal(first.Name, dashboard.Stages.Single(s => s.IsCompleted).Name);
        Assert.Equal(second.Name, dashboard.Stages.Single(s => s.IsActive).Name);
        Assert.Equal(second.Name, dashboard.CurrentStage);
        Assert.Equal(StationStageState.Pending, dashboard.Stages.Single(s => s.Name == join.Name).State);
    }

    [Fact]
    public void Pause_InsidePollingStepStillWaitsForOtherBranch()
    {
        var state = new ExecutionUiState();
        var firstScope = Guid.NewGuid();
        var secondScope = Guid.NewGuid();
        state.NodeStarted(new WaitVariableUntilNodeViewModel(), firstScope, null);
        state.NodeStarted(new DelayNodeViewModel(), secondScope, null);
        state.SetScopeWaiting(firstScope, true);
        Assert.False(state.CanPause(false));
        state.SetScopeWaiting(secondScope, true);
        Assert.True(state.CanPause(false));
        state.SetScopeWaiting(firstScope, false);
        Assert.False(state.CanPause(false));
    }

    [Fact]
    public void Dashboard_CancelledBranchIsInterruptedAndDoesNotTurnIntoProductFailure()
    {
        var (graph, first, second, _) = CreateGraph();
        var dashboard = new StationDashboardViewModel();
        var root = new TestContext(new RegisterState());
        var firstContext = root.CreateParallelBranch(CancellationToken.None);
        var secondContext = root.CreateParallelBranch(CancellationToken.None);
        dashboard.BeginRun(graph);
        dashboard.NodeStarted(first, firstContext);
        dashboard.NodeStarted(second, secondContext);
        dashboard.NodeCompleted(first, StepResult.True, firstContext);
        dashboard.NodeFinished(first, firstContext);
        dashboard.NodeFinished(second, secondContext);
        Assert.Equal(StationStageState.Interrupted, dashboard.Stages.Single(s => s.Source == second).State);
        dashboard.CompleteRun(ExecutionStatus.Cancelled, root);
        Assert.Equal("Проверка остановлена", dashboard.Headline);
        Assert.DoesNotContain(dashboard.Stages, s => s.State == StationStageState.Attention);
        Assert.False(dashboard.IsSuccessful);
    }

    [Fact]
    public void Connections_AllowFanOutButRejectDuplicatesAndReverseDirection()
    {
        using var modbus = new ModbusService();
        using var viewModel = new TestViewModel(modbus, new SlaveManager(modbus));
        var start = new StartNodeViewModel();
        var first = new SubtestNodeViewModel();
        var second = new SubtestNodeViewModel();
        foreach (var node in new NodeViewModel[] { start, first, second }) viewModel.RootGraph.Nodes.Add(node);
        viewModel.Connect(start.Output[0], first.In);
        viewModel.Connect(start.Output[0], first.In);
        viewModel.Connect(first.In, start.Output[0]);
        viewModel.Connect(start.Output[0], second.In);
        Assert.Equal(2, viewModel.RootGraph.Connections.Count);
    }

    [Fact]
    public void Dashboard_FailureAndCancelledSiblingKeepDifferentResults()
    {
        var (graph, first, second, _) = CreateGraph();
        var dashboard = new StationDashboardViewModel();
        var root = new TestContext(new RegisterState());
        var firstContext = root.CreateParallelBranch(CancellationToken.None);
        var secondContext = root.CreateParallelBranch(CancellationToken.None);
        dashboard.BeginRun(graph);
        dashboard.NodeStarted(first, firstContext);
        dashboard.NodeStarted(second, secondContext);
        dashboard.NodeFailed(first, firstContext);
        dashboard.NodeFinished(first, firstContext);
        dashboard.FinishScope(secondContext);
        dashboard.CompleteRun(ExecutionStatus.Failed, root);
        Assert.Equal(StationStageState.Attention, dashboard.Stages.Single(s => s.Source == first).State);
        Assert.Equal(StationStageState.Interrupted, dashboard.Stages.Single(s => s.Source == second).State);
        Assert.DoesNotContain(dashboard.Stages, s => s.IsActive || s.HasActiveOperations);
        Assert.Equal("Проверка не завершена", dashboard.Headline);
    }

    private static (GraphWorkspaceViewModel Graph, SubtestNodeViewModel First,
        SubtestNodeViewModel Second, SubtestNodeViewModel Join) CreateGraph()
    {
        var graph = new GraphWorkspaceViewModel();
        var start = new StartNodeViewModel();
        var first = new SubtestNodeViewModel { Name = "Загрузка" };
        var second = new SubtestNodeViewModel { Name = "Нагреватели" };
        var join = new SubtestNodeViewModel { Name = "Продолжение" };
        foreach (var node in new NodeViewModel[] { join, second, start, first }) graph.Nodes.Add(node);
        graph.Connections.Add(new ConnectionViewModel(start.Output[0], first.In));
        graph.Connections.Add(new ConnectionViewModel(start.Output[0], second.In));
        graph.Connections.Add(new ConnectionViewModel(first.SuccessOut, join.In));
        graph.Connections.Add(new ConnectionViewModel(second.SuccessOut, join.In));
        return (graph, first, second, join);
    }
}
