using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using TestBuilder.Services;
using TestBuilder.ViewModels;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class NetworkSetupTests
{
    private static BenchAdapter[] Cards() => DataTestNetworkConfigurator.BenchIps.Select((ip, i) =>
        new BenchAdapter($"card-{i}", $"Ethernet {i + 1}", $"0011223344{i:00}", "Up", ip, 1000)).ToArray();

    [Fact]
    public void OccupiedSelectionSwapsCards_AndClearDisablesApply()
    {
        var cards = Cards();
        var vm = new NetworkSetupViewModel(() => cards, () => true);
        vm.Rows[0].SelectedAdapter = cards[1];
        Assert.Equal(cards[0], vm.Rows[1].SelectedAdapter);
        Assert.Equal(cards[1], vm.Rows[0].SelectedAdapter);
        Assert.True(vm.CanApply);
        vm.Rows[0].ClearCommand.Execute(null);
        Assert.False(vm.CanApply);
        vm.RefreshCommand.Execute(null);
        Assert.Null(vm.Rows[0].SelectedAdapter);
        Assert.Contains("не выбран", vm.Validation);
    }

    [Fact]
    public void RefreshPreservesDraft_AndMissingCardBlocksApply()
    {
        var cards = Cards();
        var vm = new NetworkSetupViewModel(() => cards, () => true);
        vm.Rows[0].SelectedAdapter = cards[1];
        cards = cards.Select(c => c with { Status = "Down" }).ToArray();
        vm.RefreshCommand.Execute(null);
        Assert.Equal("card-1", vm.Rows[0].SelectedAdapter?.Id);
        Assert.Equal("Down", vm.Rows[0].SelectedAdapter?.Status);
        var allCards = cards;
        cards = cards.Where(c => c.Id != "card-1").ToArray();
        vm.RefreshCommand.Execute(null);
        Assert.False(vm.CanApply);
        vm.RefreshCommand.Execute(null);
        Assert.Null(vm.Rows[0].SelectedAdapter);
        cards = allCards;
        vm.RefreshCommand.Execute(null);
        Assert.Equal("card-1", vm.Rows[0].SelectedAdapter?.Id);
    }

    [Fact]
    public void ProductionAndBusyStateBlockAllEditorCommands()
    {
        var allowed = true;
        var vm = new NetworkSetupViewModel(Cards, () => allowed);
        allowed = false; vm.RefreshAvailability();
        Assert.False(vm.CanEdit);
        Assert.False(vm.ApplyCommand.CanExecute(null));
        Assert.False(vm.RefreshCommand.CanExecute(null));
        allowed = true; vm.IsBusy = true;
        Assert.False(vm.ResetCommand.CanExecute(null));
        Assert.False(vm.EditAddressPlanCommand.CanExecute(null));
        vm.IsBusy = false;
        Assert.True(vm.CanApply);
    }

    [Fact]
    public async Task ViewRendersAndDisablesSelectorsDuringApply()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var vm = new NetworkSetupViewModel(Cards, () => true);
            var view = new NetworkSetupView { DataContext = vm };
            var window = new Window { Width = 1200, Height = 1000, Content = view };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                vm.IsBusy = true; Dispatcher.UIThread.RunJobs();
                Assert.False(vm.CanEdit);
                vm.IsBusy = false; Dispatcher.UIThread.RunJobs();
                window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                window.CaptureRenderedFrame()!.Save(Path.Combine(Path.GetTempPath(), "tfortis-network-editor.png"));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
