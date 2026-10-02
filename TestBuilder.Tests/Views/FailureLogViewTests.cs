using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
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
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class FailureLogViewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedRun_LogsOriginalCauseAfterFailedCleanup_AndHighlightsIt(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var theme = Application.Current!.RequestedThemeVariant;
            var settings = AppSettings.Instance;
            var oldEnabled = settings.EnableFileLogging;
            var oldFolder = settings.LogFolder;
            var folder = Path.Combine(Path.GetTempPath(), $"failure-logs-{Guid.NewGuid():N}");
            settings.EnableFileLogging = true;
            settings.LogFolder = folder;
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            using var modbus = new ModbusService();
            using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
            using var view = new LogPanelView { DataContext = vm };
            var window = new Window { Width = 800, Height = 700, Content = view };
            try
            {
                var start = new StartNodeViewModel();
                var main = FailingSubtest("Проверка питания", "MissingMainValue");
                var cleanup = FailingSubtest("Аварийное отключение", "MissingCleanupValue");
                cleanup.RunOnFailure = true;
                vm.RootGraph.Nodes.Add(start);
                vm.RootGraph.Nodes.Add(main);
                vm.RootGraph.Nodes.Add(cleanup);
                vm.RootGraph.Connections.Add(new ConnectionViewModel(start.Output[0], main.Input[0]));
                window.Show();
                Assert.True(vm.RunGraphCommand.CanExecute(null));
                await vm.RunGraphCommand.ExecuteAsync(null);
                Layout(window);

                var entries = vm.TestingLogger.Entries;
                Assert.Contains(entries, e => e.Message.Contains("MissingCleanupValue"));
                var cause = entries.Last();
                Assert.True(cause.IsFailureSummary);
                Assert.Contains("Проверка питания", cause.Message);
                Assert.Contains("Check Variable Equality", cause.Message);
                Assert.Contains("MissingMainValue", cause.Message);
                Assert.DoesNotContain("MissingCleanupValue", cause.Message);
                Assert.Equal(LogLevel.Error, cause.Level);
                var fileText = File.ReadAllText(Directory.GetFiles(folder).Single());
                Assert.EndsWith(cause.Message + Environment.NewLine, fileText);

                var logView = view.GetVisualDescendants().OfType<LogEntryView>()
                    .Single(v => ReferenceEquals(v.DataContext, cause));
                var text = logView.GetVisualDescendants().OfType<SelectableTextBlock>().Single();
                Assert.Equal(FontWeight.Bold, text.FontWeight);
                Assert.Equal(Color.Parse(dark ? "#FCA5A5" : "#991B1B"), ((ISolidColorBrush)text.Foreground!).Color);
                Assert.Contains(logView.GetVisualDescendants().OfType<Border>(), b => b.BorderThickness.Left == 3 && b.Background != null);

                // Virtualized/recycled rows must return to ordinary styling for the next entry.
                logView.DataContext = new LogEntry(DateTime.Now, LogLevel.Info, "Testing", "Обычная строка");
                Layout(window);
                Assert.Equal(FontWeight.Normal, text.FontWeight);
            }
            finally
            {
                view.DataContext = null;
                window.Content = null;
                window.Close();
                LoggingService.Instance.StopFileLogForRun();
                settings.EnableFileLogging = oldEnabled;
                settings.LogFolder = oldFolder;
                Application.Current.RequestedThemeVariant = theme;
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                // Drain theme/render work before the headless platform is disposed.
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(LogLevel.Warning, "[ERROR] Selftest failed", "#DC2626")]
    [InlineData(LogLevel.Warning, "[ОШИБКА] Регистр не прочитан", "#DC2626")]
    [InlineData(LogLevel.Error, "Exception includes [OK] from earlier output", "#DC2626")]
    [InlineData(LogLevel.Info, "[OK] Проверка пройдена", "#16A34A")]
    [InlineData(LogLevel.Warning, "[ОСТАНОВ] Остановлено пользователем", "#D97706")]
    [InlineData(LogLevel.Info, "Обычная строка", null)]
    public void LogEntry_UsesErrorSeverityBeforeMessageTags(LogLevel level, string message, string? color)
        => Assert.Equal(color, new LogEntry(DateTime.Now, level, "Testing", message).HighlightColor);

    private static SubtestNodeViewModel FailingSubtest(string name, string variable)
    {
        var subtest = new SubtestNodeViewModel { Name = name };
        var start = subtest.BodyGraph.Nodes.OfType<StartNodeViewModel>().Single();
        var check = new CheckVariableEqualityNodeViewModel { VariableName = variable };
        subtest.BodyGraph.Nodes.Add(check);
        subtest.BodyGraph.Connections.Add(new ConnectionViewModel(start.Output[0], check.Input[0]));
        return subtest;
    }

    private static void Layout(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
