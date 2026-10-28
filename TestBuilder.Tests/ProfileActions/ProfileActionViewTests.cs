using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Models;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.Views;
using TestBuilder.Views.TestViews;
using TestBuilder.Tests.Views;

namespace TestBuilder.Tests.ProfileActions;

[Collection("Graph editor UI")]
public class ProfileActionViewTests
{
    [Fact]
    public async Task LeftPanel_CollapsesAndExpands_EngineerCanEdit_OperatorOnlyRunsSameButton()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            vm.RootGraph.QuickActions.Add(new() { Name = "Питание", Display = ProfileActionDisplay.Toggle });
            vm.RootGraph.QuickActions.Add(new() { Name = "Проверка", Display = ProfileActionDisplay.Progress });
            var bar = new ProfileActionBar { DataContext = vm };
            var workspace = new Border();
            Grid.SetColumn(workspace, 1);
            var host = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { bar, workspace } };
            var window = new Window { Width = 1050, Height = 500, Content = host };
            try
            {
                window.Show(); Layout(window);
                var handle = bar.FindControl<Button>("QuickActionsHandle")!;
                var panel = bar.FindControl<Border>("QuickActionsPanel")!;
                Assert.True(vm.IsQuickActionsCollapsed);
                Assert.False(panel.IsEffectivelyVisible);
                Assert.True(handle.IsEffectivelyVisible);
                Assert.Equal(24, bar.Bounds.Width);
                Click(window, handle);
                Assert.False(vm.IsQuickActionsCollapsed);
                Assert.True(panel.IsEffectivelyVisible);
                Assert.Equal(304, bar.Bounds.Width);
                Assert.True(workspace.Bounds.X >= bar.Bounds.Right);
                var buttons = bar.GetVisualDescendants().OfType<Button>().ToArray();
                var add = buttons.Single(b => AutomationProperties.GetName(b) == "Добавить кнопку в профиль");
                Assert.True(add.IsEffectivelyVisible);
                Assert.Equal(2, buttons.Count(b => AutomationProperties.GetName(b) == "Изменить кнопку" && b.IsEffectivelyVisible));
                var run = buttons.Single(b => ReferenceEquals(b.Command, vm.QuickActions[0].RunCommand));
                Assert.True(run.IsEffectivelyEnabled); Assert.True(run.Bounds.Width >= 160);
                var second = buttons.Single(b => ReferenceEquals(b.Command, vm.QuickActions[1].RunCommand));
                Assert.True(second.TranslatePoint(default, bar)!.Value.Y >= run.TranslatePoint(default, bar)!.Value.Y + run.Bounds.Height);
                Click(window, handle); Assert.False(run.IsEffectivelyVisible);
                Click(window, handle); Assert.True(run.IsEffectivelyVisible);
                vm.IsOperatorView = true; Layout(window);
                Assert.False(add.IsEffectivelyVisible);
                Assert.DoesNotContain(buttons, b => AutomationProperties.GetName(b) == "Изменить кнопку" && b.IsEffectivelyVisible);
                Click(window, run); await vm.QuickActions[0].RunCommand.ExecutionTask!;
                Assert.True(vm.QuickActions[0].IsOn);
                Click(window, run); await vm.QuickActions[0].RunCommand.ExecutionTask!;
                Assert.False(vm.QuickActions[0].IsOn);
                vm.IsOperatorView = false; Layout(window); Capture(window, "action-panel.png");
                vm.IsOperatorView = true; vm.RootGraph.QuickActions.Clear(); Layout(window);
                Assert.False(handle.IsEffectivelyVisible);
                Assert.Equal(0, bar.Bounds.Width);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Editor_UsesDetachedGraphs_Validates_AndHasNoHardwareRunCommands()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var source = new ProfileActionDefinition { Name = "Питание", Display = ProfileActionDisplay.Toggle };
            var window = new ProfileActionEditorWindow(source, null);
            try
            {
                window.Show(); Layout(window);
                window.FindControl<TextBox>("NameInput")!.Text = "Питание стенда";
                window.FindControl<TextBox>("ColorInput")!.Text = "#15803D";
                var on = (TestViewModel)window.FindControl<GraphEditorView>("OnEditor")!.DataContext!;
                var off = (TestViewModel)window.FindControl<GraphEditorView>("OffEditor")!.DataContext!;
                Assert.True(on.IsActionGraphEditor); Assert.True(on.CanEditGraph);
                Assert.False(on.RunGraphCommand.CanExecute(null)); Assert.False(on.ToggleConnectionCommand.CanExecute(null));
                Assert.False(on.SaveGraphCommand.CanExecute(null));
                Assert.NotSame(source.Graph.Nodes[0], on.RootGraph.Nodes[0]);
                Assert.NotSame(on.RootGraph, off.RootGraph);
                on.RootGraph.Nodes[0].CustomName = "Включение";
                Assert.Empty(source.Graph.Nodes[0].CustomName);
                var result = window.BuildResult();
                Assert.Equal("Питание стенда", result.Name); Assert.Equal("#15803D", result.Color);
                Assert.Equal("Включение", result.Graph.Nodes[0].CustomName);
                Assert.NotSame(on.RootGraph.Nodes[0], result.Graph.Nodes[0]);
                Assert.True(window.FindControl<TabItem>("OffTab")!.IsVisible);
                window.FindControl<ComboBox>("DisplayInput")!.SelectedIndex = 2; Layout(window);
                Assert.False(window.FindControl<TabItem>("OffTab")!.IsVisible);
                window.FindControl<TextBox>("ColorInput")!.Text = "invalid";
                Assert.Throws<InvalidOperationException>(() => window.BuildResult());
                window.FindControl<TextBox>("ColorInput")!.Text = "#15803D";
                window.FindControl<ComboBox>("DisplayInput")!.SelectedIndex = 1; Layout(window);
                Capture(window, "action-editor.png");
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RealMainGraph_ContinuesWhenButtonIsCancelled_AndKeepsItsLogFile()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            var folder = Path.Combine(Path.GetTempPath(), "action-main-" + Guid.NewGuid().ToString("N"));
            var settings = TestBuilder.Services.AppSettings.Instance;
            var oldFolder = settings.LogFolder; var oldLogging = settings.EnableFileLogging;
            settings.LogFolder = folder; settings.EnableFileLogging = true;
            try
            {
                TestBuilder.Services.GraphSerializer.Deserialize(System.Text.Json.JsonSerializer.Serialize(TestBuilder.Services.GraphSerializer.ExportActionGraph(ProfileActionTests.DelayGraph(500))), vm);
                vm.RootGraph.QuickActions.Add(new() { Name = "Отдельная задержка", Graph = ProfileActionTests.DelayGraph(5000) });
                var main = vm.RunGraphCommand.ExecuteAsync(null);
                Assert.True(vm.IsTestRunning);
                var log = TestBuilder.Services.Logging.LoggingService.Instance.CurrentLogFilePath;
                Assert.NotNull(log);
                var button = vm.QuickActions[0];
                var action = button.RunCommand.ExecuteAsync(null);
                Assert.True(button.IsRunning); Assert.True(vm.IsTestRunning);
                Assert.Equal(log, TestBuilder.Services.Logging.LoggingService.Instance.CurrentLogFilePath);
                button.Cancel(); await action;
                Assert.True(vm.IsTestRunning);
                await main;
                Assert.False(vm.IsTestRunning);
                var text = File.ReadAllText(log!);
                Assert.Contains("[Кнопка: Отдельная задержка]", text);
                Assert.Contains("Действие остановлено", text);
                Assert.DoesNotContain("[ПРИЧИНА СБОЯ]", text);
                Assert.False(bus.IsConnected);
            }
            finally { settings.LogFolder = oldFolder; settings.EnableFileLogging = oldLogging; if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InitialGraph_ActualNodeSizesHaveNoCollisions()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(TestBuilder.Services.GraphSerializer.ExportActionGraph(ProfileActionDefinition.EmptyGraph("Кнопка"))));
                Assert.Empty(ProfileLayout.ProfileLayoutRunner.Process(path, false).Conflicts);
            }
            finally { File.Delete(path); }
        }, CancellationToken.None);
    }
    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout(window);
    }
    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); Dispatcher.UIThread.RunJobs();
    }
    private static void Capture(Window window, string file)
    {
        var path = Environment.GetEnvironmentVariable("TFORTIS_UI_ARTIFACTS");
        if (string.IsNullOrEmpty(path)) return;
        Directory.CreateDirectory(path); using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(path, file));
    }
}
