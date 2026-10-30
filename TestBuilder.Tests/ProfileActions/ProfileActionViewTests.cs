using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
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
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task FailedAction_ShowsShortStatusAndDetailTooltip_WithoutGrowingButton(bool operatorMode, bool light)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(async () =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            ProfileActionTests.LoadEmpty(vm);
            vm.IsOperatorView = operatorMode; vm.IsQuickActionsCollapsed = false;
            var message = "Принтер в режиме «Работать автономно». Включите принтер и проверьте USB-подключение.\n" +
                string.Join("\n", Enumerable.Repeat("Дополнительные сведения об ошибке печати.", 20));
            var calls = 0;
            var action = new ProfileActionViewModel(new() { Name = "Повторить этикетку" },
                (_, _, _, _) => { calls++; return Task.FromResult(new ProfileActionResult(false, message)); },
                () => true, () => { });
            vm.QuickActions.Add(action);
            var bar = new ProfileActionBar { DataContext = vm, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            var window = new Window { Width = 800, Height = 500, Content = bar,
                RequestedThemeVariant = light ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark };
            try
            {
                window.Show(); Layout(window);
                var run = bar.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, action.RunCommand));
                var size = run.Bounds.Size;
                Click(window, run); await action.RunCommand.ExecutionTask!; Layout(window);
                Assert.Equal(size, run.Bounds.Size); Assert.Equal("Ошибка", action.StateText);
                Assert.Equal(message, action.DetailText);
                Assert.Equal(message, ToolTip.GetTip(run));
                Assert.Contains(run.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Ошибка");
                Assert.DoesNotContain(run.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == message);
                Capture(window, $"action-error-status-{operatorMode}-{light}.png");
                Assert.Equal(1, calls);
                Assert.True(action.RunCommand.CanExecute(null));
            }
            finally { window.Close(); }
            return true;
        }, default);
    }

    [Fact]
    public async Task SystemColor_CanBeSelectedAndReopened_AndFollowsThemeWithoutChangingCustomColors()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            ProfileActionDefinition saved;
            var editor = new ProfileActionEditorWindow();
            try
            {
                editor.Show(); Layout(editor);
                var system = editor.FindControl<StackPanel>("ColorPalette")!.Children.OfType<Button>()
                    .Single(b => Equals(b.Tag, ProfileActionDefinition.SystemColor));
                Click(editor, system);
                Assert.Equal("Системный", editor.FindControl<TextBox>("ColorInput")!.Text);
                Assert.True(system.Classes.Contains("selected"));
                saved = editor.BuildResult();
                Assert.Equal(ProfileActionDefinition.SystemColor, saved.Color);
            }
            finally { editor.Close(); }

            var reopened = new ProfileActionEditorWindow(saved, null);
            try
            {
                reopened.Show(); Layout(reopened);
                Assert.Equal("Системный", reopened.FindControl<TextBox>("ColorInput")!.Text);
                Assert.Equal("Системный — выбран", AutomationProperties.GetName(reopened.FindControl<StackPanel>("ColorPalette")!
                    .Children.OfType<Button>().Single(b => b.Classes.Contains("selected"))));
                Capture(reopened, "action-system-color-editor.png");
            }
            finally { reopened.Close(); }

            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            ProfileActionTests.LoadEmpty(vm);
            saved.Name = "Системная кнопка"; saved.Display = ProfileActionDisplay.Toggle;
            vm.RootGraph.QuickActions.Add(saved);
            vm.RootGraph.QuickActions.Add(new() { Name = "Цветная кнопка", Color = "#15803D" });
            vm.IsQuickActionsCollapsed = false;
            var bar = new ProfileActionBar { DataContext = vm };
            var window = new Window { Width = 420, Height = 400, Content = bar };
            try
            {
                window.Show(); Layout(window);
                var buttons = bar.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("profileAction")).ToArray();
                var system = buttons[0]; var custom = buttons[1];
                Color? previous = null;
                foreach (var theme in new[] { Avalonia.Styling.ThemeVariant.Light, Avalonia.Styling.ThemeVariant.Dark })
                {
                    window.RequestedThemeVariant = theme; Layout(window);
                    var background = Assert.IsAssignableFrom<ISolidColorBrush>(system.Background).Color;
                    Assert.NotEqual(previous, background); previous = background;
                    Assert.Equal(theme == Avalonia.Styling.ThemeVariant.Light ? Colors.Black : Colors.White,
                        Assert.IsAssignableFrom<ISolidColorBrush>(system.Foreground).Color);
                    Assert.Equal(system.Foreground, system.GetVisualDescendants().OfType<Ellipse>().Single().Fill);
                    foreach (var button in buttons)
                    {
                        var expected = button == system ? background : Color.Parse("#15803D");
                        Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
                        var point = button.TranslatePoint(new Point(10, 10), window)!.Value;
                        window.MouseMove(point); Layout(window);
                        Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
                        foreach (var label in button.GetVisualDescendants().OfType<TextBlock>())
                        {
                            Assert.True(label.IsEffectivelyVisible);
                            Assert.Equal(button.Foreground, label.Foreground);
                        }
                    }
                    window.MouseMove(new Point(400, 380)); Layout(window);
                    Capture(window, $"action-system-color-{theme}.png");
                }
                Assert.Equal(ProfileActionDefinition.SystemColor, saved.Color);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, false, ProfileActionDisplay.Toggle)]
    [InlineData(false, true, ProfileActionDisplay.Toggle)]
    [InlineData(true, false, ProfileActionDisplay.Toggle)]
    [InlineData(true, true, ProfileActionDisplay.Toggle)]
    [InlineData(false, false, ProfileActionDisplay.Button)]
    [InlineData(true, true, ProfileActionDisplay.Progress)]
    public async Task ActionStates_DoNotMoveButtonContentOrNeighbours(bool operatorMode, bool light, ProfileActionDisplay display)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            ProfileActionTests.LoadEmpty(vm);
            vm.IsOperatorView = operatorMode;
            vm.IsQuickActionsCollapsed = false;
            TaskCompletionSource<ProfileActionResult>? gate = null;
            var action = new ProfileActionViewModel(new() { Name = "Внешнее питание стенда", Display = display },
                (_, _, _, token) =>
                {
                    gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    return gate.Task.WaitAsync(token);
                }, () => true, () => { });
            vm.QuickActions.Add(action);
            vm.QuickActions.Add(new(new() { Name = "Следующая кнопка" },
                (_, _, _, _) => Task.FromResult(new ProfileActionResult(true, "OK")), () => true, () => { }));
            var bar = new ProfileActionBar { DataContext = vm };
            var window = new Window { Width = 420, Height = 420, Content = bar,
                RequestedThemeVariant = light ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark };
            try
            {
                window.Show(); Layout(window);
                var buttons = bar.GetVisualDescendants().OfType<Button>().ToArray();
                var run = buttons.Single(b => ReferenceEquals(b.Command, action.RunCommand));
                var next = buttons.Single(b => ReferenceEquals(b.Command, vm.QuickActions[1].RunCommand));
                var title = run.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == action.Name);
                var thumb = run.GetVisualDescendants().OfType<Ellipse>().SingleOrDefault();
                var slide = thumb?.RenderTransform as TranslateTransform;
                Rect Position(Control control) => new(control.TranslatePoint(default, bar)!.Value, control.Bounds.Size);
                var expected = (Position(run), Position(title), Position(next));
                void AssertStable()
                {
                    Layout(window);
                    Assert.Equal(expected, (Position(run), Position(title), Position(next)));
                    if (display == ProfileActionDisplay.Toggle)
                        Assert.DoesNotContain(run.GetVisualDescendants().OfType<ActionActivityIndicator>(), c => c.IsEffectivelyVisible);
                }

                var point = run.TranslatePoint(new Point(run.Bounds.Width / 2, run.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left); AssertStable();
                window.MouseUp(point, MouseButton.Left);
                var first = action.RunCommand.ExecutionTask!;
                Assert.True(action.IsRunning); AssertStable();
                Capture(window, $"action-{display}-{operatorMode}-{light}-running.png");
                gate!.SetResult(new(true, "OK")); await first;
                AssertStable();
                if (action.IsToggle)
                {
                    Assert.True(action.IsOn);
                    Assert.NotNull(slide);
                    await Task.Delay(450);
                    AssertStable();
                    Assert.Equal(14, slide.X);
                    Capture(window, $"action-{display}-{operatorMode}-{light}-on.png");
                }
                var second = action.RunCommand.ExecuteAsync(null); AssertStable();
                gate!.SetResult(new(true, "OK")); await second; AssertStable();
                if (action.IsToggle)
                {
                    Assert.False(action.IsOn);
                    await Task.Delay(450); AssertStable();
                    Assert.Equal(0, slide!.X);
                }
                var failed = action.RunCommand.ExecuteAsync(null);
                gate!.SetResult(new(false, "Длинное описание ошибки подключения к оборудованию\nДополнительные сведения"));
                await failed; AssertStable();
                var cancelled = action.RunCommand.ExecuteAsync(null); AssertStable();
                var stop = buttons.Single(b => ReferenceEquals(b.Command, action.CancelCommand));
                Click(window, stop); await cancelled.WaitAsync(TimeSpan.FromSeconds(3)); AssertStable();
                Assert.False(action.IsStateKnown);
                Capture(window, $"action-{display}-{operatorMode}-{light}-stopped.png");
            }
            finally { action.Cancel(); window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task GraphPanel_CollapsesInsideCanvas_EngineerCanEdit_OperatorOnlyRunsSameButton()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            ProfileActionTests.LoadEmpty(vm);
            vm.RootGraph.QuickActions.Add(new() { Name = "Питание", Display = ProfileActionDisplay.Toggle });
            vm.RootGraph.QuickActions.Add(new() { Name = "Проверка", Display = ProfileActionDisplay.Progress });
            using var screen = new TestView { DataContext = vm };
            var graph = screen.FindControl<GraphEditorView>("GraphEditor")!;
            var bar = graph.FindControl<ProfileActionBar>("GraphQuickActions")!;
            var workspace = graph.FindControl<Nodify.NodifyEditor>("Editor")!;
            var window = new Window { Width = 1400, Height = 850, Content = screen };
            try
            {
                window.Show(); Layout(window);
                var handle = bar.FindControl<Button>("QuickActionsHandle")!;
                var panel = bar.FindControl<Border>("QuickActionsPanel")!;
                Assert.True(vm.IsQuickActionsCollapsed);
                Assert.False(panel.IsEffectivelyVisible);
                Assert.True(handle.IsEffectivelyVisible);
                Assert.Equal(24, bar.Bounds.Width);
                var paletteHandle = graph.GetVisualDescendants().OfType<Button>()
                    .Single(b => AutomationProperties.GetName(b) == "Свернуть или развернуть палитру");
                var sidebar = screen.FindControl<RoundedPanel>("ProfileSidebar")!;
                var logs = screen.FindControl<LogPanelView>("LogPanel")!;
                Assert.True(bar.TranslatePoint(default, screen)!.Value.X >= sidebar.Bounds.Right);
                Assert.Equal(paletteHandle.TranslatePoint(default, graph)!.Value.Y, handle.TranslatePoint(default, graph)!.Value.Y, 1);
                var graphHeader = graph.FindControl<WrapPanel>("GraphActions")!;
                Assert.True(bar.TranslatePoint(default, graph)!.Value.Y >= graphHeader.TranslatePoint(default, graph)!.Value.Y + graphHeader.Bounds.Height);
                var sidebarBounds = sidebar.Bounds;
                var logBounds = logs.Bounds;
                Click(window, handle);
                Assert.False(vm.IsQuickActionsCollapsed);
                Assert.True(panel.IsEffectivelyVisible);
                Assert.Equal(304, bar.Bounds.Width);
                Assert.True(workspace.Bounds.X >= bar.Bounds.Right);
                Assert.Equal(sidebarBounds, sidebar.Bounds);
                Assert.Equal(logBounds, logs.Bounds);
                var buttons = bar.GetVisualDescendants().OfType<Button>().ToArray();
                var add = buttons.Single(b => AutomationProperties.GetName(b) == "Добавить кнопку в профиль");
                Assert.True(add.IsEffectivelyVisible);
                Assert.Equal(2, buttons.Count(b => AutomationProperties.GetName(b) == "Изменить кнопку" && b.IsEffectivelyVisible));
                var edit = buttons.First(b => AutomationProperties.GetName(b) == "Изменить кнопку");
                var delete = buttons.First(b => AutomationProperties.GetName(b) == "Удалить кнопку");
                Assert.Equal(edit.Bounds.Size, delete.Bounds.Size);
                Assert.Equal(new Size(20, 20), Assert.IsType<Viewbox>(edit.Content).Bounds.Size);
                Assert.Equal(new Size(20, 20), Assert.IsType<Viewbox>(delete.Content).Bounds.Size);
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
                vm.IsOperatorView = false; Layout(window); Capture(window, "action-panel-in-graph.png");
                vm.IsOperatorView = true; vm.RootGraph.QuickActions.Clear(); Layout(window);
                Assert.False(handle.IsEffectivelyVisible);
                Assert.Equal(0, bar.Bounds.Width);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DeleteButton_RequiresConfirmation_CancelEscapeAndClosePreserveGraphs_AndUndoRestoresOrder()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            ProfileActionTests.LoadEmpty(vm);
            var definitions = new[] {
                new ProfileActionDefinition { Name = "AC1" },
                new ProfileActionDefinition { Name = "UPS / АКБ", Display = ProfileActionDisplay.Toggle },
                new ProfileActionDefinition { Name = "Нагреватель" }
            };
            foreach (var definition in definitions) vm.RootGraph.QuickActions.Add(definition);
            vm.IsQuickActionsCollapsed = false;
            var bar = new ProfileActionBar { DataContext = vm };
            var window = new Window { Width = 420, Height = 420, Content = bar };
            DeleteProfileActionDialog? dialog = null;
            vm.ConfirmDeleteQuickActionAsync = name =>
            {
                dialog = new DeleteProfileActionDialog(name);
                return dialog.ShowDialog<bool>(window);
            };
            try
            {
                window.Show(); Layout(window);
                void RequestDelete()
                {
                    var delete = bar.GetVisualDescendants().OfType<Button>().Single(b =>
                        AutomationProperties.GetName(b) == "Удалить кнопку" && ReferenceEquals(b.DataContext, vm.QuickActions[1]));
                    Click(window, delete);
                    Layout(dialog!);
                    Assert.Contains(dialog!.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("UPS / АКБ") == true);
                    Assert.Equal(definitions, vm.RootGraph.QuickActions);
                }
                foreach (var cancellation in new[] { "button", "escape", "close" })
                {
                    RequestDelete();
                    if (cancellation == "button")
                        Click(dialog!, dialog!.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Отмена")));
                    else if (cancellation == "escape") dialog!.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                    else dialog!.Close();
                    await vm.DeleteQuickActionCommand.ExecutionTask!;
                    Layout(window);
                    Assert.Equal(definitions, vm.RootGraph.QuickActions);
                }
                RequestDelete();
                Capture(dialog!, "delete-action-confirmation.png");
                Click(dialog!, dialog!.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Удалить")));
                await vm.DeleteQuickActionCommand.ExecutionTask!;
                Assert.Equal(new[] { definitions[0], definitions[2] }, vm.RootGraph.QuickActions);
                vm.UndoCommand.Execute(null);
                Assert.Equal(definitions, vm.RootGraph.QuickActions);

                // Recheck edit availability after the asynchronous confirmation.
                var confirmation = new TaskCompletionSource<bool>();
                vm.ConfirmDeleteQuickActionAsync = _ => confirmation.Task;
                var pending = vm.DeleteQuickActionCommand.ExecuteAsync(vm.QuickActions[1]);
                vm.IsTestRunning = true;
                confirmation.SetResult(true);
                await pending;
                Assert.Equal(definitions, vm.RootGraph.QuickActions);
                vm.IsTestRunning = false;
            }
            finally { dialog?.Close(); window.Close(); }
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
