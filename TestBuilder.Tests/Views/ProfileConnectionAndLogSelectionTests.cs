using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.Views;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class ProfileConnectionAndLogSelectionTests
{
    [Fact]
    public async Task AutoSearchFailureKeepsTechnicalDetailsInFile_AndNeutralMessageInUi()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "port-search-" + Guid.NewGuid());
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            try
            {
                Dispatcher.UIThread.RunJobs(); vm.TestingLogger.Clear();
                Exception error;
                try { throw new TimeoutException("Write timed out at TestViewModel.cs:893"); }
                catch (Exception ex) { error = ex; }
                vm.ReportPortSearchFailure("COM-WRONG", error, automatic: true, directory);
                vm.TestingLogger.Info("Подключено к COM-RIGHT.");
                Dispatcher.UIThread.RunJobs();
                Assert.All(vm.TestingLogger.Entries, entry =>
                {
                    Assert.Equal(TestBuilder.Services.Logging.LogLevel.Info, entry.Level);
                    Assert.Null(entry.HighlightColor);
                    Assert.DoesNotContain("TimeoutException", entry.Message);
                    Assert.DoesNotContain("TestViewModel.cs", entry.Message);
                });
                Assert.Contains(vm.TestingLogger.Entries, e => e.Message.Contains("Поиск продолжается"));
                var diagnostic = File.ReadAllText(Assert.Single(Directory.GetFiles(directory)));
                Assert.Contains(error.ToString(), diagnostic);
                vm.ReportPortSearchFailure("COM-MANUAL", error, automatic: false, directory);
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(vm.TestingLogger.Entries, e => e.Level == TestBuilder.Services.Logging.LogLevel.Warning && e.Message.Contains("Проверьте подключение"));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("RTL_SW_board.json")]
    [InlineData("RTL_POE_board.json")]
    public async Task ConnectedUserCanLoadBoardProfile_AndFailedReconnectLeavesGraphAvailable(string file)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var testProfile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            var oldPort = AppSettings.Instance.StandPort;
            AppSettings.Instance.StandPort = Path.Combine(Path.GetTempPath(), "nonexistent-tfortis-test-port");
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            try
            {
                vm.IsConnected = true;
                vm.IsMonitoringActive = true;
                Assert.True(vm.CanChooseProfile);
                var source = JsonNode.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../profiles", file))))!;
                // New profiles own their port; never enumerate physical ports in this UI test.
                source["hardware"]!["PortName"] = AppSettings.Instance.StandPort;
                File.WriteAllText(testProfile, source.ToJsonString());
                var profile = GraphSerializer.ReadProfile(testProfile);
                await vm.SelectProfileAsync(profile);
                Dispatcher.UIThread.RunJobs();
                Assert.Same(profile, vm.SelectedProfile);
                Assert.NotEmpty(vm.RootGraph.Nodes);
                Assert.NotNull(vm.RootGraph.Hardware);
                Assert.False(vm.IsConnected);
                Assert.False(vm.IsMonitoringActive);
                Assert.False(vm.IsConnecting);
                Assert.True(vm.CanChooseProfile);
                Assert.Contains(vm.TestingLogger.Entries, e => e.ToString().Contains("Не удалось открыть"));
                Assert.DoesNotContain(vm.TestingLogger.Entries, e => e.ToString().Contains("Загрузка профиля"));
            }
            finally { AppSettings.Instance.StandPort = oldPort; File.Delete(testProfile); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ProfileSelectionIsBlockedDuringRunOrConnection_AndSameProfileKeepsEdits()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../profiles/RTL_SW_board.json"));
            var profile = GraphSerializer.ReadProfile(path);
            vm.IsTestRunning = true;
            await vm.SelectProfileAsync(profile);
            Assert.Null(vm.SelectedProfile);
            vm.IsTestRunning = false;
            vm.IsConnecting = true;
            await vm.SelectProfileAsync(profile);
            Assert.Null(vm.SelectedProfile);
            vm.IsConnecting = false;
            await vm.SelectProfileAsync(profile);
            var firstNode = vm.RootGraph.Nodes[0];
            vm.RootGraph.Title = "Изменённое название";
            vm.IsConnected = true;
            await vm.SelectProfileAsync(profile);
            vm.ShowSelectedProfileGraph();
            Assert.True(vm.IsConnected);
            Assert.Same(firstNode, vm.RootGraph.Nodes[0]);
            Assert.Equal("Изменённое название", vm.RootGraph.Title);
            Assert.False(vm.NewProfileCommand.CanExecute(null));
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JournalSupportsMouseRange_MultipleSelection_AndKeyboardCopy(bool modbus)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            using var logView = new LogPanelView { DataContext = vm };
            Control view = modbus ? new ModbusMonitoringView() : logView;
            var window = new Window { Width = 900, Height = 650, Content = view };
            try
            {
                vm.TestingLogger.Clear();
                vm.TestingLogger.Info("Первая строка");
                vm.TestingLogger.Info("Вторая строка");
                vm.TestingLogger.Info("Третья строка");
                window.Show();
                var list = view.FindControl<ListBox>("LogList")!;
                // The Modbus view has no VM here, so it cannot scan real ports.
                if (modbus) list.ItemsSource = vm.TestingLogger.Entries;
                Layout(window);
                ClickRow(0, RawInputModifiers.None);
                ClickRow(2, RawInputModifiers.Shift);
                Assert.Equal(3, list.SelectedItems!.Count);
                ClickRow(0, RawInputModifiers.None);
                ClickRow(2, RawInputModifiers.Control);
                Assert.Equal(2, list.SelectedItems.Count);
                var expected = string.Join(Environment.NewLine, new[] { vm.TestingLogger.Entries[0].ToString(), vm.TestingLogger.Entries[2].ToString() });
                Assert.Equal(expected, LogSelection.GetCopyText(list));
                window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
                Assert.Equal(expected, await window.Clipboard!.GetTextAsync());
                window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Meta);
                Assert.Equal(3, list.SelectedItems.Count);
                list.UnselectAll();
                Assert.Equal(string.Join(Environment.NewLine, vm.TestingLogger.Entries), LogSelection.GetCopyText(list));

                void ClickRow(int index, RawInputModifiers modifiers)
                {
                    var row = list.ContainerFromIndex(index)!;
                    var point = row.TranslatePoint(new Point(45, row.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(point, MouseButton.Left, modifiers);
                    window.MouseUp(point, MouseButton.Left, modifiers);
                    Layout(window);
                }
            }
            finally
            {
                view.DataContext = null;
                window.Content = null;
                window.Close();
                Layout(window);
            }
            return true;
        }, CancellationToken.None);
    }

    private static void Layout(Window window)
    {
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
