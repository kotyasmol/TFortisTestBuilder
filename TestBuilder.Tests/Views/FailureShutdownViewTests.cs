using Avalonia.Headless;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class FailureShutdownViewTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task PreparationFailureOrHandledCriticalFailure_StillRunsCleanup(bool duringPreparation, bool cleanupFails)
    {
        var marker = new StandInspectionStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".inspection"));
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        try { await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus), true, null, marker);
            vm.NewProfileCommand.Execute(null);
            var cleanup = MakeSubtest("Завершение", cleanupFails);
            cleanup.RunOnFailure = true;
            vm.RootGraph.Nodes.Add(cleanup);
            var start = new StartNodeViewModel();
            var fail = MakeSubtest(duringPreparation ? "Настройка" : "Основная проверка", true);
            fail.RunOnlyOnFirstRun = duringPreparation;
            var end = new EndNodeViewModel();
            vm.RootGraph.Nodes.Add(start); vm.RootGraph.Nodes.Add(fail); vm.RootGraph.Nodes.Add(end);
            vm.RootGraph.Connections.Add(new ConnectionViewModel(start.Output[0], fail.Input[0]));
            vm.RootGraph.Connections.Add(new ConnectionViewModel(fail.Output.Single(c => c.Title == "Success"), end.Input[0]));
            // An explicit error route still needs cleanup when a subtest sets a critical failure.
            if (!duringPreparation)
                vm.RootGraph.Connections.Add(new ConnectionViewModel(fail.Output.Single(c => c.Title == "Error"), end.Input[0]));
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.Contains(vm.TestingLogger.Entries, e => e.Message.Contains("Cleanup 'Завершение' начат"));
            Assert.False(vm.IsTestRunning); Assert.False(vm.Station.IsSuccessful);
            Assert.Equal(cleanupFails, vm.NeedsStandInspection);
            if (cleanupFails)
            {
                using var restarted = new TestViewModel(bus, new SlaveManager(bus), true, null, marker);
                Assert.True(restarted.NeedsStandInspection);
                Assert.False(restarted.CanStartRun);
                Assert.False(vm.CanStartRun);
                Assert.Equal("Проверьте состояние стенда", vm.Station.Headline);
                vm.IsOperatorView = true; vm.ConfirmStandInspectionCommand.Execute(null); Assert.True(vm.NeedsStandInspection);
                vm.IsOperatorView = false; vm.IsConnected = true; vm.ConfirmStandInspectionCommand.Execute(null); Assert.True(vm.NeedsStandInspection);
                vm.IsConnected = false; vm.ConfirmStandInspectionCommand.Execute(null); Assert.False(vm.NeedsStandInspection);
                Assert.False(marker.IsRequired());
            }
            return true;
        }, CancellationToken.None); }
        finally { marker.Clear(); }
    }

    private static SubtestNodeViewModel MakeSubtest(string name, bool fail)
    {
        var sub = new SubtestNodeViewModel { Name = name, StopOnError = true };
        var start = new StartNodeViewModel();
        NodeViewModel action = fail ? new CheckVariableEqualityNodeViewModel { VariableName = "missing" } : new EndNodeViewModel();
        sub.BodyGraph.Nodes.Clear(); sub.BodyGraph.Connections.Clear();
        sub.BodyGraph.Nodes.Add(start); sub.BodyGraph.Nodes.Add(action);
        sub.BodyGraph.Connections.Add(new ConnectionViewModel(start.Output[0], action.Input[0]));
        return sub;
    }
}
