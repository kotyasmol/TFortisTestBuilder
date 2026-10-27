using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class EquipmentLibraryTests
{
    [Fact]
    public async Task NewEquipmentDialogValidatesAndSavesReusableDefinition()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(async () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var library = new EquipmentLibrary(Path.Combine(folder, "equipment.json"));
            var owner = new Window(); owner.Show();
            var editor = new EquipmentTemplateWindow(library, new DeviceDefinition
                { Name = "", Registers = new() { new() { Address = 1700 } } });
            try
            {
                var completion = editor.ShowDialog<EquipmentTemplate?>(owner);
                editor.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var save = editor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveEquipmentTemplate");
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(completion.IsCompleted);
                editor.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "EquipmentName").Text = "Мой SIMBAT";
                editor.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "EquipmentIdentificationValuesText").Text = "9, 10";
                editor.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                editor.CaptureRenderedFrame()!.Save(Path.Combine(Path.GetTempPath(), "tfortis-new-equipment.png"));
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var result = await completion;
                Assert.NotNull(result);
                Assert.Equal(new ushort[] { 9, 10 }, result.Device.ExpectedIdentificationValues);
                Assert.Contains(new EquipmentLibrary(Path.Combine(folder, "equipment.json")).Load(), e => e.Id == result.Id);
            }
            finally { editor.Close(); owner.Close(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CustomEquipmentSurvivesReloadAndProfileCopiesStayIndependent()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                var path = Path.Combine(folder, "equipment.json");
                var library = new EquipmentLibrary(path);
                var source = new DeviceDefinition { Name = "Мой прибор", SlaveId = 23,
                    IdentificationValues = new() { 9, 10 }, Registers = new() { new() { Address = 1700, Name = "Статус" } } };
                var saved = library.Save(source);
                source.Registers[0].Name = "Изменено после сохранения";
                var loaded = new EquipmentLibrary(path).Load().Single(e => e.Id == saved.Id);
                Assert.Equal("Статус", loaded.Device.Registers[0].Name);
                var first = EquipmentLibrary.Instantiate(loaded, new byte[] { 1 });
                var second = EquipmentLibrary.Instantiate(loaded, new byte[] { 1, 2 });
                Assert.Equal(2, first.SlaveId); Assert.Equal(3, second.SlaveId);
                first.Registers[0].Name = "Только профиль 1";
                Assert.Equal("Статус", second.Registers[0].Name);
                loaded.Device.Name = "Обновлённый прибор"; library.Save(loaded.Device, loaded.Id);
                Assert.Equal("Мой прибор", second.Name);
                var bytes = File.ReadAllText(path);
                Assert.Throws<InvalidOperationException>(() => library.Save(loaded.Device));
                Assert.Equal(bytes, File.ReadAllText(path));
                loaded.Device.Registers.Clear();
                Assert.Throws<InvalidOperationException>(() => library.Save(loaded.Device, loaded.Id));
                Assert.Equal(bytes, File.ReadAllText(path));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }, CancellationToken.None);
    }

    [Fact]
    public void CorruptLibraryCannotBeSilentlyOverwritten()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, "broken json");
            var library = new EquipmentLibrary(path);
            Assert.ThrowsAny<Exception>(() => library.Save(new() { Registers = new() { new() { Address = 0 } } }));
            Assert.Equal("broken json", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CatalogAddsRepeatedUnitsToDraftWithoutChangingProfileUntilApply()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            var library = new EquipmentLibrary(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
            var window = new HardwareEditorWindow(vm, library);
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var catalog = window.GetVisualDescendants().OfType<ListBox>().Single(c => c.Name == "EquipmentCatalogList");
                Assert.NotEmpty(catalog.Items);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                window.CaptureRenderedFrame()!.Save(Path.Combine(Path.GetTempPath(), "tfortis-equipment-catalog.png"));
                var add = window.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "AddCatalogEquipment");
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var grid = window.GetVisualDescendants().OfType<DataGrid>().Single(c => c.Name == "ProfileEquipmentGrid");
                var devices = grid.ItemsSource.Cast<DeviceDefinition>().ToArray();
                Assert.Equal(2, devices.Length);
                Assert.NotEqual(devices[0].SlaveId, devices[1].SlaveId);
                Assert.NotSame(devices[0].Registers, devices[1].Registers);
                Assert.Null(vm.RootGraph.Hardware);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                window.CaptureRenderedFrame()!.Save(Path.Combine(Path.GetTempPath(), "tfortis-profile-equipment.png"));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
