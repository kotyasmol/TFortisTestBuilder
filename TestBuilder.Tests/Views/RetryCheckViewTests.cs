using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Help;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class RetryCheckViewTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task DedicatedBlockShowsAttemptsAndOpensItsChecksWithoutLoopSettings(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
            vm.IsOperatorView = false; vm.Nodes.Clear(); vm.AddNodeAtLocation("Retry Check", new Point(100, 80));
            var node = Assert.Single(vm.Nodes.OfType<RetryCheckNodeViewModel>());
            Assert.Equal("Повтор проверки", node.DisplayTitle);
            Assert.NotNull(UserGuide.ForNode(typeof(RetryCheckNodeViewModel)));
            var oldTheme = Application.Current!.RequestedThemeVariant;
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            using var view = new GraphEditorView { DataContext = vm };
            var window = new Window { Width = 1000, Height = 780, Content = view };
            try
            {
                var before = GraphSerializer.Serialize(vm, "Повтор");
                window.Show(); Layout(window);
                Assert.Equal(before, GraphSerializer.Serialize(vm, "Повтор"));
                var texts = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToArray();
                Assert.Contains("Всего попыток, включая первую", texts);
                Assert.DoesNotContain("Число повторов / имя счётчика", texts);
                var fields = view.GetVisualDescendants().OfType<TextBox>().Where(t => ReferenceEquals(t.DataContext, node)).ToArray();
                fields.Single(t => t.Text == "2").Text = "3";
                fields.Single(t => t.Text == "1000").Text = "500"; Layout(window);
                Assert.Equal(3, node.MaxAttempts); Assert.Equal(500, node.DelayMs);
                using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(Path.GetTempPath(), $"tfortis-retry-{dark}.png"));
                var button = view.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Изменить проверки"));
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout(window);
                Assert.Same(node.BodyGraph, vm.CurrentGraph);
                Assert.Contains(node.BodyGraph.Nodes, n => n.DisplayTitle == "Начало проверки");
                vm.GoBackGraphCommand.Execute(null); Layout(window); Assert.Same(vm.RootGraph, vm.CurrentGraph);
                GraphSerializer.Deserialize(GraphSerializer.Serialize(vm, "Повтор"), vm);
                Assert.Equal(3, Assert.Single(vm.Nodes.OfType<RetryCheckNodeViewModel>()).MaxAttempts);
            }
            finally { window.Close(); Application.Current.RequestedThemeVariant = oldTheme; }
        }, CancellationToken.None);
    }

    private static void Layout(Window window)
    { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); }
}
