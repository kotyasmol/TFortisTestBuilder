using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class OfflineRegisterDictionaryTests
{
    [Fact]
    public Task DictionaryWithoutHardware_OffersNamesAndFiltersReadOnlyRegisters() => InSession(() =>
    {
        using var write = new ModbusWriteNodeViewModel();
        using var check = new CheckRegisterRangeNodeViewModel();
        using var wait = new WaitUntilNodeViewModel();
        Assert.True(write.ShowRegisterSelector);
        Assert.True(check.ShowRegisterSelector);
        Assert.True(wait.ShowRegisterSelector);
        Assert.Contains(write.AvailableRegisters, r => r.Address == 1200 && r.Name.Contains("PS-2:"));
        Assert.Contains(write.AvailableRegisters, r => r.Address == 1200 && r.Name.Contains("PS-3:"));
        Assert.All(write.AvailableRegisters, r => Assert.False(r.IsReadOnly));
        Assert.Contains(check.AvailableRegisters, r => r.Address == 1000 && r.IsReadOnly);
        Assert.Contains(wait.AvailableRegisters, r => r.Address == 1000 && r.IsReadOnly);
        write.Address = 65535;
        Assert.True(write.ShowRegisterSelector); // A known address is not required to browse the dictionary.
        Assert.True(write.ShowAddressTextBox);
        Assert.Contains("описание отсутствует", write.RegisterDescription);
    });

    [Fact]
    public Task ProfileMap_PropagatesToNestedAndNewNodes_AndSeparatesAreas() => InSession(() =>
    {
        var graph = new GraphWorkspaceViewModel { Hardware = Map("Контроллер A") };
        using var subtest = new SubtestNodeViewModel();
        graph.Nodes.Add(subtest);
        using var write = new ModbusWriteNodeViewModel { SlaveId = 7, Address = 10 };
        using var check = new CheckRegisterRangeNodeViewModel { SlaveId = 7, Address = 10 };
        using var wait = new WaitUntilNodeViewModel { SlaveId = 7, Address = 10 };
        subtest.BodyGraph.Nodes.Add(write); subtest.BodyGraph.Nodes.Add(check); subtest.BodyGraph.Nodes.Add(wait);
        Assert.All(new[] { write.RegisterDescription, check.RegisterDescription, wait.RegisterDescription }, text => Assert.Contains("Выход", text));
        Assert.Single(write.AvailableRegisters);
        check.RegisterArea = wait.RegisterArea = ModbusArea.Input;
        Assert.Contains("Измерение", check.RegisterDescription);
        Assert.DoesNotContain("Выход", check.RegisterDescription);
        Assert.All(check.AvailableRegisters, r => Assert.Equal(ModbusArea.Input, r.Area));
        Assert.Contains("Измерение", wait.RegisterDescription);
        write.RegisterArea = ModbusArea.Input;
        Assert.Empty(write.AvailableRegisters);
        Assert.True(write.ShowAddressTextBox);
        graph.Hardware = Map("Контроллер B");
        Assert.Contains("Контроллер B", wait.RegisterDescription);
        wait.SlaveId = 99;
        Assert.Empty(wait.AvailableRegisters);
        Assert.Contains("описание отсутствует", wait.RegisterDescription);
        wait.UseCurrentSlaveId = true;
        Assert.Single(wait.AvailableRegisters);
        Assert.Contains("Контроллер B", wait.RegisterDescription);
    });

    [Fact]
    public Task SavedProfileAndHardwareUndo_RefreshOfflineChoices() => InSession(() =>
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        vm.RootGraph.Hardware = Map("Профиль A");
        using var node = new ModbusWriteNodeViewModel { SlaveId = 7, Address = 10 };
        vm.Nodes.Add(node);
        var json = GraphSerializer.Serialize(vm, "offline");
        GraphSerializer.Deserialize(json, vm);
        var restored = vm.Nodes.OfType<ModbusWriteNodeViewModel>().Single();
        Assert.Contains("Профиль A", restored.RegisterDescription);
        vm.ApplyHardware(Map("Профиль B"));
        Assert.Contains("Профиль B", restored.AvailableRegisters.Single().Name);
        vm.UndoCommand.Execute(null);
        Assert.Contains("Профиль A", restored.AvailableRegisters.Single().Name);
        Assert.False(bus.IsConnected);
        Assert.Empty(SlaveRegistry.Instance.Slaves);
    });

    [Fact]
    public Task RtlProfiles_ExposeTheirActualMapsWithoutConnection() => InSession(() =>
    {
        foreach (var name in new[] { "RTL-SW", "RTL-POE" })
        {
            var definition = HardwarePresets.Load().Single(d => d.Name == name);
            var graph = new GraphWorkspaceViewModel { Hardware = new() { Devices = new() { definition } } };
            var writable = definition.Registers.First(r => r.Writable);
            using var node = new ModbusWriteNodeViewModel { SlaveId = definition.SlaveId, Address = (ushort)writable.Address, RegisterArea = writable.Area };
            graph.Nodes.Add(node);
            Assert.Contains(writable.Name, node.RegisterDescription);
            Assert.Contains(name, node.RegisterDescription);
            Assert.Equal(definition.Registers.Count(r => r.Writable && r.Area == writable.Area), node.AvailableRegisters.Count);
        }
    });

    [Fact]
    public Task VisibleOfflineDropdown_SelectsAddress_AndNumericInputRemainsAvailable() => InSession(() =>
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        vm.RootGraph.Hardware = Map("Контроллер");
        var node = new ModbusWriteNodeViewModel { SlaveId = 7 };
        vm.Nodes.Add(node);
        using var view = new GraphEditorView { DataContext = vm };
        var window = new Window { Width = 1100, Height = 850, Content = view };
        try
        {
            window.Show(); Layout();
            var dictionary = view.GetVisualDescendants().OfType<ComboBox>().Single(c => ReferenceEquals(c.ItemsSource, node.AvailableRegisters));
            Assert.True(dictionary.IsEffectivelyVisible);
            Assert.True(dictionary.IsEffectivelyEnabled);
            dictionary.SelectedItem = node.AvailableRegisters.Single();
            Layout();
            Assert.Equal((ushort)10, node.Address);
            Assert.Equal("10", node.AddressText);
            var address = view.GetVisualDescendants().OfType<TextBox>().Single(c => c.DataContext == node && c.Text == "10");
            Assert.True(address.IsEffectivelyVisible);
            Assert.True(address.Bounds.Width >= 50);
            var screenshot = Environment.GetEnvironmentVariable("TFORTIS_REGISTER_SCREENSHOT");
            if (!string.IsNullOrEmpty(screenshot)) { dictionary.IsDropDownOpen = true; Layout(); using var frame = window.CaptureRenderedFrame(); frame?.Save(screenshot); }
        }
        finally { window.Close(); }
        void Layout() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); Dispatcher.UIThread.RunJobs(); }
    });

    private static HardwareConfiguration Map(string name) => new()
    {
        Devices = new() { new() { Name = name, SlaveId = 7, Registers = new()
        {
            new() { Address = 10, Name = "Выход", Writable = true },
            new() { Address = 10, Name = "Измерение", Area = ModbusArea.Input },
        } } }
    };

    private static async Task InSession(Action action)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var registry = SlaveRegistry.Instance;
            var old = registry.Slaves.ToArray(); var connected = registry.IsConnected;
            registry.Slaves.Clear(); registry.NotifyConnected(false);
            try { action(); }
            finally { registry.Slaves.Clear(); foreach (var slave in old) registry.Slaves.Add(slave); registry.NotifyConnected(connected); }
        }, CancellationToken.None);
    }
}
