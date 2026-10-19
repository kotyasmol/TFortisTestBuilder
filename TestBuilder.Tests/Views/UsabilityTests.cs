using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Models;
using TestBuilder.Services;
using TestBuilder.Services.Help;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class UsabilityTests
{
    [Theory]
    [InlineData(false, 1100)]
    [InlineData(true, 1100)]
    [InlineData(false, 1440)]
    [InlineData(true, 1440)]
    public async Task BoardNavigationAndCollapsibleProfiles_KeepGraphAndFitWindow(bool dark, int width)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var oldMode = AppSettings.Instance.EngineerMode;
            var oldTheme = Application.Current!.RequestedThemeVariant;
            var window = new MainWindow { Width = width, Height = 800 };
            var vm = (MainWindowViewModel)window.DataContext!;
            try
            {
                vm.SettingsVM.IsEngineerMode = true;
                Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                vm.SelectPageCommand.Execute("Network"); Assert.True(vm.IsNetworkPage);
                GraphSerializer.Deserialize(File.ReadAllText(Profile("board_highlight_demo.json")), vm.TestVM);
                Assert.True(vm.IsEditorPage); Assert.False(vm.ShowSwitchTools);
                window.Show(); Layout(window);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && Equals(b.Content, "Сеть"));
                var graph = window.FindControl<TestView>("TestViewControl")!.FindControl<GraphEditorView>("GraphEditor")!;
                var titleField = graph.GetVisualDescendants().OfType<AutoCompleteBox>().Single(c => c.Name == "ProfileNameInput");
                Assert.Equal(vm.TestVM.ProfileDescription, titleField.Text);
                var graphWidth = graph.Bounds.Width;
                var sameNodes = vm.TestVM.Nodes.ToArray();
                var toggle = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.TestVM.ToggleProfilesCommand));
                Click(window, toggle); Layout(window);
                Assert.True(vm.TestVM.IsProfilesCollapsed);
                Assert.True(graph.Bounds.Width >= graphWidth + 250);
                Assert.Equal(sameNodes, vm.TestVM.Nodes);
                Capture(window, $"editor-board-collapsed-{width}-{dark}");
                Click(window, toggle); Layout(window);
                Assert.False(vm.TestVM.IsProfilesCollapsed);
                Assert.Equal(graphWidth, graph.Bounds.Width, 1);
                Capture(window, $"editor-board-{width}-{dark}");
                vm.TestVM.ApplyBoard(vm.TestVM.RootGraph.Board!, false);
                Assert.True(vm.ShowSwitchTools);
                vm.SelectPageCommand.Execute("Network"); Assert.True(vm.IsNetworkPage);
                var currentGraph = vm.TestVM.RootGraph;
                var currentNodes = vm.TestVM.Nodes.ToArray();
                vm.TestVM.ProductFilter = "Платы"; Layout(window);
                Assert.False(vm.ShowSwitchTools); Assert.True(vm.IsEditorPage);
                Assert.Equal("Автономный стенд", vm.TestVM.StandSectionTitle);
                Assert.Same(currentGraph, vm.TestVM.RootGraph); Assert.Equal(currentNodes, vm.TestVM.Nodes);
                vm.SelectPageCommand.Execute("SelfTest"); Assert.True(vm.IsEditorPage);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && Equals(b.Content, "Сеть"));
                Capture(window, $"editor-autonomous-filter-{width}-{dark}");
                vm.TestVM.ProductFilter = "Все изделия";
                Assert.True(vm.ShowSwitchTools);
                vm.SelectPageCommand.Execute("Editor"); Layout(window);
                Capture(window, $"editor-switch-{width}-{dark}");
                var settings = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.OpenSettingsCommand));
                var location = settings.TranslatePoint(default, window)!.Value;
                Assert.True(location.X >= 0 && location.X + settings.Bounds.Width <= width);
            }
            finally { vm.SettingsVM.IsEngineerMode = oldMode; window.Close(); Dispatcher.UIThread.RunJobs(); Application.Current.RequestedThemeVariant = oldTheme; }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task PaletteHoverExplainsNode_WithoutAddingIt(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var training = new TutorialSession();
            var vm = training.Model; vm.IsOperatorView = false;
            var previous = Application.Current!.RequestedThemeVariant;
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var view = new NodePaletteView { DataContext = vm, Width = 280, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            var window = new Window { Content = view, Width = 700, Height = 600 };
            try
            {
                window.Show(); Layout(window);
                var entry = view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("nodeEntry") && b.DataContext is OperatorActionNodeViewModel);
                var before = vm.Nodes.ToArray();
                var point = entry.TranslatePoint(new Point(entry.Bounds.Width / 2, entry.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                var until = DateTime.UtcNow.AddSeconds(3);
                while (!ToolTip.GetIsOpen(entry) && DateTime.UtcNow < until) { await Task.Delay(50); Layout(window); }
                Assert.True(ToolTip.GetIsOpen(entry));
                var tip = Assert.IsType<StackPanel>(ToolTip.GetTip(entry));
                var texts = tip.Children.OfType<TextBlock>().Select(t => t.Text).ToArray();
                Assert.Contains("Действие оператора", texts);
                Assert.Contains(((OperatorActionNodeViewModel)entry.DataContext!).HelpSummary, texts);
                Assert.Equal(before, vm.Nodes);
                Capture(window, $"palette-hover-{dark}");
            }
            finally { window.Close(); Application.Current.RequestedThemeVariant = previous; }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task GuideAndHardware_AreUsableInBothThemes(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var vm = new MainWindowViewModel();
            var oldTheme = Application.Current!.RequestedThemeVariant;
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            try
            {
                vm.TestVM.IsOperatorView = false;
                GraphSerializer.Deserialize(File.ReadAllText(Profile("RTL_SW_board.json")), vm.TestVM);
                var original = GraphSerializer.Serialize(vm.TestVM, "Исходный профиль");
                var hardware = new HardwareEditorWindow(vm.TestVM);
                try
                {
                    hardware.Show(); Layout(hardware); Capture(hardware, $"hardware-{dark}");
                    var grids = hardware.GetVisualDescendants().OfType<DataGrid>().ToArray();
                    Assert.Equal(2, grids.Length);
                    var search = hardware.GetVisualDescendants().OfType<TextBox>().Single(t => t.Watermark == "Найти регистр");
                    search.Text = "99999"; Layout(hardware);
                    Assert.Empty(grids.Single(g => g.Columns.Count > 2).ItemsSource.Cast<object>());
                    search.Text = ""; Layout(hardware);
                    hardware.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Применить к профилю")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.False(hardware.IsVisible, string.Join(" | ", hardware.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)));
                    Assert.Equal(original, GraphSerializer.Serialize(vm.TestVM, "Исходный профиль"));
                }
                finally { hardware.Close(); Dispatcher.UIThread.RunJobs(); }
                Assert.Equal(52, UserGuide.All.Count);
                foreach (var node in vm.TestVM.AvailableNodes.Concat(new[] { new BodyStartNodeViewModel(), (TestBuilder.ViewModels.NodifyVM.NodeViewModel)new BodyEndNodeViewModel() }))
                {
                    var article = Assert.IsType<GuideArticle>(UserGuide.ForNode(node.GetType()));
                    Assert.NotEmpty(article.Blocks.Single(b => b.Kind == "steps").Items);
                    Assert.Equal(article.Summary, node.HelpSummary);
                }
                var guide = new UserGuideWindow();
                try
                {
                    guide.Show(); Layout(guide); Capture(guide, $"guide-{dark}");
                    var search = guide.GetVisualDescendants().OfType<TextBox>().Single();
                    search.Text = "Проверка цвета светодиода"; Layout(guide);
                    Assert.Single(guide.GetVisualDescendants().OfType<ListBox>().Single().Items);
                    Assert.Contains(guide.GetVisualDescendants().OfType<SelectableTextBlock>(), t => t.Text!.Contains("фотодатчиков"));
                    Capture(guide, $"guide-node-{dark}");
                    search.Text = "нет-такой-статьи"; Layout(guide);
                    Assert.Contains(guide.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Ничего не найдено");
                    File.WriteAllText(Path.Combine(Output(), "TFortis-инструкция.html"), UserGuide.ExportHtml());
                }
                finally { guide.Close(); Dispatcher.UIThread.RunJobs(); }
                foreach (var key in new[] { "TextPrimaryBrush", "TextSecondaryBrush", "TextMutedBrush" })
                {
                    Application.Current.TryGetResource(key, Application.Current.ActualThemeVariant, out var text);
                    Application.Current.TryGetResource("SurfaceBrush", Application.Current.ActualThemeVariant, out var bg);
                    var foreground = ((ISolidColorBrush)text!).Color; var background = ((ISolidColorBrush)bg!).Color;
                    var ratio = (Math.Max(Luminance(foreground), Luminance(background)) + .05) / (Math.Min(Luminance(foreground), Luminance(background)) + .05);
                    Assert.True(ratio >= 4.5, $"{key}: contrast {ratio:0.00}");
                }
            }
            finally { Application.Current.RequestedThemeVariant = oldTheme; }
        }, CancellationToken.None);
    }
    private static double Luminance(Color c) { double Channel(byte x) { var v = x / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); } return .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B); }
    private static void Click(Window window, Control control) { var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value; window.MouseDown(p, MouseButton.Left); window.MouseUp(p, MouseButton.Left); }
    private static string Profile(string name) => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../profiles", name));
    private static string Output() { var path = Environment.GetEnvironmentVariable("TFORTIS_UI_ARTIFACTS") ?? Path.Combine(Path.GetTempPath(), "tfortis-usability-ui"); Directory.CreateDirectory(path); return path; }
    private static void Layout(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); Dispatcher.UIThread.RunJobs(); }
    private static void Capture(Window window, string name) { using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); frame.Save(Path.Combine(Output(), name + ".png")); }
}
