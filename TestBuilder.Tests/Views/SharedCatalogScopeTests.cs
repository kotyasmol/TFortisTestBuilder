using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.ViewModels;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class SharedCatalogScopeTests
{
    [Fact]
    public async Task EditingALabelForANodeNeverWritesTheSharedCatalog()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            var templates = LabelTemplateCatalog.Defaults();
            var path = LabelTemplateStore.LocalPath(AppSettings.Instance.ResolvedGraphsFolder);
            LabelTemplateStore.Save(path, templates);
            var bytes = File.ReadAllBytes(path); var source = templates[0].Copy();
            var before = JsonSerializer.Serialize(source);
            var owner = new Window(); var window = new LabelTemplatesWindow(source, profileOnly: true);
            try
            {
                owner.Show(); var task = window.ShowDialog<LabelTemplateDefinition?>(owner); Layout(window);
                Assert.Equal("Этикетка ноды профиля", window.Title);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && Equals(b.Content, "Импорт каталога…"));
                var name = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == source.Name);
                name.Text = "Макет только для ноды"; Layout(window);
                window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Применить к ноде"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var selected = await task;
                Assert.NotNull(selected); Assert.Equal("Макет только для ноды", selected.Name);
                Assert.Equal(before, JsonSerializer.Serialize(source)); Assert.Equal(bytes, File.ReadAllBytes(path));
            }
            finally { window.Close(); owner.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ImportPreviewAndCatalogEditsDoNotChangeTheOpenOrSavedProfile()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            var source = new EquipmentLibrary(Path.Combine(clean.Root, "source.json"));
            var shared = new EquipmentLibrary(Path.Combine(clean.Root, "shared.json"));
            var template = source.Save(new DeviceDefinition { Name = "Контроллер", Registers = new() { new() { Address = 10, Name = "До обновления" } } });
            shared.ApplyImport(shared.PreviewImport(source.ExportJson()));
            var window = new MainWindow { Width = 1100, Height = 760 };
            var shell = (MainWindowViewModel)window.DataContext!; shell.SettingsVM.IsEngineerMode = true;
            shell.TestVM.ApplyHardware(new HardwareConfiguration { Devices = new() { EquipmentLibrary.Instantiate(template, Array.Empty<byte>()) } });
            var before = GraphSerializer.Serialize(shell.TestVM, "Профиль со своей картой");
            var savedPath = Path.Combine(clean.Profiles, "scope-test.json"); File.WriteAllText(savedPath, before);
            var savedBytes = File.ReadAllBytes(savedPath);
            template.Device.Registers[0].Address = 20; source.Save(template.Device, template.Id);
            window.EquipmentLibraryFactory = () => new EquipmentLibraryView(shared);
            window.HardwareEditorFactory = vm => new HardwareEditorView(vm, shared);
            try
            {
                window.Show(); Layout(window);
                Click(Find<Button>(window, "EquipmentLibraryNavigationButton"));
                var view = Assert.Single(window.GetVisualDescendants().OfType<EquipmentLibraryView>());
                Click(Find<Button>(window, "ProfileHardwareButton"));
                shell.OpenSettingsCommand.Execute(null);
                Assert.False(shell.IsHardwareOpen); Assert.False(shell.IsSettingsOpen); Assert.True(shell.IsEquipmentLibraryOpen);
                var bytes = File.ReadAllBytes(shared.StoragePath);
                view.PreviewImport(source.ExportJson()); Layout(window);
                Assert.Contains("Обновить: 1", Find<TextBlock>(window, "EquipmentImportSummary").Text);
                Capture(window, "import-preview");
                Assert.Equal(bytes, File.ReadAllBytes(shared.StoragePath));
                Click(Find<Button>(window, "BackToEquipmentLibrary"));
                Assert.Equal(bytes, File.ReadAllBytes(shared.StoragePath));
                view.PreviewImport(source.ExportJson()); Layout(window);
                Click(Find<Button>(window, "ConfirmEquipmentLibraryImport"));
                Assert.Equal(20, shared.Load().Single(e => e.Id == template.Id).Device.Registers[0].Address);
                Assert.Equal(before, GraphSerializer.Serialize(shell.TestVM, "Профиль со своей картой"));
                Assert.Equal(savedBytes, File.ReadAllBytes(savedPath)); Assert.Empty(window.OwnedWindows);
                view.Finish(); Layout(window);
                Capture(window, "profile-tools");
                var profileButton = Find<Button>(window, "ProfileHardwareButton");
                Assert.True(profileButton.IsEffectivelyVisible);
                Assert.Equal("Оборудование профиля…", profileButton.Content);
                Click(profileButton);
                Assert.True(shell.IsHardwareOpen); Assert.False(shell.IsEquipmentLibraryOpen);
                Assert.Equal(10, Find<DataGrid>(window, "ProfileRegistersGrid").ItemsSource.Cast<RegisterDefinition>().Single().Address);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<TabItem>(), item => Equals(item.Header, "Каталог оборудования"));
                Capture(window, "profile-hardware");
                Assert.Equal(savedBytes, File.ReadAllBytes(savedPath));
            }
            finally { window.Close(); }
            void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window); }
        }, CancellationToken.None);
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Layout(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); Dispatcher.UIThread.RunJobs(); }
    private static void Capture(Window window, string step)
    {
        var folder = Path.Combine(Path.GetTempPath(), "tfortis-profile-scope-20261123"); Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(folder, step + ".png"));
    }
}
