using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using TestBuilder.Services;
using TestBuilder.Tests.SerializationTests;
using TestBuilder.ViewModels;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class WorkspacePersistenceTests
{
    [Fact]
    public async Task RealSplittersPanelsAndFilter_AutosaveAndRestoreAfterRecreatingWindow()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var files = new ProfileCopyTests.ProfileFolder();
            files.Write("switch.json", ProfileCopyTests.SimpleJson);
            files.Write("board.json", ProfileCopyTests.SimpleJson.Replace("Модель А", "Плата Б").Replace("\"nodes\":", "\"productKind\":\"Board\",\"nodes\":"));
            var settingsPath = Path.Combine(files.Path, "workspace.settings");
            var oldMode = AppSettings.Instance.EngineerMode;
            AppSettings.Instance.EngineerMode = true;
            var settings = new AppSettings();
            var saves = 0;
            var first = new MainWindow();
            MainWindow? second = null;
            try
            {
                first.EnableWorkspacePersistence(settings, () => { File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings)); saves++; });
                first.Show(); Layout(first);
                first.Width = 1320; first.Height = 840; Layout(first);
                var vm = (MainWindowViewModel)first.DataContext!;
                vm.TestVM.ProductFilter = "Платы";
                Assert.Single(vm.TestVM.EngineerProfileGroups).IsExpanded = true;
                Assert.Single(vm.TestVM.StationProfileGroups).IsExpanded = true;
                vm.TestVM.IsPaletteCollapsed = true;
                vm.TestVM.IsProfilesCollapsed = true;
                vm.TestVM.AvailableNodeCategories[0].IsExpanded = false;
                vm.SelfTestPageVM.Categories[0].IsExpanded = false;
                var network = first.FindControl<NetworkSetupView>("NetworkSetupViewControl")!;
                network.FindControl<Expander>("IdentifyCableSection")!.IsExpanded = true;
                Layout(first);
                var editor = first.FindControl<TestView>("TestViewControl")!;
                var grid = editor.FindControl<Grid>("EditorLogLayout")!;
                var splitter = editor.FindControl<GridSplitter>("LogHeightSplitter")!;
                Drag(first, splitter, new Vector(0, -140));
                var logHeight = grid.RowDefinitions[2].ActualHeight;
                Assert.InRange(logHeight, 280, 320);
                Assert.Equal(logHeight, settings.Workspace.LogHeight, 1);

                vm.TestVM.SelectedProfile = Assert.Single(vm.TestVM.Profiles);
                vm.SettingsVM.IsEngineerMode = false;
                Layout(first);
                var station = first.FindControl<StationDashboardView>("StationViewControl")!;
                var boardGrid = station.FindControl<Grid>("BoardWorkspace")!;
                Drag(first, station.FindControl<GridSplitter>("BoardStagesSplitter")!, new Vector(-80, 0));
                var stageWidth = boardGrid.ColumnDefinitions[2].ActualWidth;
                Assert.InRange(stageWidth, 350, 390);
                Assert.Equal(stageWidth, settings.Workspace.BoardStagesWidth, 1);
                vm.SettingsVM.IsEngineerMode = true;
                vm.SelectPageCommand.Execute("Labels");
                await Task.Delay(750); Layout(first);
                Assert.True(saves > 0); // Written while the application is still open.
                Assert.Equal("Board", JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath))!.Workspace.ProductFilter);
                vm.TestVM.IsQuickActionsCollapsed = false;
                vm.TestVM.IsRegisterIndicatorsCollapsed = true;
                vm.TestVM.RegisterIndicatorsZoom = 70;
                first.Close(); // Flush the last change without waiting for the debounce.

                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath))!;
                second = new MainWindow();
                second.EnableWorkspacePersistence(settings, () => File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings)));
                second.Show(); Layout(second);
                var restored = (MainWindowViewModel)second.DataContext!;
                Assert.Equal("Платы", restored.TestVM.ProductFilter);
                Assert.All(restored.TestVM.Profiles, p => Assert.Equal("Board", p.ProductKind));
                Assert.True(Assert.Single(restored.TestVM.EngineerProfileGroups).IsExpanded);
                Assert.True(Assert.Single(restored.TestVM.StationProfileGroups).IsExpanded);
                Assert.True(restored.TestVM.IsProfilesCollapsed);
                Assert.True(restored.TestVM.IsPaletteCollapsed);
                Assert.False(restored.TestVM.IsQuickActionsCollapsed);
                Assert.True(restored.TestVM.IsRegisterIndicatorsCollapsed);
                Assert.Equal(70, restored.TestVM.RegisterIndicatorsZoom);
                Assert.False(restored.TestVM.AvailableNodeCategories[0].IsExpanded);
                Assert.False(restored.SelfTestPageVM.Categories[0].IsExpanded);
                Assert.True(second.FindControl<NetworkSetupView>("NetworkSetupViewControl")!.FindControl<Expander>("IdentifyCableSection")!.IsExpanded);
                Assert.Equal("Labels", restored.SelectedPage);
                Assert.Equal(1320, second.Width, 1); Assert.Equal(840, second.Height, 1);
                restored.SelectedPage = "Editor"; Layout(second);
                Assert.Equal(logHeight, second.FindControl<TestView>("TestViewControl")!.FindControl<Grid>("EditorLogLayout")!.RowDefinitions[2].ActualHeight, 1);
                Assert.Equal(stageWidth, settings.Workspace.BoardStagesWidth, 1);
                Assert.Null(restored.TestVM.SelectedProfile);
                Assert.False(restored.ModbusService.IsConnected);
                Assert.False(restored.TestVM.IsTestRunning);
            }
            finally { second?.Close(); first.Close(); AppSettings.Instance.EngineerMode = oldMode; AppSettings.Instance.Save(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SearchAndProductSwitching_DoNotOverwriteRememberedGroups_AndAuxiliaryEditorsAreIsolated()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var files = new ProfileCopyTests.ProfileFolder();
            files.Write("switch.json", ProfileCopyTests.SimpleJson);
            var settings = new AppSettings();
            var window = new MainWindow();
            try
            {
                window.EnableWorkspacePersistence(settings, () => { });
                var vm = ((MainWindowViewModel)window.DataContext!).TestVM;
                var group = Assert.Single(vm.EngineerProfileGroups);
                group.IsExpanded = true; group.IsExpanded = false;
                vm.ProfileSearch = "Проверка";
                Assert.True(group.IsExpanded);
                Assert.False(settings.Workspace.ExpandedSections["profiles/engineer/" + group.Key]);
                vm.ProfileSearch = "";
                Assert.False(group.IsExpanded);
                group.IsExpanded = true;
                vm.ProductFilter = "Платы"; Assert.Empty(vm.EngineerProfileGroups);
                vm.ProductFilter = "Коммутаторы";
                Assert.True(Assert.Single(vm.EngineerProfileGroups).IsExpanded);
                Assert.Equal("Switch", settings.Workspace.ProductFilter);
                using var bus = new TestBuilder.Services.Modbus.ModbusService();
                using var other = new TestViewModel(bus, new TestBuilder.Domain.Modbus.SlaveManager(bus));
                other.ProductFilter = "Платы"; other.IsProfilesCollapsed = true;
                other.IsRegisterIndicatorsCollapsed = true; other.RegisterIndicatorsZoom = 50;
                Assert.Equal("Switch", settings.Workspace.ProductFilter);
                Assert.False(settings.Workspace.ProfilesCollapsed);
                Assert.False(settings.Workspace.RegisterIndicatorsCollapsed);
                Assert.Equal(100, settings.Workspace.RegisterIndicatorsZoom);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LegacyAndInvalidSizes_DefaultSafely_AndSmallWindowKeepsBothPanesUsable()
    {
        var legacy = JsonSerializer.Deserialize<AppSettings>("{\"theme\":\"Dark\"}")!;
        Assert.Equal(160, legacy.Workspace.LogHeight);
        Assert.True(legacy.Workspace.QuickActionsCollapsed);
        Assert.False(legacy.Workspace.RegisterIndicatorsCollapsed);
        Assert.Equal(100, legacy.Workspace.RegisterIndicatorsZoom);
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var oldMode = AppSettings.Instance.EngineerMode;
            AppSettings.Instance.EngineerMode = true;
            var settings = new AppSettings { Workspace = new WorkspacePreferences
            {
                ProductFilter = "invalid", EngineerPage = "invalid", LogHeight = 2000, BoardStagesWidth = -20,
                WindowWidth = double.NaN, WindowHeight = -1, ExpandedSections = null!, RegisterIndicatorsZoom = double.NaN
            } };
            var window = new MainWindow();
            try
            {
                window.EnableWorkspacePersistence(settings, () => { });
                window.Width = 1100; window.Height = 680; window.Show(); Layout(window);
                var vm = (MainWindowViewModel)window.DataContext!;
                Assert.Equal("Все изделия", vm.TestVM.ProductFilter);
                Assert.Equal("Editor", vm.SelectedPage);
                Assert.Equal(100, vm.TestVM.RegisterIndicatorsZoom);
                var grid = window.FindControl<TestView>("TestViewControl")!.FindControl<Grid>("EditorLogLayout")!;
                Assert.True(grid.RowDefinitions[0].ActualHeight >= 160);
                Assert.True(grid.RowDefinitions[2].ActualHeight >= 100);
                Assert.True(grid.RowDefinitions[2].ActualHeight < 680);
                Assert.Equal(2000, settings.Workspace.LogHeight);
                Assert.Equal(290, settings.Workspace.BoardStagesWidth);
                var small = grid.RowDefinitions[2].ActualHeight;
                window.Height = 900; Layout(window);
                Assert.True(grid.RowDefinitions[2].ActualHeight > small);
                Assert.Equal(2000, settings.Workspace.LogHeight);
                window.WindowState = WindowState.Maximized;
                Assert.True(settings.Workspace.WindowMaximized);
                window.WindowState = WindowState.Minimized;
                Assert.True(settings.Workspace.WindowMaximized);
            }
            finally { window.Close(); AppSettings.Instance.EngineerMode = oldMode; AppSettings.Instance.Save(); }
        }, CancellationToken.None);
    }

    private static void Drag(Window window, Control splitter, Vector delta)
    {
        Assert.True(splitter.IsEffectivelyVisible);
        Assert.True(splitter.Bounds.Width > 0 && splitter.Bounds.Height > 0, splitter.Bounds.ToString());
        var point = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left); window.MouseMove(point + delta);
        window.MouseUp(point + delta, MouseButton.Left); Layout(window);
    }
    private static void Layout(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); Dispatcher.UIThread.RunJobs(); }
}
