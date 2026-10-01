using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.StepTests;

public class StationDashboardTests
{
    [Fact]
    public void ProfileGroups_SelectConfiguration_FilterAndRestoreSelection()
    {
        using var modbus = new ModbusService();
        using var vm = new TestViewModel(modbus, new SlaveManager(modbus)) { IsOperatorView = true };
        var group = vm.StationProfileGroups.Single(g => g.ModelName == "PSW+UPS-Box 8x2Pro");
        Assert.Equal("Полная проверка", group.Profiles[0].Profile.ConfigurationName);
        Assert.All(group.Profiles, p => Assert.Equal(group.ModelName, p.Profile.DeviceModel));
        var configurationCount = group.Profiles.Count;
        var printing = group.Profiles.Single(p => p.Profile.ConfigurationName == "Ручная печать этикеток");
        printing.SelectCommand.Execute(null);
        var rootNode = vm.RootGraph.Nodes.First();
        Assert.Same(printing.Profile, vm.SelectedProfile);
        Assert.True(printing.IsSelected);
        Assert.Equal("PSW+UPS-Box 8x2Pro", vm.StationProfileName);
        Assert.Contains("Ручная печать этикеток", vm.StationConfigurationCaption);
        group.ToggleExpandedCommand.Execute(null);
        vm.RefreshProfiles();
        Assert.False(group.IsExpanded);
        Assert.Same(rootNode, vm.RootGraph.Nodes.First());
        vm.StationProfileSearch = "8x2Pro";
        Assert.Same(group, Assert.Single(vm.StationProfileGroups));
        Assert.Equal(configurationCount, group.Profiles.Count);
        Assert.True(group.IsExpanded);
        vm.StationProfileSearch = "печать";
        Assert.Same(group, Assert.Single(vm.StationProfileGroups));
        Assert.True(Assert.Single(group.Profiles).IsSelected);
        vm.StationProfileSearch = "нет совпадений";
        Assert.Empty(vm.StationProfileGroups);
        Assert.Same(printing.Profile, vm.SelectedProfile);
        vm.StationProfileSearch = "";
        var selected = Assert.Single(vm.StationProfileGroups.SelectMany(g => g.Profiles).Where(p => p.IsSelected));
        Assert.Same(printing.Profile, selected.Profile);
        Assert.Same(rootNode, vm.RootGraph.Nodes.First());
    }

    [Fact]
    public void ProfileGroups_CannotChangeConfigurationDuringRun()
    {
        using var modbus = new ModbusService();
        using var vm = new TestViewModel(modbus, new SlaveManager(modbus)) { IsOperatorView = true };
        var group = vm.StationProfileGroups.Single(g => g.ModelName == "PSW+UPS-Box 8x2Pro");
        group.Profiles[0].SelectCommand.Execute(null);
        var profile = vm.SelectedProfile;
        var rootNode = vm.RootGraph.Nodes.First();
        vm.IsTestRunning = true;
        var printing = group.Profiles.Single(p => p.Profile.ConfigurationName == "Ручная печать этикеток");
        printing.SelectCommand.Execute(null);
        Assert.Same(profile, vm.SelectedProfile);
        Assert.Same(rootNode, vm.RootGraph.Nodes.First());
        Assert.True(group.Profiles[0].IsSelected);
        Assert.False(printing.IsSelected);
    }

    [Fact]
    public async Task ConnectionCommands_DoNotDisconnectDuringRunOrPauseOrCleanup()
    {
        using var modbus = new ModbusService();
        using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        Assert.True(vm.ConnectStationCommand.CanExecute(null));
        Assert.False(vm.DisconnectStationCommand.CanExecute(null));
        vm.IsConnected = true;
        Assert.False(vm.ConnectStationCommand.CanExecute(null));
        Assert.True(vm.DisconnectStationCommand.CanExecute(null));
        vm.IsTestRunning = true;
        vm.PauseTestCommand.Execute(null);
        Assert.True(vm.IsTestPaused);
        Assert.False(vm.DisconnectStationCommand.CanExecute(null));
        await vm.DisconnectStationCommand.ExecuteAsync(null);
        Assert.True(vm.IsConnected);
        vm.StopTestCommand.Execute(null);
        Assert.False(vm.IsTestPaused);
        Assert.False(vm.PauseTestCommand.CanExecute(null));
        Assert.False(vm.ResumeTestCommand.CanExecute(null));
        Assert.False(vm.DisconnectStationCommand.CanExecute(null));
        vm.PauseTestCommand.Execute(null);
        Assert.False(vm.IsTestPaused);
        vm.IsTestRunning = false;
        vm.IsStopping = false;
        Assert.True(vm.DisconnectStationCommand.CanExecute(null));
    }

    [Fact]
    public async Task PausedOperatorPrompt_WaitsForResumeAndStillAllowsCancellation()
    {
        using var modbus = new ModbusService();
        using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        using var cancellation = new CancellationTokenSource();
        vm.IsTestRunning = true;
        var prompt = vm.Station.RequestActionAsync("Подключите кабель", cancellation.Token);
        vm.PauseTestCommand.Execute(null);
        Assert.True(vm.IsPauseEffective);
        Assert.False(vm.Station.ConfirmActionCommand.CanExecute(null));
        vm.Station.ConfirmActionCommand.Execute(null);
        Assert.False(prompt.IsCompleted);
        vm.ResumeTestCommand.Execute(null);
        Assert.True(vm.Station.ConfirmActionCommand.CanExecute(null));
        Assert.False(vm.IsPauseEffective);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prompt);
    }

    [Fact]
    public void Search_PreservesLoadedGraphAndFindsFriendlyModelName()
    {
        using var modbus = new ModbusService();
        using var vm = new TestViewModel(modbus, new SlaveManager(modbus)) { IsOperatorView = true };
        var profile = vm.StationProfiles.First(p => p.StationName == "PSW-2G6F+");
        vm.SelectedProfile = profile;
        var node = vm.RootGraph.Nodes.First();
        var stages = vm.Station.Stages.ToArray();
        vm.StationProfileSearch = "несуществующее имя";
        Assert.Empty(vm.StationProfiles);
        Assert.Same(profile, vm.SelectedProfile);
        Assert.Null(vm.SelectedStationProfile);
        vm.SelectedStationProfile = null;
        Assert.Same(profile, vm.SelectedProfile);
        vm.StationProfileSearch = "PSW-2G6F+";
        Assert.Same(profile, Assert.Single(vm.StationProfiles));
        Assert.Same(profile, vm.SelectedStationProfile);
        vm.StationProfileSearch = "";
        Assert.Same(profile, vm.SelectedStationProfile);
        vm.RefreshProfiles();
        Assert.Same(profile, vm.SelectedProfile);
        Assert.Same(node, vm.RootGraph.Nodes.First());
        Assert.Equal(stages, vm.Station.Stages);
        Assert.Equal(Enumerable.Range(1, 11), vm.Station.Stages.Select(s => s.Number));
        Assert.All(vm.Station.Stages, s => Assert.True(s.HasDetails));
    }

    [Fact]
    public void StageDetails_FollowGraphConnectionsWithoutBoundaryNodes()
    {
        var parent = new SubtestNodeViewModel { Name = "04. Этап" };
        parent.BodyGraph.Nodes.Clear();
        parent.BodyGraph.Connections.Clear();
        var start = new StartNodeViewModel();
        var first = new SubtestNodeViewModel { Name = "Первый" };
        var second = new SubtestNodeViewModel { Name = "Второй" };
        foreach (var node in new NodeViewModel[] { second, start, first }) parent.BodyGraph.Nodes.Add(node);
        parent.BodyGraph.Connections.Add(new ConnectionViewModel(start.Output[0], first.In));
        parent.BodyGraph.Connections.Add(new ConnectionViewModel(first.SuccessOut, second.In));
        var stage = new StationStageViewModel(parent, parent.Name);
        Assert.Equal("Этап", stage.Caption);
        Assert.Equal(new[] { "Первый", "Второй" }, stage.Details);
    }

    [Fact]
    public async Task StopWhileLeavingPause_DoesNotStartNextOperation()
    {
        using var cancellation = new CancellationTokenSource();
        var observer = new CapturingObserver();
        var context = new TestContext(new RegisterState())
        {
            ExecutionObserver = observer,
            WaitIfPausedAsync = _ => { cancellation.Cancel(); return Task.CompletedTask; }
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TestExecutor().ExecuteAsync(
            new TestNode(new ThrowingStep()), context, cancellation.Token));
        Assert.Equal(0, observer.Finished);
    }

    [Fact]
    public void Plan_FollowsConnectionsAndHidesUnconnectedCleanup()
    {
        var (graph, first, second, cleanup) = CreateGraph();
        var dashboard = new StationDashboardViewModel();
        dashboard.Prepare(graph);
        Assert.Equal(new[] { first.Name, second.Name }, dashboard.Stages.Select(s => s.Name));
        dashboard.BeginCleanup(cleanup);
        Assert.Equal(cleanup.Name, dashboard.Stages.Last().Name);
    }

    [Fact]
    public void NestedFailureFollowedByRecovery_DoesNotPrematurelyFinishOrFailParent()
    {
        var (graph, first, _, _) = CreateGraph();
        var inner = new DelayNodeViewModel();
        first.BodyGraph.Nodes.Add(inner);
        var dashboard = new StationDashboardViewModel();
        var context = new TestContext(new RegisterState());
        dashboard.BeginRun(graph);
        dashboard.NodeStarted(first, context);
        dashboard.NodeStarted(inner, context);
        dashboard.NodeCompleted(inner, StepResult.False, context);
        Assert.Equal(StationStageState.Running, dashboard.Stages[0].State);
        dashboard.NodeCompleted(first, StepResult.True, context);
        Assert.Equal(StationStageState.Completed, dashboard.Stages[0].State);
    }

    [Fact]
    public async Task StopDuringOperatorPrompt_UnblocksAndClearsPrompt()
    {
        var dashboard = new StationDashboardViewModel();
        using var cancellation = new CancellationTokenSource();
        var task = dashboard.RequestActionAsync("Проверьте кабель", cancellation.Token);
        Assert.True(dashboard.IsAwaitingAction);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.False(dashboard.IsAwaitingAction);
        Assert.False(dashboard.ConfirmActionCommand.CanExecute(null));
        Assert.Empty(dashboard.OperatorMessage);
    }

    [Fact]
    public async Task ConsecutivePrompts_RequireSeparateAnswers()
    {
        var dashboard = new StationDashboardViewModel();
        var first = dashboard.RequestActionAsync("Первое действие", CancellationToken.None);
        dashboard.ConfirmActionCommand.Execute(null);
        Assert.True(await first);
        var second = dashboard.RequestActionAsync("Второе действие", CancellationToken.None);
        Assert.False(second.IsCompleted);
        dashboard.DeclineActionCommand.Execute(null);
        Assert.False(await second);
    }

    [Fact]
    public void CancelledRunAndSuccessfulCleanup_DoNotShowSuccessfulTest()
    {
        var (graph, first, _, cleanup) = CreateGraph();
        var dashboard = new StationDashboardViewModel();
        var context = new TestContext(new RegisterState());
        dashboard.BeginRun(graph);
        dashboard.NodeStarted(first, context);
        dashboard.RequestStop();
        dashboard.BeginCleanup(cleanup);
        dashboard.FinishCleanup(cleanup, true);
        dashboard.CompleteRun(ExecutionStatus.Cancelled, context);
        Assert.Equal(StationStageState.Interrupted, dashboard.Stages[0].State);
        Assert.Equal("Проверка остановлена", dashboard.Headline);
        Assert.False(dashboard.IsSuccessful);
    }

    [Fact]
    public void FailedCleanup_OverridesOrdinaryCancellationMessage()
    {
        var (graph, _, _, cleanup) = CreateGraph();
        var dashboard = new StationDashboardViewModel();
        dashboard.BeginRun(graph);
        dashboard.BeginCleanup(cleanup);
        dashboard.FinishCleanup(cleanup, false);
        dashboard.CompleteRun(ExecutionStatus.Cancelled, new TestContext(new RegisterState()));
        Assert.Equal("Проверьте состояние стенда", dashboard.Headline);
        Assert.True(dashboard.NeedsAttention);
    }

    [Fact]
    public void FailedDelivery_IsDistinguishedFromFailedDevice()
    {
        var dashboard = new StationDashboardViewModel();
        var context = new TestContext(new RegisterState());
        context.SetVariable("BuildReport.DevicePassed", true);
        context.SetVariable("ReportDelivery.Status", "Failed");
        dashboard.CompleteRun(ExecutionStatus.Completed, context);
        Assert.Equal("Проверки пройдены · отчёт не отправлен", dashboard.Headline);
        Assert.False(dashboard.IsSuccessful);
        context.SetVariable("BuildReport.DevicePassed", false);
        context.SetVariable("ReportDelivery.Status", "Sent");
        dashboard.CompleteRun(ExecutionStatus.Completed, context);
        Assert.Equal("Проверки не пройдены", dashboard.Headline);
    }

    [Fact]
    public void OperatorCommandsAndDirectEditingCalls_CannotChangeGraph()
    {
        using var modbus = new ModbusService();
        using var vm = new TestViewModel(modbus, new SlaveManager(modbus), allowEditing: false);
        var node = new DelayNodeViewModel();
        vm.RootGraph.Nodes.Add(node);
        vm.RootGraph.SelectedNodes.Add(node);
        Assert.False(vm.NewProfileCommand.CanExecute(null));
        Assert.False(vm.SaveGraphCommand.CanExecute(null));
        Assert.False(vm.ImportProfilesCommand.CanExecute(null));
        vm.NewProfileCommand.Execute(null);
        vm.ClearGraph();
        vm.DeleteSelectedNodes();
        vm.AddNodeAtLocation("Задержка", default);
        Assert.Same(node, Assert.Single(vm.RootGraph.Nodes));
    }

    [Fact]
    public void RunningTest_LocksProfileChangesAndEngineeringMutations()
    {
        using var modbus = new ModbusService();
        using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        vm.RootGraph.Nodes.Add(new DelayNodeViewModel());
        vm.IsTestRunning = true;
        vm.SelectedProfile = new TestBuilder.Services.GraphProfile("missing.json", "Other");
        vm.ClearGraph();
        Assert.Null(vm.SelectedProfile);
        Assert.Single(vm.RootGraph.Nodes);
        Assert.False(vm.CanEditGraph);
        Assert.False(vm.ToggleConnectionCommand.CanExecute(null));
    }

    [Fact]
    public async Task Executor_ExceptionDoesNotEmitCompletedNotification()
    {
        var observer = new CapturingObserver();
        var context = new TestContext(new RegisterState()) { ExecutionObserver = observer };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new TestExecutor().ExecuteAsync(
            new TestNode(new ThrowingStep()), context, CancellationToken.None));
        Assert.Equal(0, observer.Completed);
        Assert.Equal(1, observer.Finished);
    }

    [Fact]
    public void InvalidProfile_CannotRunPreviousGraphOrRetainGreenResult()
    {
        using var modbus = new ModbusService();
        using var vm = new TestViewModel(modbus, new SlaveManager(modbus), allowEditing: false) { IsOperatorView = true };
        vm.Station.CompleteRun(ExecutionStatus.Completed, new TestContext(new RegisterState()));
        vm.SelectedProfile = new TestBuilder.Services.GraphProfile(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), "Broken");
        Assert.False(vm.CanStartRun);
        Assert.False(vm.Station.IsSuccessful);
        Assert.Equal("Не удалось загрузить программу", vm.Station.Headline);
    }

    [Fact]
    public void DemoProfile_LoadsInOperatorModeAndContainsOnlyPromptsAndDelays()
    {
        using var modbus = new ModbusService();
        using var vm = new TestViewModel(modbus, new SlaveManager(modbus), allowEditing: false) { IsOperatorView = true };
        var file = Path.Combine(TestBuilder.Services.ProfileDirectoryLocator.Resolve(), "station_dashboard_demo.json");
        vm.SelectedProfile = new TestBuilder.Services.GraphProfile(file, "Demo");
        Assert.True(vm.CanStartRun);
        Assert.Equal(3, vm.Station.Stages.Count);
        Assert.False(TestBuilder.Services.Graph.GraphConnectionRequirements.RequiresStandConnection(vm.RootGraph));
        foreach (var subtest in vm.RootGraph.Nodes.OfType<SubtestNodeViewModel>())
            Assert.All(subtest.BodyGraph.Nodes, node => Assert.True(node is StartNodeViewModel or EndNodeViewModel or
                DelayNodeViewModel or OperatorActionNodeViewModel));
    }

    private sealed class ThrowingStep : ITestStep
    {
        public Task<StepResult> ExecuteAsync(TestContext context, CancellationToken token) => throw new InvalidOperationException();
    }
    private sealed class CapturingObserver : IExecutionObserver
    {
        public int Completed, Finished;
        public Task NodeStartedAsync(TestNode n, TestContext c, CancellationToken t) => Task.CompletedTask;
        public Task NodeFailedAsync(TestNode n, TestContext c, CancellationToken t) => Task.CompletedTask;
        public Task NodeFinishedAsync(TestNode n, TestContext c, CancellationToken t) { Finished++; return Task.CompletedTask; }
        public Task NodeCompletedAsync(TestNode n, StepResult r, TestContext c, CancellationToken t) { Completed++; return Task.CompletedTask; }
    }

    private static (GraphWorkspaceViewModel, SubtestNodeViewModel, SubtestNodeViewModel, SubtestNodeViewModel) CreateGraph()
    {
        var start = new StartNodeViewModel();
        var first = new SubtestNodeViewModel { Name = "Первый этап" };
        var second = new SubtestNodeViewModel { Name = "Второй этап" };
        var cleanup = new SubtestNodeViewModel { Name = "Завершение", RunOnFailure = true };
        var graph = new GraphWorkspaceViewModel();
        foreach (var node in new NodeViewModel[] { second, cleanup, start, first }) graph.Nodes.Add(node);
        graph.Connections.Add(new ConnectionViewModel(start.Output[0], first.In));
        graph.Connections.Add(new ConnectionViewModel(first.SuccessOut, second.In));
        return (graph, first, second, cleanup);
    }
}
