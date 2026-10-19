using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class RunDurationTests
{
    [Theory]
    [InlineData(0.1, "меньше 1 с")]
    [InlineData(59, "59 с")]
    [InlineData(137, "2 мин 17 с")]
    [InlineData(3909, "1 ч 05 мин 09 с")]
    [InlineData(90001, "25 ч 00 мин 01 с")]
    public void DurationIsReadable_AndHoursDoNotWrapAtMidnight(double seconds, string expected)
        => Assert.Equal(expected, StationDashboardViewModel.FormatDuration(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancelled")]
    [InlineData("compileError")]
    [InlineData("partial")]
    [InlineData("pause")]
    public async Task FinishedRun_ShowsAndLogsElapsed_IncludingPauseAndCleanup(string outcome)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var settings = AppSettings.Instance;
            var oldEnabled = settings.EnableFileLogging; var oldFolder = settings.LogFolder;
            var folder = Path.Combine(Path.GetTempPath(), "run-duration-" + Guid.NewGuid().ToString("N"));
            settings.EnableFileLogging = true; settings.LogFolder = folder;
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            var view = new StationDashboardView { DataContext = vm };
            var window = new Window { Width = 1300, Height = 800, Content = view };
            try
            {
                var start = new StartNodeViewModel();
                var delay = new DelayNodeViewModel { Milliseconds = outcome is "cancelled" or "pause" ? 1000 : 60 };
                NodeViewModel last = outcome == "failure" ? new CheckVariableEqualityNodeViewModel { VariableName = "MissingValue" } : new EndNodeViewModel();
                if (outcome != "compileError") vm.Nodes.Add(start);
                vm.Nodes.Add(delay); vm.Nodes.Add(last);
                if (outcome != "compileError") vm.Connections.Add(new(start.Output[0], delay.Input[0]));
                vm.Connections.Add(new(delay.Output[0], last.Input[0]));
                var cleanup = new SubtestNodeViewModel { Name = "Завершающие действия", RunOnFailure = true };
                var cleanupDelay = new DelayNodeViewModel { Milliseconds = 90 };
                cleanup.BodyGraph.Nodes.Add(cleanupDelay);
                cleanup.BodyGraph.Connections.Add(new(cleanup.BodyGraph.Nodes.OfType<StartNodeViewModel>().Single().Output[0], cleanupDelay.Input[0]));
                cleanup.BodyGraph.Connections.Add(new(cleanupDelay.Output[0], cleanup.BodyGraph.Nodes.OfType<EndNodeViewModel>().Single().Input[0]));
                vm.Nodes.Add(cleanup);
                if (outcome == "partial") vm.ConfigureNodeRun(delay, true);
                window.Show(); Dispatcher.UIThread.RunJobs();
                var run = vm.RunGraphCommand.ExecuteAsync(null);
                if (outcome is "cancelled" or "pause")
                {
                    Assert.True(vm.IsTestRunning);
                    Assert.False(vm.Station.HasDuration);
                    if (outcome == "cancelled")
                    {
                        await Task.Delay(60);
                        vm.StopTestCommand.Execute(null);
                    }
                    else
                    {
                        vm.PauseTestCommand.Execute(null);
                        await Task.Delay(1200);
                        Assert.False(run.IsCompleted);
                        vm.ResumeTestCommand.Execute(null);
                    }
                }
                await run;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.True(vm.Station.HasResult);
                Assert.True(vm.Station.HasDuration);
                Assert.Contains(vm.Station.DurationText, vm.StatusMessage);
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == vm.Station.DurationText);
                Assert.Single(vm.TestingLogger.Entries.Where(e => e.Message == vm.Station.DurationText));
                Assert.Contains(vm.Station.DurationText, File.ReadAllText(Directory.GetFiles(folder).Single()));
                var elapsed = vm.Station.Elapsed!.Value;
                if (outcome == "pause") Assert.True(elapsed >= TimeSpan.FromMilliseconds(1200));
                else if (outcome is "failure" or "cancelled")
                {
                    Assert.True(cleanupDelay.HasExecutionSucceeded);
                    Assert.True(elapsed >= TimeSpan.FromMilliseconds(145), $"Cleanup missing from elapsed: {elapsed}");
                }
                else if (outcome != "compileError") Assert.True(elapsed >= TimeSpan.FromMilliseconds(55));
                if (outcome == "failure") Assert.True(vm.TestingLogger.Entries.Last().IsFailureSummary);
                var snapshot = vm.Station.Elapsed;
                await Task.Delay(20);
                Assert.Equal(snapshot, vm.Station.Elapsed);
                vm.Station.BeginRun(vm.RootGraph);
                Assert.False(vm.Station.HasDuration); Assert.Empty(vm.Station.DurationText);
            }
            finally
            {
                view.DataContext = null; window.Content = null; window.Close();
                LoggingService.Instance.StopFileLogForRun();
                settings.EnableFileLogging = oldEnabled; settings.LogFolder = oldFolder;
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
            }
            return true;
        }, CancellationToken.None);
    }
}
