using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nodify;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Views;
using TestBuilder.ViewModels;
using TestBuilder.Views;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.ProfileActions;

[Collection("Graph editor UI")]
public class RegisterIndicatorPanelTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TopPanel_WrapsAndScalesCardsWithoutScrolling_IndependentlyOfGraphAndSidePanels(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
            ProfileActionTests.LoadEmpty(vm);
            for (var i = 0; i < 10; i++)
            {
                var indicator = RegisterIndicatorTests.Indicator();
                indicator.Name = $"Напряжение PoE {i + 1}"; indicator.SlaveId = (byte)(i + 1);
                indicator.Color = i % 2 == 0 ? "system" : "#2563EB";
                vm.RootGraph.RegisterIndicators.Add(indicator);
            }
            var serialized = GraphSerializer.Serialize(vm, "profile");
            using var view = new TestView { DataContext = vm };
            var window = new Window { Width = 1400, Height = 1000, Content = view, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            try
            {
                window.Show(); Layout(window);
                var graph = view.FindControl<GraphEditorView>("GraphEditor")!;
                var editor = graph.FindControl<NodifyEditor>("Editor")!;
                var panel = graph.FindControl<RegisterIndicatorsView>("GraphRegisterIndicators")!;
                var side = graph.FindControl<ProfileActionBar>("GraphQuickActions")!;
                var logs = view.FindControl<LogPanelView>("LogPanel")!;
                var sidebar = view.FindControl<RoundedPanel>("ProfileSidebar")!;
                var scroll = panel.FindControl<ScrollViewer>("RegisterIndicatorsScroll")!;
                var transforms = panel.GetVisualDescendants().OfType<LayoutTransformControl>().ToArray();
                Assert.Equal(10, transforms.Length);
                Assert.DoesNotContain(side.GetVisualDescendants(), v => v is RegisterIndicatorsView);
                Assert.True(panel.TranslatePoint(default, graph)!.Value.Y + panel.Bounds.Height <= editor.TranslatePoint(default, graph)!.Value.Y + 1);
                Assert.True(editor.Bounds.Height >= graph.FindControl<Grid>("GraphWorkspace")!.Bounds.Height / 2 - 1);
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                var widthAt100 = transforms[0].Bounds.Width;
                var graphZoom = editor.ViewportZoom;
                var sidebarBounds = sidebar.Bounds; var logBounds = logs.Bounds;
                var zoomOut = panel.FindControl<Button>("IndicatorsZoomOut")!;
                for (var i = 0; i < 5; i++) Click(window, zoomOut);
                Assert.Equal(50, vm.RegisterIndicatorsZoom); Assert.False(zoomOut.IsEffectivelyEnabled);
                Assert.Equal(widthAt100 / 2, transforms[0].Bounds.Width, 1);
                Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 1, $"extent={scroll.Extent}, viewport={scroll.Viewport}");
                Assert.Equal(graphZoom, editor.ViewportZoom);
                Assert.Equal(sidebarBounds, sidebar.Bounds); Assert.Equal(logBounds, logs.Bounds);
                Capture(window, $"register-top-50-{dark}.png");
                var asked = false;
                vm.ConfirmDeleteRegisterIndicatorAsync = _ => { asked = true; return Task.FromResult(false); };
                var delete = panel.GetVisualDescendants().OfType<Button>().First(b => AutomationProperties.GetName(b) == "Удалить индикатор");
                Click(window, delete);
                Assert.True(asked); Assert.Equal(10, vm.RegisterIndicators.Count);
                var before = transforms.Select(t => (t.Bounds, Position: t.TranslatePoint(default, panel))).ToArray();
                vm.RegisterIndicators[0].ValueText = "123456,789 В";
                vm.RegisterIndicators[0].StatusText = "Регистр отсутствует в оборудовании профиля";
                vm.RegisterIndicators[0].IsOutOfRange = true; Layout(window);
                Assert.Equal(before, transforms.Select(t => (t.Bounds, Position: t.TranslatePoint(default, panel))).ToArray());
                Click(window, panel.FindControl<Button>("RegisterIndicatorsHandle")!);
                Assert.True(vm.IsRegisterIndicatorsCollapsed);
                Assert.Equal(24, panel.Bounds.Height);
                Assert.True(vm.IsQuickActionsCollapsed); Assert.False(vm.IsPaletteCollapsed);
                Assert.Equal(logBounds, logs.Bounds);
                Click(window, side.FindControl<Button>("QuickActionsHandle")!);
                Assert.True(vm.IsRegisterIndicatorsCollapsed);
                Click(window, side.FindControl<Button>("QuickActionsHandle")!);
                Click(window, panel.FindControl<Button>("RegisterIndicatorsHandle")!);
                Click(window, panel.FindControl<Button>("IndicatorsZoomReset")!);
                Assert.Equal(100, vm.RegisterIndicatorsZoom);
                panel.FindControl<Slider>("IndicatorsZoomSlider")!.Value = 150; Layout(window);
                Assert.Equal(150, vm.RegisterIndicatorsZoom);
                Assert.False(panel.FindControl<Button>("IndicatorsZoomIn")!.IsEffectivelyEnabled);
                Assert.Equal(widthAt100 * 1.5, transforms[0].Bounds.Width, 1);
                Assert.Equal(serialized, GraphSerializer.Serialize(vm, "profile"));
                window.Width = 1100; window.Height = 680; Layout(window);
                Assert.True(editor.Bounds.Height >= graph.FindControl<Grid>("GraphWorkspace")!.Bounds.Height / 2 - 1);
                Assert.True(panel.FindControl<Button>("RegisterIndicatorsHandle")!.IsEffectivelyVisible);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task OperatorPanel_IsIndependentOfButtons_AndRetainsZoomAndCollapseAcrossModes()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var oldMode = AppSettings.Instance.EngineerMode; AppSettings.Instance.EngineerMode = true;
            var window = new MainWindow();
            try
            {
                var shell = (MainWindowViewModel)window.DataContext!; var vm = shell.TestVM;
                ProfileActionTests.LoadEmpty(vm); vm.RootGraph.RegisterIndicators.Add(RegisterIndicatorTests.Indicator());
                window.Show(); Layout(window);
                var editor = window.FindControl<TestView>("TestViewControl")!.FindControl<GraphEditorView>("GraphEditor")!;
                var engineer = editor.FindControl<RegisterIndicatorsView>("GraphRegisterIndicators")!;
                engineer.FindControl<Slider>("IndicatorsZoomSlider")!.Value = 70;
                shell.SettingsVM.IsEngineerMode = false; Layout(window);
                var station = window.FindControl<StationDashboardView>("StationViewControl")!;
                var panel = station.FindControl<RegisterIndicatorsView>("OperatorRegisterIndicators")!;
                Assert.True(panel.IsEffectivelyVisible); Assert.False(vm.ShowQuickActions);
                Assert.Equal(70, panel.FindControl<Slider>("IndicatorsZoomSlider")!.Value);
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible &&
                    AutomationProperties.GetName(b) is "Изменить индикатор" or "Удалить индикатор" or "Добавить индикатор регистра");
                Click(window, panel.FindControl<Button>("IndicatorsZoomOut")!); Assert.Equal(60, vm.RegisterIndicatorsZoom);
                Capture(window, "register-top-operator.png");
                Click(window, panel.FindControl<Button>("RegisterIndicatorsHandle")!);
                shell.SettingsVM.IsEngineerMode = true; Layout(window);
                Assert.True(vm.IsRegisterIndicatorsCollapsed); Assert.Equal(24, engineer.Bounds.Height);
                Assert.Equal(60, engineer.FindControl<Slider>("IndicatorsZoomSlider")!.Value);
            }
            finally { window.Close(); AppSettings.Instance.EngineerMode = oldMode; }
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
