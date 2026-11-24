using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class HardwareWorkflowTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EmptyProfileCanAddAddressedUnitsAndEditARegisterWithForms(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            var theme = Application.Current!.RequestedThemeVariant; Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            var library = new EquipmentLibrary(Path.Combine(clean.Root, "library.json"));
            var libraryBefore = JsonSerializer.Serialize(library.Load());
            var window = new HardwareEditorWindow(vm, library);
            try
            {
                window.Show(); Layout(window);
                Assert.True(Find<StackPanel>(window, "ProfileEquipmentEmpty").IsEffectivelyVisible);
                Assert.False(Find<Button>(window, "RemoveProfileDevice").IsEnabled);
                Capture(window, "03-empty", dark);
                Click(window, Find<Button>(window, "AddFirstProfileEquipment"));
                var add = Assert.Single(window.GetVisualDescendants().OfType<AddProfileEquipmentView>()); Layout(window);
                var list = Find<ListBox>(add, "EquipmentCatalogList");
                list.SelectedItem = list.Items.OfType<EquipmentTemplate>().Single(t => t.Device.Name == "EL-60v5");
                Find<TextBox>(add, "EquipmentCount").Text = "8"; Find<TextBox>(add, "EquipmentFirstSlave").Text = "1"; Find<TextBox>(add, "EquipmentSlaveStep").Text = "2"; Layout(window);
                Assert.Contains("1, 3, 5, 7, 9, 11, 13, 15", Find<TextBlock>(add, "EquipmentAddressPreview").Text);
                Capture(window, "04-add-group", dark); Click(window, Find<Button>(add, "ConfirmAddEquipment")); Layout(window);
                var devices = Find<DataGrid>(window, "ProfileEquipmentGrid");
                var rows = devices.ItemsSource.Cast<DeviceDefinition>().ToArray();
                Assert.Equal(new byte[] { 1, 3, 5, 7, 9, 11, 13, 15 }, rows.Select(d => d.SlaveId));
                Assert.NotSame(rows[0].Registers, rows[1].Registers); Assert.Null(vm.RootGraph.Hardware);
                Find<TextBox>(window, "ProfileDeviceName").Text = "Нагрузка первого порта";
                Find<TextBox>(window, "ProfileDeviceSlaveId").Text = "17";
                Find<TextBox>(window, "ProfileDeviceSlaveId").Focus(); window.KeyPress(Key.Tab, Avalonia.Input.RawInputModifiers.None); window.KeyRelease(Key.Tab, Avalonia.Input.RawInputModifiers.None); Layout(window);
                Assert.Equal("Нагрузка первого порта", rows[0].Name); Assert.Equal(17, rows[0].SlaveId);
                Capture(window, "05-profile", dark);
                Click(window, Find<Button>(window, "AddProfileRegister"));
                var editor = Assert.Single(window.GetVisualDescendants().OfType<RegisterDefinitionEditor>()); Layout(window);
                Click(window, Find<Button>(editor, "SaveRegisterDefinition")); Assert.True(editor.IsEffectivelyVisible);
                Assert.Contains("название", Find<TextBlock>(editor, "RegisterDefinitionStatus").Text);
                Find<TextBox>(editor, "RegisterDefinitionAddress").Text = "1400"; Find<TextBox>(editor, "RegisterDefinitionName").Text = "Дубликат";
                Click(window, Find<Button>(editor, "SaveRegisterDefinition")); Assert.True(editor.IsEffectivelyVisible);
                Assert.Contains("уже есть", Find<TextBlock>(editor, "RegisterDefinitionStatus").Text);
                Find<TextBox>(editor, "RegisterDefinitionAddress").Text = "1500"; Find<TextBox>(editor, "RegisterDefinitionName").Text = "Ручное состояние";
                Find<CheckBox>(editor, "RegisterDefinitionWritable").IsChecked = true; Find<CheckBox>(editor, "RegisterDefinitionRepeat").IsChecked = true;
                Capture(window, "06-register-form", dark);
                Click(window, Find<Button>(editor, "SaveRegisterDefinition")); Layout(window);
                var added = rows[0].Registers.Single(r => r.Address == 1500); Assert.True(added.Writable); Assert.True(added.RetryWriteOnMismatch);
                Assert.DoesNotContain(rows[1].Registers, r => r.Address == 1500);
                Assert.Equal(4, Find<DataGrid>(window, "ProfileRegistersGrid").Columns.Count(c => c.IsVisible));
                Find<CheckBox>(window, "ShowAllRegisterFields").IsChecked = true;
                Assert.Equal(7, Find<DataGrid>(window, "ProfileRegistersGrid").Columns.Count(c => c.IsVisible));
                Find<CheckBox>(window, "ShowAllRegisterFields").IsChecked = false;
                added.ExtensionData = new() { ["firmwareHint"] = JsonSerializer.SerializeToElement("preserve") };
                Click(window, Find<Button>(window, "EditProfileRegister")); editor = Assert.Single(window.GetVisualDescendants().OfType<RegisterDefinitionEditor>()); Layout(window);
                Find<ComboBox>(editor, "RegisterDefinitionArea").SelectedItem = ModbusArea.Input;
                Assert.False(Find<CheckBox>(editor, "RegisterDefinitionWritable").IsEnabled); Assert.False(Find<CheckBox>(editor, "RegisterDefinitionRepeat").IsChecked);
                Click(window, Find<Button>(editor, "SaveRegisterDefinition")); Layout(window);
                added = rows[0].Registers.Single(r => r.Address == 1500); Assert.Equal(ModbusArea.Input, added.Area); Assert.False(added.Writable); Assert.Equal("preserve", added.ExtensionData!["firmwareHint"].GetString());
                Click(window, window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Применить к профилю")));
                Assert.False(window.IsVisible); Assert.Equal(8, vm.RootGraph.Hardware!.Devices.Count);
                Assert.Equal(17, vm.RootGraph.Hardware.Devices[0].SlaveId); Assert.Equal(libraryBefore, JsonSerializer.Serialize(library.Load()));
            }
            finally { foreach (var child in window.OwnedWindows.ToArray()) child.Close(); window.Close(); Application.Current.RequestedThemeVariant = theme; }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BatchConflictsAndInvalidTextAreRejectedAndCancellingKeepsProfileUnchanged()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            vm.RootGraph.Hardware = new HardwareConfiguration { Devices = new() { new() { Name = "Существующий прибор", SlaveId = 3, Registers = new() { new() { Address = 1, Name = "Регистр" } } } } };
            var before = GraphSerializer.Serialize(vm, "Исходный"); var library = new EquipmentLibrary(Path.Combine(clean.Root, "library.json"));
            var window = new HardwareEditorWindow(vm, library) { Width = 1000, Height = 650 };
            try
            {
                window.Show(); Layout(window); Click(window, Find<Button>(window, "AddProfileEquipment"));
                var add = Assert.Single(window.GetVisualDescendants().OfType<AddProfileEquipmentView>()); Layout(window);
                Find<TextBox>(add, "EquipmentCount").Text = "2"; Find<TextBox>(add, "EquipmentFirstSlave").Text = "1"; Find<TextBox>(add, "EquipmentSlaveStep").Text = "2"; Layout(window);
                Assert.False(Find<Button>(add, "ConfirmAddEquipment").IsEnabled); Assert.Contains("заняты", Find<TextBlock>(add, "EquipmentAddressPreview").Text);
                Find<TextBox>(add, "EquipmentFirstSlave").Text = "bad"; Assert.False(Find<Button>(add, "ConfirmAddEquipment").IsEnabled);
                add.Cancel(); Layout(window); Assert.Single(Find<DataGrid>(window, "ProfileEquipmentGrid").ItemsSource.Cast<object>());
                Find<TextBox>(window, "ProfileDeviceSlaveId").Text = "не число";
                Click(window, window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Применить к профилю"))); Assert.True(window.IsVisible);
                Click(window, Find<Button>(window, "RemoveProfileDevice")); Layout(window); Assert.True(Find<StackPanel>(window, "ProfileEquipmentEmpty").IsVisible);
                Assert.Equal(before, GraphSerializer.Serialize(vm, "Исходный")); window.Close(); Assert.Equal(before, GraphSerializer.Serialize(vm, "Исходный"));
            }
            finally { foreach (var child in window.OwnedWindows.ToArray()) child.Close(); window.Close(); }
        }, CancellationToken.None);
    }
    private static T Find<T>(Control window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Layout(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); Dispatcher.UIThread.RunJobs(); }
    private static void Click(Window window, Button button)
    {
        button.BringIntoView(); Layout(window);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout(window);
    }
    private static void Capture(Window window, string step, bool dark)
    {
        var folder = Environment.GetEnvironmentVariable("TFORTIS_HARDWARE_WORKFLOW_CAPTURES") ?? Path.Combine(Path.GetTempPath(), "tfortis-hardware-workflow-20261123");
        Directory.CreateDirectory(folder); using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(folder, step + (dark ? "-dark" : "-light") + ".png"));
    }
}
