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
                window.MouseWheel(center, new Vector(0, 5)); Layout();
                Assert.False(toggle.IsChecked);
                var before = viewer.Offset.Y;
                Add("New row while reading history"); Layout();
                Assert.Equal(before, viewer.Offset.Y, 1);
                Assert.True(viewer.Offset.Y < viewer.Extent.Height - viewer.Viewport.Height - 4);

                Click(view.FindControl<Button>("LogToBottomButton")!);
                Assert.True(toggle.IsChecked);
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
