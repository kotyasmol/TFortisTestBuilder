using Avalonia.Headless;
using TestBuilder.Domain.Modbus;
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
    public async Task PreparationFailureOrHandledCriticalFailure_StillRunsCleanup(bool malformedMain, bool cleanupFails)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
            vm.NewProfileCommand.Execute(null);
            var cleanup = MakeSubtest("Завершение", cleanupFails);
            cleanup.RunOnFailure = true;
            vm.RootGraph.Nodes.Add(cleanup);
            if (!malformedMain)
            {
                var start = new StartNodeViewModel(); var fail = MakeSubtest("Основная проверка", true); var end = new EndNodeViewModel();
                vm.RootGraph.Nodes.Add(start); vm.RootGraph.Nodes.Add(fail); vm.RootGraph.Nodes.Add(end);
                vm.RootGraph.Connections.Add(new ConnectionViewModel(start.Output[0], fail.Input[0]));
                // A graph can deliberately route a failed subtest to End. Critical failure still needs cleanup.
                vm.RootGraph.Connections.Add(new ConnectionViewModel(fail.Output.Single(c => c.Title == "Error"), end.Input[0]));
            }
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.Contains(vm.TestingLogger.Entries, e => e.Message.Contains("Cleanup 'Завершение' начат"));
            Assert.False(vm.IsTestRunning); Assert.False(vm.Station.IsSuccessful);
            Assert.Equal(cleanupFails, vm.NeedsStandInspection);
            if (cleanupFails)
            {
                Assert.False(vm.CanStartRun);
                Assert.Equal("Проверьте состояние стенда", vm.Station.Headline);
                vm.IsOperatorView = true; vm.ConfirmStandInspectionCommand.Execute(null); Assert.True(vm.NeedsStandInspection);
                vm.IsOperatorView = false; vm.IsConnected = true; vm.ConfirmStandInspectionCommand.Execute(null); Assert.True(vm.NeedsStandInspection);
                vm.IsConnected = false; vm.ConfirmStandInspectionCommand.Execute(null); Assert.False(vm.NeedsStandInspection);
            }
            return true;
        }, CancellationToken.None);
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
