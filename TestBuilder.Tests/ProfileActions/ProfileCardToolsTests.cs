using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Models;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Views;
using TestBuilder.ViewModels;
using TestBuilder.Views;

namespace TestBuilder.Tests.ProfileActions;

[Collection("Graph editor UI")]
public class ProfileCardToolsTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EmbeddedTools_AreInsideColouredFaces_AndEditingNeverRunsAnAction(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
            ProfileActionTests.LoadEmpty(vm); vm.IsQuickActionsCollapsed = false;
            foreach (var color in new[] { "system", "#2563EB", "#D97706" })
            {
                vm.RootGraph.QuickActions.Add(new() { Name = "Питание " + color, Color = color, Display = ProfileActionDisplay.Toggle });
                var indicator = RegisterIndicatorTests.Indicator(); indicator.Color = color; vm.RootGraph.RegisterIndicators.Add(indicator);
            }
            var actions = new ProfileActionBar { DataContext = vm };
            var registers = new RegisterIndicatorsView { DataContext = vm };
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            content.Children.Add(actions); Grid.SetColumn(registers, 1); content.Children.Add(registers);
            var window = new Window { Content = content, Width = 1150, Height = 520,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            try
            {
                window.Show(); Layout(window);
                var faces = actions.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("profileAction")).ToArray();
                var cards = registers.GetVisualDescendants().OfType<ContentControl>().Where(c => c.Classes.Contains("registerIndicator")).ToArray();
                var actionTools = actions.GetVisualDescendants().OfType<Button>().Where(IsEditOrDelete).ToArray();
                var registerTools = registers.GetVisualDescendants().OfType<Button>().Where(IsEditOrDelete).ToArray();
                Assert.Equal(6, actionTools.Length); Assert.Equal(6, registerTools.Length);
                foreach (var tool in actionTools)
                {
                    var face = faces.Single(f => ReferenceEquals(f.DataContext, tool.DataContext));
                    Inside(tool, face); AssertReadable(tool);
                    Assert.Equal(28, tool.Bounds.Height);
                    window.MouseMove(tool.TranslatePoint(new Point(14, 14), window)!.Value); Layout(window);
                    AssertReadable(tool);
                    Assert.DoesNotContain(face, tool.GetVisualAncestors()); // sibling hit target, not a nested run button
                }
                foreach (var scale in new[] { 50d, 100d, 150d })
                {
                    vm.RegisterIndicatorsZoom = scale; Layout(window);
                    foreach (var tool in registerTools)
                    {
                        var face = cards.Single(c => ReferenceEquals(c.DataContext, tool.DataContext));
                        Inside(tool, face); AssertReadable(tool);
                    }
                }
                vm.RegisterIndicatorsZoom = 100; Layout(window); Capture(window, $"embedded-tools-{dark}.png");
                var action = vm.QuickActions[0];
                var edit = actionTools.Single(b => ReferenceEquals(b.DataContext, action) && AutomationProperties.GetName(b) == "Изменить кнопку");
                Assert.Same(vm.EditQuickActionCommand, edit.Command); Assert.Same(action, edit.CommandParameter);
                Click(window, edit);
                Assert.Null(action.RunCommand.ExecutionTask);
                Assert.NotNull(vm.EditQuickActionCommand.ExecutionTask);
                await vm.EditQuickActionCommand.ExecutionTask; Layout(window);
                Assert.Null(action.RunCommand.ExecutionTask);
                var requested = false;
                vm.ConfirmDeleteQuickActionAsync = _ => { requested = true; return Task.FromResult(false); };
                var delete = actionTools.Single(b => ReferenceEquals(b.DataContext, action) && AutomationProperties.GetName(b) == "Удалить кнопку");
                Click(window, delete); await vm.DeleteQuickActionCommand.ExecutionTask!;
                Assert.True(requested); Assert.Equal(3, vm.QuickActions.Count); Assert.Null(action.RunCommand.ExecutionTask);
                vm.IsTestRunning = true; Layout(window);
                Assert.True(action.RunCommand.CanExecute(null)); Assert.False(edit.IsEffectivelyEnabled);
                Click(window, edit); Click(window, delete);
                Assert.Null(action.RunCommand.ExecutionTask); Assert.Empty(window.OwnedWindows);
                vm.IsTestRunning = false; Layout(window);
                Click(window, faces[0]); await action.RunCommand.ExecutionTask!;
                Assert.True(action.IsOn);
                vm.RegisterIndicatorsZoom = 50; Layout(window);
                var registerEdit = registerTools.First(b => AutomationProperties.GetName(b) == "Изменить индикатор");
                Assert.Same(vm.EditRegisterIndicatorCommand, registerEdit.Command); Assert.Same(vm.RegisterIndicators[0], registerEdit.CommandParameter);
                Click(window, registerEdit);
                Assert.NotNull(vm.EditRegisterIndicatorCommand.ExecutionTask);
                await vm.EditRegisterIndicatorCommand.ExecutionTask; Layout(window);
                vm.IsOperatorView = true; Layout(window);
                Assert.All(actionTools.Concat(registerTools), b => Assert.False(b.IsEffectivelyVisible));
                var formerToolPoint = faces[0].TranslatePoint(new Point(faces[0].Bounds.Width - 20, faces[0].Bounds.Height / 2), window)!.Value;
                window.MouseDown(formerToolPoint, MouseButton.Left); window.MouseUp(formerToolPoint, MouseButton.Left);
                await action.RunCommand.ExecutionTask!; Assert.False(action.IsOn);
            }
            finally
            {
                vm.IsTestRunning = false;
                foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    private static bool IsEditOrDelete(Button button) => AutomationProperties.GetName(button) is
        "Изменить кнопку" or "Удалить кнопку" or "Изменить индикатор" or "Удалить индикатор";
    private static void AssertReadable(Button tool)
    {
        var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(tool.Foreground).Color;
        var background = Assert.IsAssignableFrom<ISolidColorBrush>(tool.Background).Color;
        Assert.Equal(255, background.A); // The icon remains readable on any profile color.
        static double Luminance(Color color)
        {
            static double Channel(byte value) => value / 255d <= .04045
                ? value / 255d / 12.92 : Math.Pow((value / 255d + .055) / 1.055, 2.4);
            return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        }
        var a = Luminance(foreground); var b = Luminance(background);
        Assert.True((Math.Max(a, b) + .05) / (Math.Min(a, b) + .05) >= 4.5);
    }
    private static void Inside(Control tool, Control face)
    {
        var point = tool.TranslatePoint(default, face)!.Value;
        Assert.True(point.X >= 1 && point.Y >= 1 && point.X + tool.Bounds.Width <= face.Bounds.Width - 1 &&
            point.Y + tool.Bounds.Height <= face.Bounds.Height - 1, $"Tool {point} {tool.Bounds.Size}; face {face.Bounds}");
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
