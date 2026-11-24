using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Services;
using TestBuilder.ViewModels;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class NetworkToolsLayoutTests
{
    private static NetworkConfigurationPreset Preset() => new("fixture", "Четыре тестовых порта",
        new NetworkPortBatch(4, 1, "LAN", "Медь", 100, "10.42.0.2", "172.20.0.2", true).AppendTo(new()));
    private static BenchAdapter[] Cards(NetworkConfigurationPreset preset) => preset.Plan.Addresses.Select((p, i) =>
        new BenchAdapter("card-" + i, "Ethernet " + i, "00112233" + i.ToString("X4"), "Up", p.Ip, 1000)).ToArray();

    [Theory]
    [InlineData(false, 1100)] [InlineData(true, 1100)]
    [InlineData(false, 1440)] [InlineData(true, 1440)]
    public async Task MainScreenKeepsToolsCompactAndPresetMenuLoadsOnlyOnExplicitChoice(bool dark, int width)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            var oldTheme = Application.Current!.RequestedThemeVariant;
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var preset = Preset(); var saves = 0;
            var vm = new NetworkSetupViewModel(() => Cards(preset), () => true, loadPresets: () => new[] { preset }, saveSettings: () => saves++);
            var view = new NetworkSetupView { DataContext = vm };
            var window = new Window { Width = width, Height = 800, Content = view };
            try
            {
                window.Show(); Layout(window);
                var diagnostics = view.FindControl<ToggleButton>("NetworkDiagnosticsToggle")!;
                var panel = view.FindControl<Border>("NetworkDiagnosticsPanel")!;
                Assert.False(diagnostics.IsChecked == true); Assert.False(panel.IsEffectivelyVisible);
                Assert.DoesNotContain(view.GetVisualDescendants().OfType<Expander>(), e => Equals(e.Header, "Наборы, связь и компоненты"));
                Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, preset.Name) && b.IsEffectivelyVisible);
                Assert.Equal(4, view.FindControl<DataGrid>("NetworkAdaptersGrid")!.ItemsSource.Cast<object>().Count());
                var before = JsonSerializer.Serialize(AppSettings.Instance.NetworkPlan);
                Capture(window, "main");
                var button = view.FindControl<Button>("NetworkPresetsButton")!;
                Click(window, button);
                var flyout = Assert.IsType<Flyout>(button.Flyout);
                var content = Assert.IsAssignableFrom<Control>(flyout.Content);
                Layout(window);
                Assert.Equal(before, JsonSerializer.Serialize(AppSettings.Instance.NetworkPlan)); Assert.Equal(0, saves);
                var load = content.GetVisualDescendants().OfType<Button>().Single(b => b.DataContext is NetworkPresetItem item && item.Definition.Id == preset.Id);
                Assert.True(load.IsEnabled);
                load.Command!.Execute(load.CommandParameter); // Same command as the popup row.
                load.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Layout(window);
                Assert.Equal(1, saves); Assert.Equal(4, vm.Rows.Count); Assert.True(vm.HasPresetStatus);
                Assert.False(flyout.IsOpen);
                Click(window, diagnostics); Assert.True(panel.IsEffectivelyVisible);
                var tabs = view.FindControl<TabControl>("NetworkDiagnosticsTabs")!;
                Assert.Equal(new[] { "Связь портов", "Найти карту по кабелю", "Компоненты DataTest" },
                    tabs.Items.OfType<TabItem>().Select(t => t.Header));
                Assert.Equal(OperatingSystem.IsWindows(), tabs.Items.OfType<TabItem>().Single(t => Equals(t.Header, "Связь портов")).IsVisible);
                Assert.Equal(OperatingSystem.IsWindows(), tabs.Items.OfType<TabItem>().Single(t => Equals(t.Header, "Компоненты DataTest")).IsVisible);
                if (!OperatingSystem.IsWindows()) Assert.Equal(1, tabs.SelectedIndex);
                Assert.True(panel.Bounds.Height < 400);
                Capture(window, "diagnostics");
                Click(window, diagnostics); Assert.False(panel.IsEffectivelyVisible);
                Assert.Empty(window.OwnedWindows);
                void Capture(Window target, string step)
                {
                    var folder = Path.Combine(Path.GetTempPath(), "tfortis-network-tools-20261123"); Directory.CreateDirectory(folder);
                    using var frame = target.CaptureRenderedFrame(); frame!.Save(Path.Combine(folder, $"{step}-{dark}-{width}.png"));
                }
            }
            finally { window.Close(); Application.Current.RequestedThemeVariant = oldTheme; }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ProbeCanStillBeStoppedWhenDiagnosticsIsHidden()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(async () =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            var preset = Preset(); AppSettings.Instance.NetworkPlan = preset.CopyPlan();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var vm = new NetworkSetupViewModel(() => Cards(preset), () => true, probePair: async (pair, token) =>
            {
                entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return new(pair, "unused", "");
            }, loadPresets: () => new[] { preset });
            var window = new Window { Width = 1100, Height = 800, Content = new NetworkSetupView { DataContext = vm } };
            try
            {
                window.Show(); Layout(window);
                var before = JsonSerializer.Serialize(AppSettings.Instance.NetworkPlan);
                var task = vm.ProbeConnectionsCommand.ExecuteAsync(null); await entered.Task; Layout(window);
                Assert.True(vm.CanCancelProbe);
                var cancel = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Остановить проверку связи"));
                Assert.True(cancel.IsEffectivelyVisible); Click(window, cancel); await task;
                Assert.False(vm.IsBusy); Assert.Contains("остановлена", vm.ProbeStatus);
                Assert.Equal(before, JsonSerializer.Serialize(AppSettings.Instance.NetworkPlan));
            }
            finally { vm.CancelProbeCommand.Execute(null); window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public void OpeningNetworkPageDoesNotAutomaticallyInspectComponents()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
        using var vm = new MainWindowViewModel();
        vm.SelectPageCommand.Execute("Network");
        Assert.True(vm.IsNetworkPage); Assert.False(vm.NetworkSetupVM.IsCheckingRuntime);
        Assert.Equal("Компоненты DataTest ещё не проверены.", vm.NetworkSetupVM.RuntimeSummary);
    }
    private static void Layout(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); Dispatcher.UIThread.RunJobs(); }
    private static void Click(Window window, Control control)
    {
        control.BringIntoView(); Layout(window);
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout(window);
    }
}
