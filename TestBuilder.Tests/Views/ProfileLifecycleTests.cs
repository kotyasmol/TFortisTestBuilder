using Avalonia.Headless;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.SerializationTests;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class ProfileLifecycleTests
{
    [Fact]
    public Task ProfileListOnlyLoadsJsonFilesInTheConfiguredFolder() => InSession(() =>
    {
        using var folder = new ProfileCopyTests.ProfileFolder();
        foreach (var subfolder in new[] { "standard", "deleted" })
        {
            Directory.CreateDirectory(Path.Combine(folder.Path, subfolder));
            folder.Write(Path.Combine(subfolder, "profile.json"), ProfileCopyTests.SimpleJson);
        }
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        Assert.Empty(vm.AllProfiles);

        var path = folder.Write("working.json", ProfileCopyTests.SimpleJson);
        vm.RefreshProfiles();
        Assert.Equal(path, Assert.Single(vm.AllProfiles).FilePath);
        Assert.Equal(path, Assert.Single(vm.Profiles).FilePath);
        Assert.Equal(path, Assert.Single(vm.StationProfiles).FilePath);
    });

    [Fact]
    public Task Undo_TracksConsoleSettingsAndBothLabelSourceFlags() => InSession(() =>
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        var console = new SerialConsoleNodeViewModel();
        // Legacy profiles could store both flags; undo must restore the exact saved values.
        var print = new PrintLabelNodeViewModel { UseLastTestLabel = true, UseManualSerialNumber = true };
        vm.Nodes.Add(console);
        vm.Nodes.Add(print);
        console.DeviceId = "uart1";
        console.ConnectOnly = true;
        print.SelectedDataSource = 0;

        vm.UndoCommand.Execute(null);
        Assert.True(print.UseLastTestLabel);
        Assert.True(print.UseManualSerialNumber);
        vm.UndoCommand.Execute(null);
        Assert.False(console.ConnectOnly);
        Assert.Equal("uart1", console.DeviceId);
        vm.UndoCommand.Execute(null);
        Assert.Equal(string.Empty, console.DeviceId);

        for (var i = 0; i < 3; i++) vm.RedoCommand.Execute(null);
        Assert.Equal("uart1", console.DeviceId);
        Assert.True(console.ConnectOnly);
        Assert.Equal(0, print.SelectedDataSource);
        Assert.False(print.UseManualSerialNumber);
        Assert.False(print.UseLastTestLabel);
    });

    [Fact]
    public Task Reload_DetachesOldGraphAndNestedNodes_ButKeepsNewNodesSubscribed() => InSession(() =>
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        var subtest = new SubtestNodeViewModel();
        var nested = new DelayNodeViewModel();
        subtest.BodyGraph.Nodes.Add(nested);
        vm.Nodes.Add(subtest);
        var action = new TestBuilder.Models.ProfileActionDefinition();
        var actionNode = new DelayNodeViewModel();
        action.Graph.Nodes.Add(actionNode);
        vm.RootGraph.QuickActions.Add(action);
        var json = GraphSerializer.Serialize(vm, "Граф");
        var oldNodes = new NodeViewModel[] { subtest, nested, actionNode };
        var staleNotifications = 0;
        foreach (var node in oldNodes)
            node.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(NodeViewModel.IsConnected)) staleNotifications++; };

        GraphSerializer.Deserialize(json, vm);
        var loaded = Assert.Single(vm.Nodes.OfType<SubtestNodeViewModel>());
        vm.SelectedNodes.Add(loaded);
        vm.DeleteSelectedNodes();
        Assert.DoesNotContain(loaded, vm.Nodes);
        vm.UndoCommand.Execute(null);
        Assert.Contains(loaded, vm.Nodes);
        var loadedNotifications = 0;
        loaded.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(NodeViewModel.IsConnected)) loadedNotifications++; };
        var registry = SlaveRegistry.Instance;
        var connected = registry.IsConnected;
        try
        {
            registry.NotifyConnected(!connected);
            Assert.Equal(0, staleNotifications);
            Assert.Equal(1, loadedNotifications);
        }
        finally { registry.NotifyConnected(connected); }
    });

    [Fact]
    public Task ConcurrentNodeCreation_CanSubscribeAndDisposeInParallel() => InSession(() =>
    {
        Parallel.For(0, 512, _ =>
        {
            using var node = new DelayNodeViewModel();
        });
    });

    [Fact]
    public Task SwitchingModes_KeepsFinishedVerdictAndElapsedTime() => InSession(() =>
    {
        using var main = new MainWindowViewModel();
        var engineerMode = main.SettingsVM.IsEngineerMode;
        try
        {
            main.SettingsVM.IsEngineerMode = false;
            var station = main.TestVM.Station;
            var elapsed = TimeSpan.FromSeconds(12);
            station.CompleteRun(ExecutionStatus.Completed, null, elapsed: elapsed);
            var headline = station.Headline;
            main.SettingsVM.IsEngineerMode = true;
            main.SettingsVM.IsEngineerMode = false;

            Assert.True(station.HasResult);
            Assert.Equal(elapsed, station.Elapsed);
            Assert.Equal(headline, station.Headline);
        }
        finally { main.SettingsVM.IsEngineerMode = engineerMode; }
    });

    private static async Task InSession(Action test)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() => { test(); return true; }, CancellationToken.None);
    }
}
