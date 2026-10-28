using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Services;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels;
using TestBuilder.Views;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class ThemeContrastTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ButtonLabelsAndIcons_StayReadableInEveryState(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var buttons = new List<Button>();
            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
            foreach (var style in new[] { "", "primary", "danger" })
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
                foreach (var composite in new[] { false, true })
                {
                    var button = new Button { Content = "Действие", MinWidth = 180, Tag = style + "/" + composite };
                    if (style.Length > 0) button.Classes.Add(style);
                    if (composite)
                    {
                        var icon = new PathIcon { Width = 18, Height = 18, Data = Geometry.Parse("M6 3 L21 12 6 21 Z") };
                        var label = new TextBlock { Text = "Запустить" };
                        label.Bind(TextBlock.ForegroundProperty, new Binding(nameof(Button.Foreground)) { Source = button });
                        button.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { icon, label } };
                    }
                    row.Children.Add(button); buttons.Add(button);
                }
                panel.Children.Add(row);
            }
            var toggle = new Avalonia.Controls.Primitives.ToggleButton { Content = "Следить за логом", IsChecked = true, HorizontalAlignment = HorizontalAlignment.Left };
            panel.Children.Add(toggle); buttons.Add(toggle);
            var swatch = new Button { Background = Brushes.Orange, Width = 32, Height = 32 };
            swatch.Classes.Add("colorSwatch"); panel.Children.Add(swatch); buttons.Add(swatch);
            var window = new Window { Width = 760, Height = 440, Content = panel };
            var failures = new List<string>();
            try
            {
                window.Show(); Layout(window);
                foreach (var enabled in new[] { true, false })
                {
                    foreach (var button in buttons) button.IsEnabled = enabled;
                    foreach (var button in buttons)
                    {
                        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                        foreach (var state in new[] { "normal", "hover", "pressed" })
                        {
                            window.MouseMove(state == "normal" ? new Point(740, 420) : point);
                            if (state == "pressed") window.MouseDown(point, MouseButton.Left);
                            Layout(window);
                            CheckButton(button, $"{dark}/{button.Tag}/{enabled}/{state}", failures);
                            if (button == swatch && enabled)
                                Assert.Equal(Colors.Orange, ((ISolidColorBrush)button.GetVisualDescendants()
                                    .OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter").Background!).Color);
                            if (state == "pressed") window.MouseUp(point, MouseButton.Left);
                        }
                    }
                    Capture(window, $"buttons-{dark}-{enabled}");
                }
                Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ActualRunButtonAndColoredLogs_StayReadableAcrossLiveThemeChange(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var vm = new MainWindowViewModel();
            vm.TestVM.IsOperatorView = false;
            vm.TestVM.IsConnecting = true;
            using var view = new TestView { DataContext = vm.TestVM };
            var window = new Window { Width = 1400, Height = 850, Content = view };
            var failures = new List<string>();
            try
            {
                window.Show();
                foreach (var theme in new[] { dark, !dark })
                {
                    Application.Current!.RequestedThemeVariant = theme ? ThemeVariant.Dark : ThemeVariant.Light;
                    if (vm.TestVM.TestingLogger.Entries.Count == 0)
                    {
                        vm.TestVM.TestingLogger.Info("Обычный журнал");
                        vm.TestVM.TestingLogger.Info("[OK] Проверка пройдена");
                        vm.TestVM.TestingLogger.Info("[ШАГ] Начало проверки");
                        vm.TestVM.TestingLogger.Warning("[ОСТАНОВ] Прервано");
                        vm.TestVM.TestingLogger.Error("Ошибка ответа");
                    }
                    var grid = view.FindControl<Grid>("EditorLogLayout")!;
                    grid.RowDefinitions[2].Height = new GridLength(300);
                    Layout(window);
                    var run = view.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Запустить тест");
                    Assert.False(run.IsEffectivelyEnabled);
                    CheckButton(run, $"Actual run/{theme}", failures);
                    foreach (var row in view.GetVisualDescendants().OfType<LogEntryView>())
                    foreach (var text in row.GetVisualDescendants().OfType<SelectableTextBlock>())
                        CheckInk(text, text.Foreground, $"Log/{theme}/{text.Text}", failures);
                    Capture(window, "editor-contrast-" + theme);
                }
                Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CustomActionColors_ChooseReadableInk_AndUpdateWhenThemeChanges()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var vm = new MainWindowViewModel();
            vm.TestVM.IsOperatorView = true;
            vm.TestVM.IsQuickActionsCollapsed = false;
            foreach (var color in new[] { "#EA580C", "#999999", "#80FFFFFF", "#80000000", "#3B82F6", "#7C3AED", "#FFFFFF", "#101828" })
                vm.TestVM.RootGraph.QuickActions.Add(new() { Name = color, Color = color });
            var bar = new ProfileActionBar { DataContext = vm.TestVM };
            var window = new Window { Width = 360, Height = 700, Content = bar };
            var failures = new List<string>();
            try
            {
                window.Show();
                foreach (var dark in new[] { false, true, false })
                {
                    Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                    Layout(window);
                    var buttons = bar.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("profileAction")).ToArray();
                    Assert.Equal(8, buttons.Length);
                    foreach (var button in buttons)
                    {
                        CheckButton(button, "Custom/" + dark + "/" + AutomationProperties.GetName(button), failures);
                        button.IsEnabled = false; Layout(window);
                        CheckButton(button, "Custom disabled/" + dark + "/" + AutomationProperties.GetName(button), failures);
                        button.ClearValue(InputElement.IsEnabledProperty);
                    }
                    Capture(window, "custom-actions-" + dark);
                }
                Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void CheckButton(Button button, string name, List<string> failures)
    {
        foreach (var text in button.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)))
            CheckInk(text, text.Foreground, name + "/text", failures);
        foreach (var icon in button.GetVisualDescendants().OfType<PathIcon>().Where(t => t.IsEffectivelyVisible))
            CheckInk(icon, icon.Foreground, name + "/icon", failures);
    }

    [Fact]
    public async Task DisabledInputsAndRegisterRows_StayReadableWhenSwitchingTheme()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var controls = new StackPanel { Spacing = 12, Margin = new Thickness(20), Children =
            {
                new TextBox { Text = "Название профиля", IsEnabled = false },
                new TextBox { Watermark = "Название проверки", IsEnabled = false },
                new AutoCompleteBox { Text = "Устройство", IsEnabled = false },
                new ComboBox { ItemsSource = new[] { "Коммутатор", "Плата" }, SelectedIndex = 0, IsEnabled = false },
                new NumericUpDown { Value = 123, IsEnabled = false },
                new CheckBox { Content = "При открытии профиля", IsChecked = true, IsEnabled = false }
            } };
            var window = new Window { Width = 500, Height = 440, Content = controls };
            using var vm = new MainWindowViewModel();
            vm.SlaveManager.Slaves.Add(new TestBuilder.Domain.Modbus.Models.PS2Model(21, vm.ModbusService));
            vm.ModbusVM.SelectedSlave = vm.SlaveManager.Slaves[0];
            var registers = new ModbusMonitoringView { DataContext = vm.ModbusVM };
            var registerWindow = new Window { Width = 1300, Height = 820, Content = registers };
            var failures = new List<string>();
            try
            {
                window.Show(); registerWindow.Show();
                registers.GetVisualDescendants().OfType<DataGrid>().Single().SelectedIndex = 0;
                foreach (var dark in new[] { false, true, false })
                {
                    Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                    Layout(window); Layout(registerWindow);
                    foreach (var text in controls.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)))
                        CheckInk(text, text.Foreground, "Disabled input/" + dark + "/" + text.Text, failures);
                    foreach (var text in controls.GetVisualDescendants().OfType<TextPresenter>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)))
                        CheckInk(text, text.Foreground, "Disabled value/" + dark + "/" + text.Text, failures);
                    var rows = registers.GetVisualDescendants().OfType<DataGridRow>().ToArray();
                    Assert.NotEmpty(rows);
                    foreach (var row in rows)
                    {
                        var background = ((ISolidColorBrush)row.Background!).Color;
                        var readOnly = ((TestBuilder.Domain.Modbus.Models.RegisterItem)row.DataContext!).IsReadOnly;
                        Assert.Equal(Color.Parse(dark ? (readOnly ? "#182235" : "#1E3A5F") : (readOnly ? "#FFFFFF" : "#E8F4FF")), background);
                        foreach (var text in row.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)))
                            CheckInk(text, text.Foreground, "Register/" + dark + "/" + text.Text, failures);
                    }
                    Capture(window, "inputs-" + dark); Capture(registerWindow, "registers-" + dark);
                }
                Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
                Assert.False(vm.ModbusService.IsConnected);
            }
            finally { window.Close(); registerWindow.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RegisterDialog_InvalidInputStaysReadableAndFitsWhenThemeChanges()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var bus = new TestBuilder.Services.Modbus.ModbusService();
            var slave = new TestBuilder.Domain.Modbus.Models.PS2Model(21, bus);
            var window = new WriteRegisterDialog(slave, new() { Address = 5 });
            var failures = new List<string>();
            try
            {
                window.Show();
                // Invalid input returns before any Modbus operation.
                window.FindControl<TextBox>("ValueBox")!.Text = "не число";
                window.FindControl<Button>("SendButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                var result = window.FindControl<TextBlock>("ResultText")!;
                Assert.Equal("Ошибка: неверное значение", result.Text);
                foreach (var dark in new[] { false, true, false })
                {
                    Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                    Layout(window);
                    CheckInk(result, result.Foreground, "Register error/" + dark, failures);
                    foreach (var button in window.GetVisualDescendants().OfType<Button>())
                    {
                        var point = button.TranslatePoint(new Point(0, button.Bounds.Height), window)!.Value;
                        Assert.InRange(point.Y, 1, window.ClientSize.Height);
                        CheckButton(button, "Register dialog/" + dark, failures);
                    }
                    Capture(window, "register-dialog-" + dark);
                }
                Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
                Assert.False(bus.IsConnected);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void CheckInk(Visual visual, IBrush? brush, string name, List<string> failures)
    {
        if (brush is not ISolidColorBrush ink) return;
        var background = BackgroundAt(visual);
        var opacity = visual.GetSelfAndVisualAncestors().Aggregate(1d, (value, v) => value * v.Opacity);
        var foreground = Blend(ink.Color, background, opacity * ink.Opacity);
        var contrast = Contrast(foreground, background);
        if (contrast < 4.5) failures.Add($"{name}: {foreground} on {background}, contrast={contrast:0.00}; " +
            string.Join(" > ", visual.GetSelfAndVisualAncestors().Select(v => $"{v.GetType().Name}#{v.Name} opacity={v.Opacity}")));
    }

    internal static Color BackgroundAt(Visual visual)
    {
        var color = Colors.White;
        foreach (var v in visual.GetSelfAndVisualAncestors().Reverse())
        {
            var brush = v switch { ContentPresenter p => p.Background, Border b => b.Background, Panel p => p.Background,
                DataGridRow row => row.Background, Window w => w.Background, _ => null };
            if (brush is ISolidColorBrush solid) color = Blend(solid.Color, color, solid.Opacity * v.Opacity);
        }
        return color;
    }
    internal static Color Blend(Color front, Color back, double opacity = 1)
    {
        var alpha = front.A / 255d * opacity;
        byte Channel(byte f, byte b) => (byte)Math.Round(f * alpha + b * (1 - alpha));
        return Color.FromRgb(Channel(front.R, back.R), Channel(front.G, back.G), Channel(front.B, back.B));
    }
    internal static double Contrast(Color first, Color second)
    {
        static double Luminance(Color c)
        {
            static double Channel(byte b) { var v = b / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
            return .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        }
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    private static void Layout(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); Dispatcher.UIThread.RunJobs(); }
    private static void Capture(Window window, string name)
    {
        var folder = Environment.GetEnvironmentVariable("TFORTIS_CONTRAST_CAPTURES");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder); using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, name + ".png"));
    }
}
