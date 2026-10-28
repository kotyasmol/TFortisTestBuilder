using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Services;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class DataTestPairEditorTests
{
    [Fact]
    public void AllTenPortsCanUseGigabitAndIndividualPairsCanBeExcluded()
    {
        var node = new RunDataTestNodeViewModel();
        var draft = new DataTestPairEditorViewModel(node);
        draft.SetAllSpeeds(1000); draft.Apply(node);
        Assert.True(node.AllowGigabit);
        Assert.Equal(5, new DataTestPairEditorViewModel(node).Rows.Count);
        Assert.All(new DataTestPairEditorViewModel(node).Rows, r => Assert.Equal(1000, r.Mbps));
        draft.Rows[1].Mbps = 100;
        draft.Rows[2].Enabled = false;
        draft.Apply(node);
        Assert.DoesNotContain(draft.Rows[2].Name, node.PortsText);
        Assert.Contains(draft.Rows[2].Name, node.DisabledPortsText);
        var restored = new DataTestPairEditorViewModel(node);
        Assert.Equal(100, restored.Rows.Single(r => r.Name == draft.Rows[1].Name).Mbps);
        Assert.False(restored.Rows.Single(r => r.Name == draft.Rows[2].Name).Enabled);
        draft.Rows[2].Enabled = true; draft.Apply(node);
        Assert.Empty(node.DisabledPortsText);
    }

    [Fact]
    public void CancelledOrInvalidDraftNeverModifiesNode()
    {
        var node = new RunDataTestNodeViewModel();
        var original = node.PortsText;
        var draft = new DataTestPairEditorViewModel(node);
        draft.SetAllSpeeds(1000);
        Assert.False(node.AllowGigabit);
        Assert.Equal(original, node.PortsText);
        draft.Rows[0].SecondIp = draft.Rows[0].FirstIp;
        Assert.Throws<InvalidOperationException>(() => draft.Apply(node));
        Assert.False(node.AllowGigabit);
        Assert.Equal(original, node.PortsText);
        foreach (var r in draft.Rows) r.Enabled = false;
        Assert.Throws<InvalidOperationException>(() => draft.Apply(node));
    }

    [Fact]
    public void DisabledPairsRoundTripAndCloneWithoutEnteringRuntimePorts()
    {
        using var modbus = new ModbusService();
        using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        var node = new RunDataTestNodeViewModel { PortsText = "A,10.0.0.2,10.0.0.3,100", DisabledPortsText = "B,10.0.0.4,10.0.0.5,1000" };
        vm.RootGraph.Nodes.Add(node);
        GraphSerializer.Deserialize(GraphSerializer.Serialize(vm, "Pairs"), vm);
        var restored = Assert.Single(vm.RootGraph.Nodes.OfType<RunDataTestNodeViewModel>());
        Assert.Equal(node.PortsText, restored.PortsText);
        Assert.Equal(node.DisabledPortsText, restored.DisabledPortsText);
        var clone = Assert.IsType<RunDataTestNodeViewModel>(restored.Clone());
        var draft = new DataTestPairEditorViewModel(clone);
        draft.Rows[1].Enabled = true; draft.Apply(clone);
        Assert.True(clone.AllowGigabit);
        Assert.Contains(",1000", clone.PortsText);
        Assert.Equal(node.PortsText, restored.PortsText);
    }

    [Fact]
    public async Task TableBindingsApplySelectedAddressesAndSpeed()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() => {
            var node = new RunDataTestNodeViewModel { PortsText = "Пара 1,192.168.0.2,192.168.0.3,100" };
            var window = new DataTestPairsWindow(node);
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var combos = window.GetVisualDescendants().OfType<ComboBox>().ToArray();
                Assert.Equal(3, combos.Length);
                combos[0].SelectedItem = "192.168.0.4";
                combos[2].SelectedItem = 1000;
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                window.CaptureRenderedFrame()!.Save(Path.Combine(Path.GetTempPath(), "tfortis-datatest-pairs.png"));
                window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Применить к ноде"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("Пара 1,192.168.0.4,192.168.0.3,1000", node.PortsText);
                Assert.True(node.AllowGigabit);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
