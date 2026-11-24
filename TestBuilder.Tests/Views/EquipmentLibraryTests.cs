using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.Views;
using TestBuilder.Presentation;
using System.Text.Json;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class EquipmentLibraryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogPreviewsBuiltInAndCustomMapsWithoutAddingOrChangingEquipment(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            var theme = Application.Current!.RequestedThemeVariant; Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            var library = new EquipmentLibrary(path);
            var custom = library.Save(new DeviceDefinition { Name = "Свой шаблон для просмотра", IdentificationValues = new() { 5, 8 }, StandSerialNumberAddress = 3,
                Registers = new() { new() { Address = 3, Name = "Номер стенда" }, new() { Address = 12, Name = "PoeMode", Writable = true, RetryWriteOnMismatch = true, Unit = "мА", Category = "Контроль" } } });
            var bytes = File.ReadAllBytes(path); var catalogBefore = JsonSerializer.Serialize(library.Load());
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            var graph = GraphSerializer.Serialize(vm, vm.CurrentProfileName);
            var window = new Window { Content = new EquipmentLibraryView(library), Width = dark ? 1180 : 1000, Height = dark ? 780 : 650 };
            try
            {
                window.Show(); Layout();
                var catalog = Find<ListBox>("EquipmentCatalogList"); var grid = Find<DataGrid>("CatalogPreviewRegisters");
                Assert.NotEmpty(grid.ItemsSource.Cast<object>()); // First template is immediately visible.
                Find<TextBox>("CatalogRegisterSearch").Text = "not found";
                catalog.SelectedItem = catalog.Items.OfType<EquipmentTemplate>().Single(e => e.Id == custom.Id); Layout();
                Assert.Equal("", Find<TextBox>("CatalogRegisterSearch").Text);
                Assert.Equal(2, grid.ItemsSource.Cast<object>().Count());
                Assert.Contains("5, 8", Find<TextBlock>("CatalogPreviewDetails").Text);
                Assert.Contains("стенда (хранения): 3", Find<TextBlock>("CatalogPreviewDetails").Text);
                grid.ScrollIntoView(grid.ItemsSource.Cast<object>().Last(), grid.Columns[1]); Layout();
                Assert.Contains(grid.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == RussianUiText.Display("PoeMode"));
                Assert.True(grid.IsReadOnly); Assert.All(grid.Columns, column => Assert.True(column.IsReadOnly));
                Assert.False(grid.BeginEdit());
                Find<TextBox>("CatalogRegisterSearch").Text = "12"; Layout();
                var register = Assert.Single(grid.ItemsSource.Cast<EquipmentTemplateBrowser.CatalogRegisterPreview>());
                Assert.Equal("PoeMode", register.Name); Assert.Equal("RW · запись", register.Access); Assert.Equal("Да", register.Repeat); Assert.Equal("мА", register.Unit);
                catalog.SelectedItem = catalog.Items.OfType<EquipmentTemplate>().Single(e => e.Device.Name == "PS-3"); Layout();
                Assert.Equal(7, grid.ItemsSource.Cast<object>().Count());
                Assert.Equal("PS-3", Find<TextBlock>("CatalogPreviewTitle").Text);
                Find<TextBox>("CatalogRegisterSearch").Text = "1216"; Layout();
                register = Assert.Single(grid.ItemsSource.Cast<EquipmentTemplateBrowser.CatalogRegisterPreview>());
                Assert.Equal("RO · чтение", register.Access);
                Find<TextBox>("CatalogRegisterSearch").Text = ""; Layout();
                Assert.Equal(7, grid.ItemsSource.Cast<object>().Count());
                using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(Path.GetTempPath(), $"tfortis-equipment-preview-{dark}.png"));
                Find<TextBox>("CatalogRegisterSearch").Text = "absent register"; Layout();
                Assert.Empty(grid.ItemsSource.Cast<object>()); Assert.Contains("не найдены", Find<TextBlock>("CatalogRegisterCount").Text);
                Find<TextBox>("CatalogRegisterSearch").Text = "";
                window.GetVisualDescendants().OfType<TextBox>().Single(c => c.Watermark?.ToString() == "Найти оборудование по названию").Text = "absent equipment"; Layout();
                Assert.Null(catalog.SelectedItem); Assert.Empty(grid.ItemsSource.Cast<object>()); Assert.False(Find<TextBox>("CatalogRegisterSearch").IsEnabled);
                Assert.Equal(graph, GraphSerializer.Serialize(vm, vm.CurrentProfileName));
                Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Equal(catalogBefore, JsonSerializer.Serialize(library.Load()));
            }
            finally { window.Close(); File.Delete(path); Application.Current.RequestedThemeVariant = theme; }
            T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
            void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); Dispatcher.UIThread.RunJobs(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScrollingEquipmentKeepsNamesBoundToTheCurrentDevice(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var oldTheme = Avalonia.Application.Current!.RequestedThemeVariant;
            Avalonia.Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            var presets = HardwarePresets.Load();
            var devices = Enumerable.Range(1, 100).Select(i =>
            {
                var device = TestViewModel.CopyDefinition(presets.Single(d => d.Name == (i % 2 == 0 ? "SIMBAT-24" : "EL-60v5")));
                device.SlaveId = (byte)i; device.Name += $" / {i}";
                return device;
            }).ToList();
            var expectedDevices = System.Text.Json.JsonSerializer.Serialize(devices);
            vm.RootGraph.Hardware = new HardwareConfiguration { Devices = devices };
            var window = new HardwareEditorWindow(vm);
            try
            {
                window.Show(); Layout();
                var grid = window.GetVisualDescendants().OfType<DataGrid>().Single(c => c.Name == "ProfileEquipmentGrid");
                foreach (var index in new[] { 99, 40, 0, 70, 1 })
                {
                    var device = grid.ItemsSource.Cast<DeviceDefinition>().ElementAt(index);
                    grid.SelectedItem = device; grid.ScrollIntoView(device, grid.Columns[1]); Layout();
                    var row = grid.GetVisualDescendants().OfType<DataGridRow>().Single(r => ReferenceEquals(r.DataContext, device));
                    var labels = row.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
                    Assert.True(labels.Contains(device.Name), $"Ожидалось «{device.Name}», показано: {string.Join(" / ", labels)}");
                    Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == $"Регистры: {device.Name}");
                }
                using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(Path.GetTempPath(), $"tfortis-equipment-scroll-{dark}.png"));
                window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Применить к профилю"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(window.IsVisible);
                Assert.Equal(expectedDevices, System.Text.Json.JsonSerializer.Serialize(vm.RootGraph.Hardware!.Devices));
                GraphSerializer.Deserialize(GraphSerializer.Serialize(vm, "Проверка названий"), vm);
                Assert.Equal(expectedDevices, System.Text.Json.JsonSerializer.Serialize(vm.RootGraph.Hardware!.Devices));
            }
            finally { window.Close(); Avalonia.Application.Current.RequestedThemeVariant = oldTheme; }
            void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); }
        }, CancellationToken.None);
    }

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
                editor.GetVisualDescendants().OfType<Expander>().Single().IsExpanded = true; editor.UpdateLayout(); Dispatcher.UIThread.RunJobs();
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
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                window.CaptureRenderedFrame()!.Save(Path.Combine(Path.GetTempPath(), "tfortis-equipment-catalog.png"));
                for (var i = 0; i < 2; i++)
                {
                    var add = window.GetVisualDescendants().OfType<Button>().Single(c => c.IsEffectivelyVisible && c.Name == (i == 0 ? "AddFirstProfileEquipment" : "AddProfileEquipment"));
                    add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    var picker = Assert.Single(window.GetVisualDescendants().OfType<AddProfileEquipmentView>()); picker.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    picker.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "ConfirmAddEquipment").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs();
                }
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
