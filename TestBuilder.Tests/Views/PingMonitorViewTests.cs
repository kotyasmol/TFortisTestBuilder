using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Models;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.MonitoringTests;
using TestBuilder.Tests.ProfileActions;
using TestBuilder.ViewModels;
using TestBuilder.Views;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class PingMonitorViewTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EngineerPanel_SitsRightOfLogs_BoardsHideIt_AndOperatorVisibilityIsConfigurable(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
            ProfileActionTests.LoadEmpty(vm);
            using var editor = new TestView { DataContext = vm };
            var window = new Window { Content = editor, Width = 1280, Height = 800, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            try
            {
                window.Show(); Layout(window);
                var panel = editor.FindControl<PingMonitorView>("EngineerPingPanel")!;
                var logs = editor.FindControl<LogPanelView>("LogPanel")!;
                Assert.True(panel.IsEffectivelyVisible);
                var left = panel.TranslatePoint(default, editor)!.Value.X;
                Assert.True(left >= logs.TranslatePoint(default, editor)!.Value.X + logs.Bounds.Width);
                Assert.InRange(panel.Bounds.Height, 100, 180);
                Capture(window, $"ping-engineer-{dark}.png");
                window.Width = 1100; Layout(window);
                foreach (var button in logs.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
                {
                    var position = button.TranslatePoint(default, logs)!.Value.X;
                    Assert.True(position >= 0 && position + button.Bounds.Width <= logs.Bounds.Width);
                }
                Capture(window, $"ping-engineer-narrow-{dark}.png");
                window.Width = 1280; Layout(window);
                var logWidth = logs.Bounds.Width;
                vm.RootGraph.ProductKind = "Board"; vm.NotifyProfileMetadataChanged(); Layout(window);
                Assert.False(panel.IsEffectivelyVisible); Assert.True(logs.Bounds.Width > logWidth + 200);
                Assert.False(vm.ShowOperatorPing);
                var settings = PingMonitorTests.Settings(); settings.ShowInEngineer = false; vm.ApplyPingSettings(settings);
                Assert.False(vm.ShowEngineerPing); Assert.True(vm.ShowOperatorPing);
                vm.IsOperatorView = true;
                var station = new StationDashboardView { DataContext = vm }; window.Content = station; Layout(window);
                var operatorPanel = station.FindControl<PingMonitorView>("OperatorPingPanel")!;
                Assert.True(operatorPanel.IsEffectivelyVisible);
                Assert.DoesNotContain(operatorPanel.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible);
                Capture(window, $"ping-operator-{dark}.png");
                vm.IsOperatorView = false; settings.ShowInOperator = false; vm.ApplyPingSettings(settings); Layout(window);
                Assert.False(operatorPanel.IsEffectivelyVisible);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SettingsDialog_OpensFromPanel_Validates_AndCancelDoesNotChangeProfile()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
            ProfileActionTests.LoadEmpty(vm);
            var panel = new PingMonitorView { DataContext = vm };
            var owner = new Window { Content = panel, Width = 350, Height = 240 };
            try
            {
                owner.Show(); Layout(owner);
                var configure = panel.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Настройки пинга");
                Click(owner, configure);
                var dialog = Assert.IsType<PingSettingsWindow>(owner.OwnedWindows.Single()); Layout(dialog);
                var draft = Assert.IsType<PingSettingsViewModel>(dialog.DataContext);
                dialog.FindControl<CheckBox>("EnabledInput")!.IsChecked = true;
                var apply = dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Применить к профилю"));
                Click(dialog, apply); Assert.True(dialog.IsVisible); Assert.NotEmpty(draft.Error);
                dialog.FindControl<TextBox>("HostInput")!.Text = "192.0.2.15";
                dialog.FindControl<NumericUpDown>("IntervalInput")!.Value = 2000;
                dialog.FindControl<CheckBox>("OperatorInput")!.IsChecked = true;
                Layout(dialog); Capture(dialog, "ping-settings.png");
                Click(dialog, apply); await vm.ConfigurePingCommand.ExecutionTask!;
                Assert.Equal("192.0.2.15", vm.RootGraph.PingMonitor!.Host); Assert.Equal(2000, vm.RootGraph.PingMonitor.IntervalMs);
                Assert.True(vm.RootGraph.PingMonitor.ShowInOperator);
                var saved = GraphSerializer.Serialize(vm, "profile");
                Click(owner, configure);
                dialog = Assert.IsType<PingSettingsWindow>(owner.OwnedWindows.Single()); Layout(dialog);
                dialog.FindControl<TextBox>("HostInput")!.Text = "different.local";
                Click(dialog, dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Отмена")));
                await vm.ConfigurePingCommand.ExecutionTask!;
                Assert.Equal(saved, GraphSerializer.Serialize(vm, "profile"));
            }
            finally { owner.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MainWindow_ActivatesOnePoller_ContinuesInOperatorAndDuringTest_AndStopsOnClose()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var window = new MainWindow(); var shell = (MainWindowViewModel)window.DataContext!;
            var vm = shell.TestVM;
            try
            {
                vm.IsOperatorView = false; ProfileActionTests.LoadEmpty(vm);
                vm.ApplyPingSettings(PingMonitorTests.Settings("127.0.0.1"));
                Assert.Equal("Опрос остановлен", vm.PingMonitor.StatusText);
                window.Show(); Layout(window);
                await PingMonitorTests.Until(() => vm.PingMonitor.HasReply);
                Capture(window, "ping-live-main.png");
                Assert.False(vm.IsConnected); // ping does not require or open COM
                vm.IsOperatorView = true; vm.IsTestRunning = true;
                Assert.True(vm.PingMonitor.HasReply); Assert.False(vm.ConfigurePingCommand.CanExecute(window));
                vm.IsTestRunning = false;
                window.Close(); await vm.PingMonitor.Completion.WaitAsync(TimeSpan.FromSeconds(3));
            }
            finally { vm.IsTestRunning = false; window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TrainingAndActionEditors_DoNotStartPingingEvenWithEnabledSettings()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var bus = new ModbusService();
            using var training = new TestViewModel(bus, new SlaveManager(bus), true, Path.GetTempPath());
            training.RootGraph.PingMonitor = PingMonitorTests.Settings("127.0.0.1");
            training.SetPingMonitoringActive(true);
            Assert.False(training.ShowEngineerPing); Assert.False(training.ShowOperatorPing);
            Assert.Equal("Опрос остановлен", training.PingMonitor.StatusText); Assert.True(training.PingMonitor.Completion.IsCompleted);
            using var action = new TestViewModel(bus, new SlaveManager(bus)) { IsActionGraphEditor = true };
            action.RootGraph.PingMonitor = PingMonitorTests.Settings("127.0.0.1"); action.SetPingMonitoringActive(true);
            Assert.Equal("Опрос остановлен", action.PingMonitor.StatusText); Assert.True(action.PingMonitor.Completion.IsCompleted);
        }, CancellationToken.None);
    }

    private static void Click(Window window, Button button)
    {
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout(window);
    }
    private static void Layout(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); Dispatcher.UIThread.RunJobs(); }
    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("TFORTIS_UI_ARTIFACTS"); if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir); using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(dir, name));
    }
}
