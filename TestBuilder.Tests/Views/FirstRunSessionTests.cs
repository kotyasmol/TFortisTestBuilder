using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class FirstRunSessionTests
{
    [Fact]
    public async Task SuccessfulSession_SkipsPreparationAfterReload_ManualRunAndRestartExecuteIt()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "first-run-" + Guid.NewGuid().ToString("N") + ".json");
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            try
            {
                var prep = new DelayNodeViewModel { Milliseconds = 1, RunOnlyOnFirstRun = true };
                var everyTime = new DelayNodeViewModel { Milliseconds = 1 };
                Chain(vm.RootGraph, prep, everyTime);
                await vm.SaveGraphToPathAsync(path);
                await vm.RunGraphCommand.ExecuteAsync(null);
                Assert.True(vm.Station.IsSuccessful); Assert.True(prep.HasExecutionSucceeded);
                await vm.RunGraphCommand.ExecuteAsync(null);
                Assert.True(vm.Station.IsSuccessful); Assert.False(prep.HasExecutionSucceeded); Assert.True(everyTime.HasExecutionSucceeded);
                Assert.Contains(vm.Station.Stages, s => s.Source == prep && s.State == StationStageState.Skipped);
                Assert.Contains(vm.TestingLogger.Entries, e => e.Message.Contains("[ПРОПУСК]"));
                // A save date / movement / color is not a new algorithm.
                prep.Location = new Point(900, 700); prep.NodeColor = "red";
                await vm.SaveGraphToPathAsync(path);
                vm.NewProfileCommand.Execute(null); vm.SelectedProfile = GraphSerializer.ReadProfile(path);
                prep = vm.Nodes.OfType<DelayNodeViewModel>().First();
                await vm.RunGraphCommand.ExecuteAsync(null); Assert.False(prep.HasExecutionSucceeded);
                vm.ConfigureNodeRun(prep, true);
                await vm.RunGraphCommand.ExecuteAsync(null); Assert.True(prep.HasExecutionSucceeded);
                await vm.RunGraphCommand.ExecuteAsync(null); Assert.False(prep.HasExecutionSucceeded);
                prep.Milliseconds = 2;
                await vm.RunGraphCommand.ExecuteAsync(null); Assert.True(prep.HasExecutionSucceeded);
                using var restarted = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
                restarted.SelectedProfile = GraphSerializer.ReadProfile(path);
                await restarted.RunGraphCommand.ExecuteAsync(null);
                Assert.True(restarted.Nodes.OfType<DelayNodeViewModel>().First().HasExecutionSucceeded);
            }
            finally { File.Delete(path); }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailedOrCancelledRun_DoesNotConsumePreparation(bool cancel)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            var prep = new DelayNodeViewModel { Milliseconds = 1, RunOnlyOnFirstRun = true };
            NodeViewModel check = cancel ? new DelayNodeViewModel { Milliseconds = 100 } : new CheckVariableEqualityNodeViewModel { VariableName = "missing" };
            Chain(vm.RootGraph, prep, check);
            for (var i = 0; i < 2; i++)
            {
                var run = vm.RunGraphCommand.ExecuteAsync(null);
                if (cancel) { await Task.Delay(30); vm.StopTestCommand.Execute(null); }
                await run;
                Assert.True(prep.HasExecutionSucceeded);
                Assert.False(vm.Station.IsSuccessful);
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EmergencyCleanupRunsEvenAfterPreparationWasConsumed_AndNextRunPreparesAgain()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            var prep = new DelayNodeViewModel { Milliseconds = 1, RunOnlyOnFirstRun = true };
            Chain(vm.RootGraph, prep, new DelayNodeViewModel { Milliseconds = 100 });
            var cleanup = new SubtestNodeViewModel { RunOnFailure = true, RunOnlyOnFirstRun = true };
            var action = new DelayNodeViewModel { Milliseconds = 1, RunOnlyOnFirstRun = true };
            Chain(cleanup.BodyGraph, action); vm.Nodes.Add(cleanup);
            await vm.RunGraphCommand.ExecuteAsync(null); Assert.True(prep.HasExecutionSucceeded);
            var second = vm.RunGraphCommand.ExecuteAsync(null);
            await Task.Delay(30); vm.StopTestCommand.Execute(null); await second;
            Assert.False(prep.HasExecutionSucceeded); Assert.True(action.HasExecutionSucceeded);
            await vm.RunGraphCommand.ExecuteAsync(null); Assert.True(prep.HasExecutionSucceeded);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NodeSettingsToggle_IsVisibleUndoable_AndLockedDuringRun()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
            var prep = new DelayNodeViewModel { Milliseconds = 100 };
            Chain(vm.RootGraph, prep);
            using var view = new GraphEditorView { DataContext = vm };
            var window = new Window { Width = 1400, Height = 850, Content = view };
            try
            {
                window.Show(); Layout();
                var options = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NodeOptionsTrigger" && b.DataContext == prep);
                Assert.True(options.IsEnabled);
                options.Flyout!.ShowAt(options); Layout();
                var popup = Assert.IsType<Flyout>(options.Flyout);
                var content = Assert.IsType<StackPanel>(popup.Content);
                var check = content.Children.OfType<CheckBox>().Single();
                Assert.True(check.IsVisible); Assert.True(check.IsEffectivelyEnabled);
                check.IsChecked = true; Assert.True(prep.RunOnlyOnFirstRun);
                vm.UndoCommand.Execute(null); Assert.False(check.IsChecked);
                vm.RedoCommand.Execute(null); Assert.True(check.IsChecked);
                var run = vm.RunGraphCommand.ExecuteAsync(null); Layout();
                Assert.False(options.IsEnabled); Assert.False(check.IsEffectivelyEnabled);
                await run; Layout(); Assert.True(options.IsEnabled);
                popup.Hide();
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Только первый запуск" && t.IsVisible && t.DataContext == prep);
            }
            finally { window.Close(); }
            return true;
            void Layout() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); }
        }, CancellationToken.None);
    }

    private static void Chain(TestBuilder.ViewModels.Graphs.GraphWorkspaceViewModel graph, params NodeViewModel[] steps)
    {
        graph.Clear();
        var start = new StartNodeViewModel(); var end = new EndNodeViewModel();
        graph.Nodes.Add(start); NodeViewModel previous = start;
        foreach (var step in steps.Append<NodeViewModel>(end))
        {
            graph.Nodes.Add(step); graph.Connections.Add(new(previous.Output[0], step.Input[0])); previous = step;
        }
    }
}
