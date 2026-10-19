using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Models;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class StationBoardViewTests
{
    static string Profiles => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../profiles"));
    static string Output => Environment.GetEnvironmentVariable("TFORTIS_BOARD_PULT_ARTIFACTS") ?? Path.Combine(Path.GetTempPath(), "tfortis-board-pult");
    static GraphProfile Profile(string file)
    {
        var path = TestBuilder.Tests.Support.TestProfilePaths.Find(file);
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var root = json.RootElement;
        string? Value(string key) => root.TryGetProperty(key, out var value) ? value.GetString() : null;
        return new(path, Value("name") ?? file, Value("deviceModel"), Value("configurationName"), productKind: Value("productKind") ?? "Switch");
    }

    [Theory]
    [InlineData(false, "RTL_POE_board.json", 1320, 850)]
    [InlineData(true, "RTL_SW_board.json", 1100, 680)]
    public async Task BoardAndStagesAreVisible_AndZoomedWindowReceivesLiveResults(bool dark, string file, int width, int height)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = true };
            vm.SelectedProfile = Profile(file);
            var view = new StationDashboardView { DataContext = vm };
            var window = new Window { Width = width, Height = height, Content = view };
            try
            {
                window.Show(); Layout(window);
                var preview = view.FindControl<BoardPreviewView>("InlineBoard")!;
                var canvas = preview.FindControl<BoardCanvas>("BoardSurface")!;
                var stages = view.FindControl<ItemsControl>("BoardStages")!;
                Assert.True(canvas.IsEffectivelyVisible); Assert.True(stages.IsEffectivelyVisible);
                Assert.True(canvas.Bounds.Width >= 450); Assert.True(canvas.Bounds.Height >= 220);
                Assert.True(stages.Bounds.Width >= 240);
                var splitter = view.FindControl<GridSplitter>("BoardStagesSplitter")!;
                var boardWidth = preview.Bounds.Width; var stagesWidth = stages.Bounds.Width;
                var grip = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;
                window.MouseDown(grip, MouseButton.Left);
                window.MouseMove(grip.WithX(grip.X - 100));
                window.MouseUp(grip.WithX(grip.X - 100), MouseButton.Left); Layout(window);
                Assert.True(preview.Bounds.Width < boardWidth - 80);
                Assert.True(stages.Bounds.Width > stagesWidth + 80);
                grip = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;
                window.MouseDown(grip, MouseButton.Left);
                window.MouseMove(grip.WithX(grip.X - 2000));
                window.MouseUp(grip.WithX(grip.X - 2000), MouseButton.Left); Layout(window);
                Assert.True(preview.Bounds.Width >= 240);
                grip = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;
                window.MouseDown(grip, MouseButton.Left);
                window.MouseMove(grip.WithX(grip.X + 2000));
                window.MouseUp(grip.WithX(grip.X + 2000), MouseButton.Left); Layout(window);
                Assert.True(view.FindControl<Grid>("BoardWorkspace")!.ColumnDefinitions[2].ActualWidth >= 220);

                Assert.Equal("100%", preview.FindControl<TextBlock>("ZoomText")!.Text);
                Assert.True(preview.FindControl<TextBlock>("ZoomText")!.Bounds.Height > 10);
                var node = Flatten(vm.RootGraph.Nodes).First(n => !string.IsNullOrWhiteSpace(n.BoardElements));
                var id = node.BoardElements.Split(new[] { ',', ';' }, StringSplitOptions.TrimEntries)[0];
                var context = new TestContext(new RegisterState()); var runtime = new TestNode(null, node);
                await vm.NodeStartedAsync(runtime, context, default); Layout(window);
                Assert.Equal("Проверяется", preview.DisplayedState(id));
                Capture(window, (dark ? "dark" : "light") + "-" + file.Replace(".json", ""));
                Click(window, preview.FindControl<Button>("ZoomInButton")!);
                Assert.True(canvas.Zoom > 1);
                var zoom = canvas.Zoom;
                Click(window, preview.FindControl<Button>("ExpandButton")!); Layout(window);
                var expanded = Assert.Single(window.OwnedWindows.OfType<BoardPreviewWindow>());
                var largePreview = expanded.GetVisualDescendants().OfType<BoardPreviewView>().Single();
                Assert.Equal("Проверяется", largePreview.DisplayedState(id));
                await vm.NodeCompletedAsync(runtime, StepResult.False, context, default); Layout(window); Layout(expanded);
                Assert.Equal("Ошибка", preview.DisplayedState(id)); Assert.Equal("Ошибка", largePreview.DisplayedState(id));
                Assert.Equal(zoom, canvas.Zoom);
                Capture(expanded, (dark ? "dark" : "light") + "-expanded");
                expanded.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); Layout(window);
                Assert.False(expanded.IsVisible);
                Click(window, preview.FindControl<Button>("FitButton")!); Assert.Equal(1, canvas.Zoom);
                vm.SelectedProfile = Profile("station_dashboard_demo.json"); Layout(window);
                Assert.False(view.FindControl<Grid>("BoardWorkspace")!.IsEffectivelyVisible);
                vm.SelectedProfile = Profile(file); Layout(window);
                Assert.True(canvas.IsEffectivelyVisible); Assert.Equal("Не проверен", preview.DisplayedState(id));
                Assert.Equal(1, canvas.Zoom);
            }
            finally { foreach (var w in window.OwnedWindows.ToArray()) w.Close(); window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RealRunUpdatesInlineImage_AndNewRunClearsPreviousResults()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = true };
            vm.SelectedProfile = Profile("board_highlight_demo.json");
            foreach (var d in vm.Nodes.OfType<DelayNodeViewModel>()) d.Milliseconds = 20;
            var view = new StationDashboardView { DataContext = vm };
            var window = new Window { Width = 1280, Height = 800, Content = view };
            try
            {
                window.Show(); Layout(window);
                var preview = view.FindControl<BoardPreviewView>("InlineBoard")!;
                var observed = new HashSet<string>();
                vm.BoardChanged += () => observed.Add(preview.DisplayedState("R1"));
                await vm.RunGraphCommand.ExecuteAsync(null); Layout(window);
                Assert.Contains("Проверяется", observed); Assert.Contains("Годен", observed);
                Assert.Equal("Ошибка", preview.DisplayedState("C1")); Assert.Equal("Не проверен", preview.DisplayedState("U1"));
                Assert.True(vm.Station.HasResult); Capture(window, "demo-results");
                var run = vm.RunGraphCommand.ExecuteAsync(null); Layout(window);
                Assert.Equal("Не проверен", preview.DisplayedState("C1"));
                vm.StopTestCommand.Execute(null); await run;
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task OperatorPromptKeepsBoardLarge_AndItsButtonsCompleteTheActualRun()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = true };
            vm.SelectedProfile = Profile("board_highlight_demo.json"); vm.Nodes.Clear(); vm.Connections.Clear();
            var start = new StartNodeViewModel(); var action = new OperatorActionNodeViewModel { Message = "Проверьте светодиод D1. Он должен гореть зелёным.", BoardElements = "D1" }; var end = new EndNodeViewModel();
            vm.Nodes.Add(start); vm.Nodes.Add(action); vm.Nodes.Add(end);
            vm.Connections.Add(new(start.Output[0], action.Input[0])); vm.Connections.Add(new(action.Output[0], end.Input[0]));
            var view = new StationDashboardView { DataContext = vm }; var window = new Window { Width = 1100, Height = 680, Content = view };
            try
            {
                window.Show(); Layout(window); var run = vm.RunGraphCommand.ExecuteAsync(null);
                for (var i = 0; i < 100 && !vm.Station.IsAwaitingAction; i++) await Task.Delay(10);
                Layout(window); Assert.True(vm.Station.IsAwaitingAction);
                var canvas = view.FindControl<BoardPreviewView>("InlineBoard")!.FindControl<BoardCanvas>("BoardSurface")!;
                Assert.True(canvas.Bounds.Height >= 220);
                var confirm = Assert.Single(view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && Equals(b.Content, "Готово · продолжить")));
                Capture(window, "small-operator-prompt");
                Click(window, confirm); await run; Layout(window);
                Assert.True(vm.Station.IsSuccessful);
                Assert.Equal("Годен", view.FindControl<BoardPreviewView>("InlineBoard")!.DisplayedState("D1"));
            }
            finally { vm.StopTestCommand.Execute(null); window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MissingImageAndDetachReattach_DoNotKeepOldDrawingOrOldSubscriptions()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(() =>
        {
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
            vm.SelectedProfile = Profile("board_highlight_demo.json");
            var preview = new BoardPreviewView { DataContext = vm }; var window = new Window { Width = 900, Height = 650, Content = preview };
            try
            {
                window.Show(); Layout(window); var canvas = preview.FindControl<BoardCanvas>("BoardSurface")!;
                Click(window, preview.FindControl<Button>("ZoomInButton")!); Assert.True(canvas.Zoom > 1);
                window.Content = null; Layout(window);
                vm.RootGraph.Board = new BoardDesign(); vm.NotifyProfileMetadataChanged();
                window.Content = preview; Layout(window);
                Assert.False(canvas.IsVisible); Assert.True(preview.FindControl<TextBlock>("EmptyMessage")!.IsVisible);
                vm.SelectedProfile = Profile("RTL_SW_board.json"); Layout(window);
                Assert.True(canvas.IsVisible); Assert.False(preview.FindControl<TextBlock>("EmptyMessage")!.IsVisible); Assert.Equal(1, canvas.Zoom);
                Capture(window, "reattached-board");
                var bound = Flatten(vm.Nodes).First(n => !string.IsNullOrWhiteSpace(n.BoardElements)).BoardElements.Split(',')[0].Trim();
                Assert.Equal("Не проверен", preview.DisplayedState(bound));
                foreach (var node in Flatten(vm.Nodes)) node.BoardElements = "";
                vm.IsOperatorView = true; Layout(window);
                Assert.Equal("Не привязан", preview.DisplayedState(bound));
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CanvasAllowsSelectionPanningAndZoom_WithoutChangingTheBoardDefinition()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(() =>
        {
            var design = new BoardDesign { Regions = new() { new BoardRegion { Id = "R1", X = .35, Y = .35, Width = .3, Height = .3 } } };
            var canvas = new BoardCanvas { State = _ => "Годен" };
            canvas.SetDesign(design);
            var window = new Window { Width = 700, Height = 500, Content = canvas };
            var original = System.Text.Json.JsonSerializer.Serialize(design);
            try
            {
                window.Show(); Layout(window);
                var center = new Point(350, 250);
                window.MouseDown(center, MouseButton.Left); window.MouseUp(center, MouseButton.Left); Layout(window);
                Assert.Equal("R1", canvas.SelectedId);
                canvas.ZoomIn(); var zoom = canvas.Zoom;
                window.MouseDown(center, MouseButton.Left); window.MouseMove(new Point(430, 280)); window.MouseUp(new Point(430, 280), MouseButton.Left); Layout(window);
                Assert.Equal(zoom, canvas.Zoom); Assert.Equal("R1", canvas.SelectedId);
                canvas.ZoomOut(); Assert.Equal(1, canvas.Zoom);
                for (var i = 0; i < 30; i++) canvas.ZoomIn(); Assert.Equal(8, canvas.Zoom);
                canvas.ResetView(); Assert.Equal(1, canvas.Zoom);
                Assert.Equal(original, System.Text.Json.JsonSerializer.Serialize(design));
                return true;
            }
            finally { window.Close(); canvas.Dispose(); }
        }, CancellationToken.None);
    }

    static IEnumerable<NodeViewModel> Flatten(IEnumerable<NodeViewModel> nodes)
    { foreach (var n in nodes) { yield return n; if (n is ICompositeNodeViewModel c) foreach (var child in Flatten(c.BodyGraph.Nodes)) yield return child; } }
    static void Click(Window w, Control c)
    { var p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w)!.Value; w.MouseDown(p, MouseButton.Left); w.MouseUp(p, MouseButton.Left); Layout(w); }
    static void Layout(Window w) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    static void Capture(Window w, string name) { Directory.CreateDirectory(Output); Layout(w); using var frame = w.CaptureRenderedFrame(); Assert.NotNull(frame); frame.Save(Path.Combine(Output, name + ".png")); }
}
