using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.SerializationTests;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class ConnectionEditorTests
{
    [Fact]
    public async Task UserAddsConsole_SelectsItInNode_AndSavesConnectionToProfile()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(() =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            vm.RootGraph.Hardware = ConnectionProfileTests.Hardware();
            var node = new SerialConsoleNodeViewModel { DeviceId = "processor" };
            vm.RootGraph.Nodes.Add(node);
            var equipment = new HardwareEditorWindow(vm);
            equipment.Show(); Layout(equipment);
            equipment.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 2;
            Layout(equipment);
            Click(equipment, "Добавить консоль"); Layout(equipment);
            var list = equipment.GetVisualDescendants().OfType<ListBox>().Single();
            var device = Assert.IsType<ConsoleDeviceDefinition>(list.SelectedItem);
            equipment.GetVisualDescendants().OfType<TextBox>().Single(t => ReferenceEquals(t.DataContext, device) && t.Text == "Консоль устройства").Text = "Дополнительный прибор";
            equipment.GetVisualDescendants().OfType<AutoCompleteBox>().Single(t => ReferenceEquals(t.DataContext, device)).Text = "SIM-SECOND";
            Layout(equipment);
            var output = Path.Combine(Path.GetTempPath(), "tfortis-connections-ui"); Directory.CreateDirectory(output);
            using (var frame = equipment.CaptureRenderedFrame()) { Assert.NotNull(frame); frame.Save(Path.Combine(output, "equipment.png")); }
            Click(equipment, "Применить к профилю");
            Assert.False(equipment.IsVisible);
            Assert.Equal(2, vm.RootGraph.Hardware.ConsoleDevices.Count);
            Assert.Equal("SIM-SECOND", vm.RootGraph.Hardware.ConsoleDevices[1].PortName);
            Assert.Equal("Дополнительный прибор", vm.RootGraph.Hardware.ConsoleDevices[1].Name);

            using var editor = new GraphEditorView { DataContext = vm };
            var window = new Window { Content = editor, Width = 1500, Height = 1000 };
            try
            {
                window.Show(); Layout(window);
                var choice = editor.GetVisualDescendants().OfType<ComboBox>()
                    .Single(c => ReferenceEquals(c.DataContext, node) && c.ItemsSource?.Cast<object>().Any(i => i is ConsoleDeviceDefinition) == true);
                Assert.Equal("processor", node.DeviceId);
                Assert.Equal("processor", Assert.IsType<ConsoleDeviceDefinition>(choice.SelectedItem).Id);
                choice.SelectedItem = vm.RootGraph.Hardware.ConsoleDevices[1]; Layout(window);
                Assert.Equal(device.Id, node.DeviceId); Assert.False(node.UsesLegacyConnection);
                var saved = GraphSerializer.Serialize(vm, "Новое устройство · Проверка");
                GraphSerializer.Deserialize(saved, vm); Layout(window);
                Assert.Equal(device.Id, vm.RootGraph.Nodes.OfType<SerialConsoleNodeViewModel>().Single().DeviceId);
                Assert.Equal("SIM-SECOND", vm.RootGraph.Hardware!.ConsoleDevices[1].PortName);
            }
            finally { editor.DataContext = null; window.Content = null; window.Close(); equipment.Close(); Dispatcher.UIThread.RunJobs(); }
            return true;
        }, CancellationToken.None);
    }

    private static void Click(Window window, string content) => window.GetVisualDescendants().OfType<Button>()
        .Single(b => b.Content as string == content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Layout(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); }
}
