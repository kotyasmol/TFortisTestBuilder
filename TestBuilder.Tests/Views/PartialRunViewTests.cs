using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nodify;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class PartialRunViewTests
{
    [Fact]
    public async Task RightClick_SelectsTemporaryEntry_ResetAndDeleteClearIt_WithoutSavingOrHistory()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var modbus = new ModbusService();
            using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
            vm.NewProfileCommand.Execute(null);
            vm.AddNodeAtLocation("Задержка", new Point(100, 100));
            var node = vm.Nodes.OfType<DelayNodeViewModel>().Single();
            var json = GraphSerializer.Serialize(vm, vm.CurrentProfileName);
            using var view = new GraphEditorView { DataContext = vm };
            var window = new Window { Width = 1100, Height = 800, Content = view };
            try
            {
                window.Show(); Layout(window);
                var container = view.GetVisualDescendants().OfType<ItemContainer>().Single(c => ReferenceEquals(c.DataContext, node));
                var point = container.TranslatePoint(new Point(15, 15), window)!.Value;
                window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right); Layout(window);
                Assert.NotNull(view.NodeRunMenu);
                Assert.True(view.NodeRunMenu!.IsOpen);
                var menuItem = view.NodeRunMenu.Items.OfType<MenuItem>().Last();
                menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                view.NodeRunMenu.Close(); Layout(window);
                Assert.True(vm.HasPartialRun);
                Assert.Equal("СТАРТ ЗАПУСКА", node.RunStartLabel);
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "СТАРТ ЗАПУСКА" && t.IsVisible);
                Assert.Equal(json, GraphSerializer.Serialize(vm, vm.CurrentProfileName));
                var capture = Environment.GetEnvironmentVariable("TESTBUILDER_LAYOUT_CAPTURES");
                if (!string.IsNullOrWhiteSpace(capture)) { Directory.CreateDirectory(capture); window.CaptureRenderedFrame()!.Save(Path.Combine(capture, "partial-run.png")); }
                vm.ClearPartialRunCommand.Execute(null);
                Assert.False(node.IsRunStart);
                vm.ConfigureNodeRun(node, true);
                vm.UndoCommand.Execute(null);
                Assert.DoesNotContain(node, vm.Nodes); // selection was not added to undo history
                Assert.False(vm.HasPartialRun);
                Assert.False(node.IsRunStart);
                vm.RedoCommand.Execute(null);
                vm.ConfigureNodeRun(node, false);
                vm.NewProfileCommand.Execute(null);
                Assert.False(vm.HasPartialRun);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PartialRun_UsesSlaveContext_ResetsAfterRun_AndCannotBeConfiguredByOperator()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(async () =>
        {
            using var modbus = new ModbusService();
            using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
            vm.NewProfileCommand.Execute(null);
            var node = new CheckVariableEqualityNodeViewModel { VariableName = "slaveId", ExpectedValue = "7" };
            var skippedModbus = new ModbusWriteNodeViewModel();
            vm.Nodes.Add(node); vm.Nodes.Add(skippedModbus);
            vm.ConfigureNodeRun(node, true); vm.RunSlaveId = 7;
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.True(node.HasExecutionSucceeded);
            Assert.False(skippedModbus.HasExecutionSucceeded);
            Assert.False(vm.HasPartialRun);
            Assert.Null(vm.RunSlaveId);
            Assert.Contains("Частичный запуск", vm.StatusMessage);
            Assert.False(vm.Station.IsSuccessful);
            vm.ConfigureNodeRun(node, true);
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.True(node.HasExecutionError); // the previous slaveId must not leak into another run
            vm.ConfigureNodeRun(node, true);
            vm.IsOperatorView = true;
            Assert.False(vm.HasPartialRun);
            Assert.False(vm.CanConfigureNodeRun(node));
            vm.ConfigureNodeRun(node, true);
            Assert.False(vm.HasPartialRun);
            vm.IsOperatorView = false;
            vm.IsTestRunning = true;
            Assert.False(vm.CanConfigureNodeRun(node));
            vm.IsTestRunning = false;
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FailedPartialRun_StillExecutesFailureCleanup()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(async () =>
        {
            using var modbus = new ModbusService();
            using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
            vm.NewProfileCommand.Execute(null);
            var node = new CheckVariableEqualityNodeViewModel { VariableName = "missing" };
            var cleanup = new SubtestNodeViewModel { Name = "Cleanup", RunOnFailure = true };
            var bodyStart = cleanup.BodyGraph.Nodes.OfType<StartNodeViewModel>().Single();
            var bodyEnd = cleanup.BodyGraph.Nodes.OfType<EndNodeViewModel>().Single();
            cleanup.BodyGraph.Connections.Add(new(bodyStart.Output[0], bodyEnd.Input[0]));
            vm.Nodes.Add(node); vm.Nodes.Add(cleanup);
            vm.ConfigureNodeRun(node, true);
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.True(node.HasExecutionError);
            Assert.True(bodyEnd.HasExecutionSucceeded);
            Assert.False(vm.HasPartialRun);
            Assert.False(vm.IsTestRunning);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NestedSelection_StaysInsideItsWorkspace_AndLoadingProfileClearsSelection()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(async () =>
        {
            using var modbus = new ModbusService();
            using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
            vm.NewProfileCommand.Execute(null);
            var subtest = new SubtestNodeViewModel();
            var rootAfter = new DelayNodeViewModel { Milliseconds = 0 };
            vm.Nodes.Add(subtest); vm.Nodes.Add(rootAfter);
            vm.Connections.Add(new(subtest.SuccessOut, rootAfter.In));
            vm.OpenCompositeNodeBodyCommand.Execute(subtest);
            var node = subtest.BodyGraph.Nodes.OfType<EndNodeViewModel>().Single();
            vm.ConfigureNodeRun(node, false);
            vm.GoBackGraphCommand.Execute(null);
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.Same(subtest.BodyGraph, vm.CurrentGraph);
            Assert.True(node.HasExecutionSucceeded);
            Assert.False(rootAfter.HasExecutionSucceeded);
            vm.ConfigureNodeRun(node, true);
            var path = Path.Combine(Path.GetTempPath(), $"partial-profile-{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(path, "{\"name\":\"Other\",\"nodes\":[],\"connections\":[]}");
                vm.SelectedProfile = GraphSerializer.ReadProfile(path);
                Assert.False(vm.HasPartialRun);
                Assert.False(node.IsRunStart);
            }
            finally { File.Delete(path); }
            return true;
        }, CancellationToken.None);
    }

    private static void Layout(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}
