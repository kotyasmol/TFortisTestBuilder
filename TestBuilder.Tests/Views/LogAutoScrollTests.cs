using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class LogAutoScrollTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProfilePreparationPublishesLogsWithoutClickingLogButtons(bool initiallyHidden)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            using var view = new LogPanelView { IsVisible = !initiallyHidden };
            var window = new Window { Width = 900, Height = 500, Content = view, DataContext = vm };
            var path = Path.Combine(Path.GetTempPath(), "log-preparation-" + Guid.NewGuid() + ".json");
            try
            {
                var start = new TestBuilder.ViewModels.StepVM.StartNodeViewModel();
                var prep = new TestBuilder.ViewModels.StepVM.DelayNodeViewModel { Milliseconds = 150, RunOnlyOnFirstRun = true };
                var test = new TestBuilder.ViewModels.StepVM.DelayNodeViewModel { Milliseconds = 1 };
                var end = new TestBuilder.ViewModels.StepVM.EndNodeViewModel();
                vm.RootGraph.Clear();
                foreach (var node in new TestBuilder.ViewModels.NodifyVM.NodeViewModel[] { start, prep, test, end }) vm.Nodes.Add(node);
                vm.Connections.Add(new(start.Output[0], prep.Input[0]));
                vm.Connections.Add(new(prep.Output[0], test.Input[0]));
                vm.Connections.Add(new(test.Output[0], end.Input[0]));
                File.WriteAllText(path, TestBuilder.Services.GraphSerializer.Serialize(vm, "Log preparation"));
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var list = view.FindControl<ListBox>("LogList")!;
                var opening = vm.SelectProfileAsync(TestBuilder.Services.GraphSerializer.ReadProfile(path));
                await Task.Delay(30);
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(list.Items.Cast<LogEntry>(), e => e.Message.StartsWith("Подготовка стенда при открытии профиля:"));
                Assert.Same(vm.TestingLogger.Entries, list.ItemsSource);
                await opening;
                await Task.Run(() => vm.TestingLogger.Info("BACKGROUND LOG WITHOUT CLICK"));
                Dispatcher.UIThread.RunJobs();
                view.IsVisible = true;
                for (var i = 0; i < 6; i++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
                Assert.Contains(list.Items.Cast<LogEntry>(), e => e.Message == "BACKGROUND LOG WITHOUT CLICK");
                Assert.Equal(vm.TestingLogger.Entries.ToArray(), list.Items.Cast<LogEntry>().ToArray());
                Assert.True(vm.Nodes.OfType<TestBuilder.ViewModels.StepVM.DelayNodeViewModel>().First().HasExecutionSucceeded);
                Assert.False(vm.Nodes.OfType<TestBuilder.ViewModels.StepVM.DelayNodeViewModel>().Last().HasExecutionSucceeded);
                Assert.Contains(list.GetVisualDescendants().OfType<SelectableTextBlock>(),
                    t => t.Text?.Contains("BACKGROUND LOG WITHOUT CLICK") == true);
            }
            finally { view.DataContext = null; window.Close(); File.Delete(path); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LongRowsFollowToTheirEnd_UserCanReadHistory_AndReturnWithMouse()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            vm.TestingLogger.Entries.Clear();
            using var view = new LogPanelView { DataContext = vm };
            var window = new Window { Width = 900, Height = 500, Content = view };
            try
            {
                window.Show(); Layout();
                var list = view.FindControl<ListBox>("LogList")!;
                var viewer = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
                var toggle = view.FindControl<ToggleButton>("FollowLogsToggle")!;
                for (var i = 0; i < 1000; i++) Add($"Line {i}");
                Add(string.Join('\n', Enumerable.Range(0, 150).Select(i => $"HTML response line {i}")) + "\nEND OF RESPONSE");
                Layout();
                AtBottom();
                Assert.All(list.GetVisualDescendants().OfType<SelectableTextBlock>(), text => Assert.Equal(14, text.FontSize));
                Assert.True(toggle.IsChecked);

                var center = list.TranslatePoint(new Point(100, list.Bounds.Height / 2), window)!.Value;
                var liveOffset = viewer.Offset.Y;
                window.MouseWheel(center, new Vector(0, 5)); Layout();
                Assert.False(toggle.IsChecked);
                var before = viewer.Offset.Y;
                Assert.True(before > 0 && liveOffset - before < 1000,
                    $"Pausing jumped from {liveOffset} to {before}");
                Add("New row while reading history"); Layout();
                Assert.Equal(before, viewer.Offset.Y, 1);
                Assert.True(viewer.Offset.Y < viewer.Extent.Height - viewer.Viewport.Height - 4);

                var frozen = list.Items.Cast<LogEntry>().ToArray();
                // A running subtest produces enough output to trim the live buffer.
                for (var i = 0; i < 1100; i++)
                {
                    Add($"New subtest row {i}");
                    vm.TestingLogger.Entries.RemoveAt(0);
                }
                Layout();
                Assert.Equal(frozen, list.Items.Cast<LogEntry>().ToArray());
                Assert.Equal(before, viewer.Offset.Y, 1);
                // Clearing the source for another run must not discard the reading snapshot.
                vm.TestingLogger.Entries.Clear(); Add("Next run"); Layout();
                Assert.Equal(frozen, list.Items.Cast<LogEntry>().ToArray());
                window.MouseWheel(center, new Vector(0, -1)); Layout();
                Assert.False(toggle.IsChecked);

                Click(view.FindControl<Button>("LogToBottomButton")!);
                Assert.True(toggle.IsChecked);
                Assert.Equal(vm.TestingLogger.Entries.ToArray(), list.Items.Cast<LogEntry>().ToArray());
                AtBottom();
                // Trimming the oldest entries and resizing must not disable following.
                for (var i = 0; i < 40; i++) { vm.TestingLogger.Entries.RemoveAt(0); Add($"Next {i}"); }
                window.Width = 760; Layout();
                Assert.True(toggle.IsChecked); AtBottom();
                Click(toggle);
                Assert.False(toggle.IsChecked);
                Add("Paused explicitly"); Layout();
                Assert.False(toggle.IsChecked);
                Click(toggle); AtBottom();
                window.CaptureRenderedFrame()!.Save("/private/tmp/tfortis-log-scroll.png");

                void AtBottom() => Assert.True(viewer.Extent.Height - viewer.Viewport.Height - viewer.Offset.Y <= 4,
                    $"Offset {viewer.Offset.Y}, extent {viewer.Extent.Height}, viewport {viewer.Viewport.Height}");
            }
            finally { view.DataContext = null; window.Close(); vm.TestingLogger.Entries.Clear(); }

            void Add(string message) => vm.TestingLogger.Entries.Add(new LogEntry(DateTime.Now, LogLevel.Info, "Testing", message));
            void Layout()
            {
                for (var i = 0; i < 6; i++)
                {
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                }
            }
            void Click(Control control)
            {
                var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout();
            }
        }, CancellationToken.None);
    }
}
