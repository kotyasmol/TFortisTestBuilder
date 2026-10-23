using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nodify;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Services;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class WindowLayoutTests
{
    [Theory]
    [InlineData(1100, 680, false)]
    [InlineData(1440, 860, false)]
    [InlineData(1100, 680, true)]
    public async Task ScreensFitWindow(int width, int height, bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var vm = new MainWindowViewModel();
            Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            vm.TestVM.IsOperatorView = false;
            var profile = vm.TestVM.Profiles.First(p => p.Name.Contains("8x2Pro", StringComparison.OrdinalIgnoreCase));
            vm.TestVM.SelectedProfile = profile;
            vm.SlaveManager.Slaves.Add(new PS2Model(21, vm.ModbusService));
            vm.ModbusVM.SelectedSlave = vm.SlaveManager.Slaves[0];
            vm.TestVM.SelfTestPageState.SetLoaded("http://192.168.0.1/test.shtml", "Dut",
                new Dictionary<string,string> { ["dev_type"] = "6", ["default_mac"] = "C0:11:A6:00:12:34", ["init_ok"] = "1", ["sensor_1"] = "0", ["poe_a_1_state"] = "1" });
            using var test = new TestView { DataContext = vm.TestVM };
            var views = new (string Name, Control View)[]
            {
                ("editor", test),
                ("station", new StationDashboardView { DataContext = vm.TestVM }),
                ("settings", new SettingsView { DataContext = vm.SettingsVM }),
                ("network", new NetworkSetupView { DataContext = vm.NetworkSetupVM }),
                ("modbus", new ModbusMonitoringView { DataContext = vm.ModbusVM }),
                ("selftest", new SelfTestPageView { DataContext = vm.SelfTestPageVM })
            };
            foreach (var (name, view) in views)
            {
                var window = new Window { Width = width, Height = height, Content = view };
                try
                {
                    window.Show();
                    Layout(window);
                    Assert.InRange(view.Bounds.Width, 1, width);
                    Assert.InRange(view.Bounds.Height, 1, height);
                    AssertActionsFit(view);
                    if (name == "settings")
                    {
                        var panel = view.GetVisualDescendants().OfType<StackPanel>().Single(p => p.MaxWidth == 800);
                        var origin = panel.TranslatePoint(default, window)!.Value;
                        Assert.InRange(Math.Abs(origin.X + panel.Bounds.Width / 2 - width / 2.0), 0, 2);
                    }
                    Capture(window, name, width, dark);
                    if (name == "editor")
                    {
                        var editor = test.FindControl<GraphEditorView>("GraphEditor")!;
                        using var help = new SelfTestCheckNodeViewModel();
                        editor.HelpNode = help;
                        editor.IsNodeHelpOpen = true;
                        Layout(window);
                        var canvas = editor.FindControl<NodifyEditor>("Editor")!;
                        var inspector = editor.FindControl<Grid>("InspectorPanel")!;
                        Assert.True(canvas.Bounds.Right <= inspector.Bounds.Left + 1);
                        Assert.True(canvas.Bounds.Width >= 400);
                        Assert.True(inspector.Bounds.Width >= 280);
                        var handle = inspector.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("edgeHandle"));
                        Assert.InRange(Math.Abs(handle.Bounds.Center.Y - inspector.Bounds.Height / 2), 0, 1);
                        var helpPanel = editor.HelpWindow!;
                        Assert.NotNull(helpPanel);
                        Layout(helpPanel);
                        var helpScroll = helpPanel.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "HelpScroll");
                        Assert.True(helpScroll.Bounds.Height > 120);
                        Assert.True(helpScroll.Extent.Height > helpScroll.Viewport.Height);
                        Capture(helpPanel, "node-help", width, dark);
                        Capture(window, "editor-help", width, dark);
                        editor.IsNodeHelpOpen = false;
                        vm.TestVM.IsTestRunning = true;
                        vm.TestVM.IsTestPaused = true;
                        Layout(window);
                        AssertActionsFit(view);
                        Capture(window, "editor-paused", width, dark);
                        vm.TestVM.IsTestPaused = false;
                        vm.TestVM.IsTestRunning = false;
                    }
                }
                finally { window.Close(); }
            }

            var originalMode = AppSettings.Instance.EngineerMode;
            var shell = new MainWindow { Width = width, Height = height };
            var shellVm = (MainWindowViewModel)shell.DataContext!;
            try
            {
                shellVm.SettingsVM.IsEngineerMode = true;
                Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                shellVm.TestVM.SelectedProfile = profile;
                shell.Show();
                Layout(shell);
                var rootNode = shellVm.TestVM.RootGraph.Nodes.First();
                var graph = shell.FindControl<TestView>("TestViewControl")!.FindControl<GraphEditorView>("GraphEditor")!;
                Assert.True(graph.FindControl<NodifyEditor>("Editor")!.Bounds.Height > 150);
                AssertActionsFit(shell);
                Capture(shell, "shell-editor", width, dark);
                shellVm.OpenSettingsCommand.Execute(null);
                Layout(shell);
                Assert.True(shellVm.IsSettingsOpen);
                Assert.Equal("Настройки", shellVm.WorkspaceTitle);
                Capture(shell, "shell-settings", width, dark);
                shellVm.CloseSettingsCommand.Execute(null);
                shellVm.SwitchWorkspaceCommand.Execute(null);
                Layout(shell);
                Assert.False(shellVm.IsEngineerWorkspace);
                Assert.Same(rootNode, shellVm.TestVM.RootGraph.Nodes.First());
                Capture(shell, "shell-station", width, dark);
                shellVm.TestVM.IsConnecting = true;
                Assert.True(shellVm.SwitchWorkspaceCommand.CanExecute(null));
                shellVm.TestVM.IsConnecting = false;
                shellVm.TestVM.IsTestRunning = true;
                Assert.True(shellVm.SwitchWorkspaceCommand.CanExecute(null));
                var stages = shellVm.TestVM.Station.Stages.ToArray();
                shellVm.TestVM.Station.SerialNumber = "3200123";
                shellVm.SwitchWorkspaceCommand.Execute(null);
                Assert.True(shellVm.IsEngineerWorkspace);
                Assert.False(shellVm.TestVM.CanEditGraph);
                shellVm.SwitchWorkspaceCommand.Execute(null);
                Assert.False(shellVm.IsEngineerWorkspace);
                Assert.True(shellVm.TestVM.IsTestRunning);
                Assert.Equal(stages, shellVm.TestVM.Station.Stages.ToArray());
                Assert.Equal("3200123", shellVm.TestVM.Station.SerialNumber);
                shellVm.TestVM.IsTestRunning = false;
            }
            finally
            {
                shell.Close();
                AppSettings.Instance.EngineerMode = originalMode;
                AppSettings.Instance.Save();
            }
        }, CancellationToken.None);
    }

    private static void AssertActionsFit(Control view)
    {
        foreach (var panel in view.GetVisualDescendants().OfType<WrapPanel>().Where(p => p.Classes.Contains("actions")))
        {
            var children = panel.Children.Where(c => c.IsEffectivelyVisible).ToArray();
            foreach (var child in children)
            {
                Assert.True(child.Bounds.Right <= panel.Bounds.Width + 1, $"{child}: command is clipped horizontally");
                Assert.True(child.Bounds.Bottom <= panel.Bounds.Height + 1, $"{child}: command is clipped vertically");
            }
            for (var i = 0; i < children.Length; i++)
            for (var j = i + 1; j < children.Length; j++)
                Assert.False(children[i].Bounds.Intersects(children[j].Bounds), "Command buttons overlap");
        }
    }

    private static void Layout(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        window.InvalidateVisual();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string name, int width, bool dark)
    {
        var folder = Environment.GetEnvironmentVariable("TESTBUILDER_LAYOUT_CAPTURES");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(folder, $"{name}-{width}-{(dark ? "dark" : "light")}.png"));
    }
}
