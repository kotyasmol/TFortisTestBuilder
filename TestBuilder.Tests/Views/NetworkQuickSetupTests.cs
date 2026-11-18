using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using TestBuilder.Services;
using TestBuilder.ViewModels;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class NetworkQuickSetupTests
{
    private static NetworkConfigurationPreset Preset(string id = "four", string name = "Четыре порта") => new(id, name,
        new BenchNetworkPlan { Addresses = new()
        {
            new() { Ip = "192.168.0.2", TemporaryIp = "192.0.2.2", Pair = "A", IncludeInDataTest = true },
            new() { Ip = "192.168.0.3", TemporaryIp = "192.0.2.3", Pair = "A", IncludeInDataTest = true },
            new() { Ip = "192.168.0.4", TemporaryIp = "192.0.2.4", Pair = "B", IncludeInDataTest = true },
            new() { Ip = "192.168.0.5", TemporaryIp = "192.0.2.5", Pair = "B", IncludeInDataTest = true }
        }});

    private static BenchAdapter[] Cards() => new[]
    {
        new BenchAdapter("local-a", "Карта A", "001122334455", "Up", "192.168.0.2", 1000),
        new BenchAdapter("local-b", "Карта B", "001122334456", "Up", "192.168.0.3", 1000),
        new BenchAdapter("local-c", "Карта C", "001122334457", "Up", "192.168.0.4", 1000),
        new BenchAdapter("local-d", "Карта D", "001122334458", "Up", "192.168.0.5", 1000)
    };

    [Fact]
    public void PresetLoadKeepsLocalIdsAndDoesNotAlterTheSourceOrAppliedOwnership()
    {
        var settings = AppSettings.Instance; var oldPlan = settings.NetworkPlan;
        var oldBindings = settings.DataTestAdapterIds; var oldApplied = settings.AppliedDataTestAdapterIds;
        try
        {
            settings.NetworkPlan = Preset().CopyPlan();
            settings.DataTestAdapterIds = Cards().ToDictionary(c => c.Ipv4, c => c.Id);
            settings.AppliedDataTestAdapterIds = new() { ["10.9.9.2"] = "previous-card" };
            var applied = settings.AppliedDataTestAdapterIds; var source = Preset(); var saved = 0;
            var vm = new NetworkSetupViewModel(Cards, () => true, loadPresets: () => new[] { source }, saveSettings: () => saved++);
            vm.Rows[0].SelectedAdapter = Cards()[1];
            vm.Presets.Single(p => p.Definition.Id == source.Id).LoadCommand.Execute(null);
            Assert.Equal(1, saved);
            Assert.Equal("local-b", vm.Rows[0].SelectedAdapter!.Id);
            Assert.Same(applied, settings.AppliedDataTestAdapterIds);
            Assert.Equal("local-a", settings.DataTestAdapterIds["192.168.0.2"]);
            Assert.NotSame(source.Plan, settings.NetworkPlan);
            settings.NetworkPlan.Addresses[0].Name = "Изменено";
            Assert.Equal("", source.Plan.Addresses[0].Name);
        }
        finally { settings.NetworkPlan = oldPlan; settings.DataTestAdapterIds = oldBindings; settings.AppliedDataTestAdapterIds = oldApplied; }
    }

    [Fact]
    public void FailedSaveRestoresPlanAndCustomPresets()
    {
        var settings = AppSettings.Instance; var oldPlan = settings.NetworkPlan; var oldPresets = settings.NetworkPresets;
        var vm = new NetworkSetupViewModel(Cards, () => true, loadPresets: () => new[] { Preset() },
            saveSettings: () => throw new IOException("disk full"));
        vm.Presets.First().LoadCommand.Execute(null);
        Assert.Same(oldPlan, settings.NetworkPlan);
        Assert.Contains("disk full", vm.PresetStatus);
        Assert.Throws<IOException>(() => vm.AddCustomPresets(new[] { Preset() }));
        Assert.Same(oldPresets, settings.NetworkPresets);
    }

    [Fact]
    public void ImportCreatesIndependentCustomItemsAndDoesNotReplacePlanOrLocalBindings()
    {
        var settings = AppSettings.Instance; var old = settings.NetworkPresets;
        var plan = settings.NetworkPlan; var bindings = settings.DataTestAdapterIds;
        try
        {
            settings.NetworkPresets = new();
            var source = Preset();
            var vm = new NetworkSetupViewModel(Cards, () => true, loadPresets: () => new[] { source }, saveSettings: () => { });
            vm.AddCustomPresets(new[] { source });
            var imported = Assert.Single(settings.NetworkPresets);
            Assert.NotEqual(source.Id, imported.Id);
            Assert.Equal("Четыре порта (2)", imported.Name);
            Assert.NotSame(source.Plan, imported.Plan);
            Assert.Same(plan, settings.NetworkPlan); Assert.Same(bindings, settings.DataTestAdapterIds);
            var restored = NetworkPresetCatalog.Read(new NetworkPresetCatalog { Presets = new() { imported } }.ToJson());
            Assert.Equal(imported.Name, restored.Presets.Single().Name);
            Assert.DoesNotContain("local-a", restored.ToJson());
        }
        finally { settings.NetworkPresets = old; }
    }

    [Fact]
    public async Task RunningProbeBlocksMutationsAndCanBeCancelledWithoutChangingSettings()
    {
        var settings = AppSettings.Instance; var oldPlan = settings.NetworkPlan; var oldBindings = settings.DataTestAdapterIds;
        try
        {
            settings.NetworkPlan = Preset().CopyPlan(); settings.DataTestAdapterIds = new();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            var vm = new NetworkSetupViewModel(Cards, () => true, probePair: async (pair, token) =>
            {
                calls++; started.TrySetResult(); await Task.Delay(Timeout.Infinite, token);
                return new(pair, "unreachable", "");
            }, loadPresets: () => new[] { Preset() }, saveSettings: () => throw new Exception("must not save"));
            var plan = JsonSerializer.Serialize(settings.NetworkPlan);
            var task = vm.ProbeConnectionsCommand.ExecuteAsync(null); await started.Task;
            Assert.True(vm.IsBusy); Assert.False(vm.CanEdit); Assert.True(vm.CanCancelProbe);
            Assert.False(vm.ApplyCommand.CanExecute(null)); Assert.False(vm.Presets[0].LoadCommand.CanExecute(null));
            Assert.False(vm.CheckRuntimeCommand.CanExecute(null)); Assert.False(vm.ProbeConnectionsCommand.CanExecute(null));
            vm.CancelProbeCommand.Execute(null); await task;
            Assert.Equal(1, calls); Assert.False(vm.IsBusy); Assert.Contains("остановлена", vm.ProbeStatus);
            Assert.Equal(plan, JsonSerializer.Serialize(settings.NetworkPlan)); Assert.Empty(settings.DataTestAdapterIds);
        }
        finally { settings.NetworkPlan = oldPlan; settings.DataTestAdapterIds = oldBindings; }
    }

    [Fact]
    public async Task BothPairsAreReportedAndFailureDoesNotDisappearBehindNextSuccess()
    {
        var settings = AppSettings.Instance; var previous = settings.NetworkPlan;
        try
        {
            settings.NetworkPlan = Preset().CopyPlan();
            var vm = new NetworkSetupViewModel(Cards, () => true, probePair: (pair, token) => Task.FromResult(
                new NetworkProbeResult(pair, "result", "", new(pair.Name == "B", 10, pair.Name == "B" ? 10 : 0, ""), new(true, 10, 10, ""))),
                loadPresets: () => Array.Empty<NetworkConfigurationPreset>());
            await vm.ProbeConnectionsCommand.ExecuteAsync(null);
            Assert.Equal(2, vm.ProbeResults.Count); Assert.False(vm.ProbeResults[0].Passed); Assert.True(vm.ProbeResults[1].Passed);
            Assert.Contains("обе стороны: 1", vm.ProbeStatus);
        }
        finally { settings.NetworkPlan = previous; }
    }

    [Fact]
    public async Task MappingAndLinkChangesInvalidatePreviouslySuccessfulResults()
    {
        var settings = AppSettings.Instance; var previous = settings.NetworkPlan;
        var cards = Cards();
        try
        {
            settings.NetworkPlan = Preset().CopyPlan();
            var vm = new NetworkSetupViewModel(() => cards, () => true, probePair: (pair, token) => Task.FromResult(
                new NetworkProbeResult(pair, "OK", "", new(true, 10, 10, ""), new(true, 10, 10, ""))),
                loadPresets: () => Array.Empty<NetworkConfigurationPreset>());
            await vm.ProbeConnectionsCommand.ExecuteAsync(null);
            Assert.Equal(2, vm.ProbeResults.Count);
            vm.Rows[0].SelectedAdapter = cards[1];
            Assert.Empty(vm.ProbeResults); Assert.Contains("Повторите", vm.ProbeStatus);
            await vm.ProbeConnectionsCommand.ExecuteAsync(null);
            cards = cards.Select(c => c.Id == "local-a" ? c with { Status = "Down", SpeedMbps = 0 } : c).ToArray();
            vm.RefreshCommand.Execute(null);
            Assert.Empty(vm.ProbeResults);
        }
        finally { settings.NetworkPlan = previous; }
    }

    [Fact]
    public void LargerPresetFindsNewRowsByActualUniqueIpWithoutGuessingByCardOrder()
    {
        var settings = AppSettings.Instance; var oldPlan = settings.NetworkPlan; var oldBindings = settings.DataTestAdapterIds;
        try
        {
            var four = Preset(); var two = Preset() with { Id = "two", Name = "Два порта", Plan = new() { Addresses = four.CopyPlan().Addresses.Take(2).ToList() } };
            settings.NetworkPlan = two.CopyPlan(); settings.DataTestAdapterIds = new();
            var vm = new NetworkSetupViewModel(() => Cards().Reverse().ToArray(), () => true,
                loadPresets: () => new[] { four }, saveSettings: () => { });
            vm.Rows[0].SelectedAdapter = null;
            vm.Presets.First().LoadCommand.Execute(null);
            Assert.Null(vm.Rows[0].SelectedAdapter);
            Assert.Equal("local-c", vm.Rows[2].SelectedAdapter?.Id);
            Assert.Equal("local-d", vm.Rows[3].SelectedAdapter?.Id);
        }
        finally { settings.NetworkPlan = oldPlan; settings.DataTestAdapterIds = oldBindings; }
    }

    [Fact]
    public async Task NewNetworkControlsRenderAndCommandsFollowBusyState()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var vm = new NetworkSetupViewModel(Cards, () => true);
            var window = new Window { Width = 1200, Height = 1000, Content = new NetworkSetupView { DataContext = vm } };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.True(vm.ProbeConnectionsCommand.CanExecute(null));
                vm.IsCheckingRuntime = true; Assert.False(vm.CanEdit); Assert.False(vm.ProbeConnectionsCommand.CanExecute(null));
                vm.IsCheckingRuntime = false; window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                window.CaptureRenderedFrame()!.Save(Path.Combine(Path.GetTempPath(), "tfortis-network-quick-setup.png"));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
