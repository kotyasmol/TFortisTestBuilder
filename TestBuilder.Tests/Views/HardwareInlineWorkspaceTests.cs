using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.ViewModels;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class HardwareInlineWorkspaceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EquipmentFormsStayInsideTheMainWindowAndDoNotAccumulate(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            var previous = Application.Current!.RequestedThemeVariant;
            var window = new MainWindow { Width = 1100, Height = 680 };
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var library = new EquipmentLibrary(Path.Combine(clean.Root, "inline-library.json"));
            window.HardwareEditorFactory = test => new HardwareEditorView(test, library);
            window.EquipmentLibraryFactory = () => new EquipmentLibraryView(library);
            var vm = (MainWindowViewModel)window.DataContext!;
            vm.SettingsVM.IsEngineerMode = true;
            var original = GraphSerializer.Serialize(vm.TestVM, "До открытия");
            try
            {
                window.Show(); Layout(window);
                var bounds = window.ClientSize;
                var open = Find<Button>(window, "ProfileHardwareButton"); Click(window, open);
                Assert.True(vm.IsHardwareOpen); Assert.False(vm.IsWorkspaceOpen);
                var view = Assert.Single(window.GetVisualDescendants().OfType<HardwareEditorView>());
                // Even queued/repeated clicks cannot create another view or window.
                open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window);
                Assert.Same(view, Assert.Single(window.GetVisualDescendants().OfType<HardwareEditorView>()));
                Assert.Empty(window.OwnedWindows);
                for (var i = 0; i < 4; i++)
                {
                    Click(window, Find<Button>(window, "AddFirstProfileEquipment"));
                    var picker = Assert.Single(window.GetVisualDescendants().OfType<AddProfileEquipmentView>());
                    Assert.Empty(window.OwnedWindows); Assert.Equal(bounds, window.ClientSize);
                    if (i == 0) Capture(window, "add", dark);
                    Click(window, Find<Button>(window, "BackFromHardwareForm"));
                    Assert.Null(picker.GetVisualRoot());
                    Assert.Empty(window.GetVisualDescendants().OfType<AddProfileEquipmentView>());
                }
                Assert.DoesNotContain(view.GetVisualDescendants().OfType<TabItem>(), t => Equals(t.Header, "Каталог оборудования"));
                Assert.Contains(vm.TestVM.CurrentProfileName, Find<TextBlock>(window, "HardwareProfileContext").Text);
                Click(window, "Отменить изменения оборудования");
                var shared = Find<Button>(window, "EquipmentLibraryNavigationButton"); Click(window, shared);
                Assert.True(vm.IsEquipmentLibraryOpen); Assert.False(vm.IsHardwareOpen);
                var catalogView = Assert.Single(window.GetVisualDescendants().OfType<EquipmentLibraryView>());
                shared.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window);
                Assert.Same(catalogView, Assert.Single(window.GetVisualDescendants().OfType<EquipmentLibraryView>()));
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && Equals(b.Content, "Применить к профилю"));
                Click(window, "Новое оборудование…");
                var editor = Assert.Single(window.GetVisualDescendants().OfType<EquipmentTemplateEditor>());
                Assert.Empty(window.OwnedWindows); Assert.Equal(bounds, window.ClientSize);
                Find<TextBox>(window, "EquipmentName").Text = "Черновик без сохранения";
                Capture(window, "new-template", dark);
                Click(window, Find<Button>(window, "BackToEquipmentLibrary")); Assert.Null(editor.GetVisualRoot());
                // A saved catalog template survives, while its profile instances remain draft until Apply.
                Click(window, "Новое оборудование…");
                Find<TextBox>(window, "EquipmentName").Text = "Прибор из встроенной формы";
                Click(window, "Сохранить в каталог");
                Assert.Equal(original, GraphSerializer.Serialize(vm.TestVM, "До открытия"));
                Capture(window, "shared-catalog", dark);
                Click(window, "Вернуться к работе"); Click(window, open);
                Click(window, Find<Button>(window, "AddFirstProfileEquipment"));
                var list = Find<ListBox>(window, "EquipmentCatalogList");
                list.SelectedItem = list.Items.OfType<EquipmentTemplate>().Single(e => e.Device.Name == "Прибор из встроенной формы"); Layout(window);
                Find<TextBox>(window, "EquipmentCount").Text = "2";
                Find<TextBox>(window, "EquipmentFirstSlave").Text = "17";
                Click(window, Find<Button>(window, "ConfirmAddEquipment"));
                Assert.Equal(2, Find<DataGrid>(window, "ProfileEquipmentGrid").ItemsSource.Cast<DeviceDefinition>().Count());
                Assert.Equal(original, GraphSerializer.Serialize(vm.TestVM, "До открытия"));
                Click(window, Find<Button>(window, "AddProfileRegister"));
                Assert.Single(window.GetVisualDescendants().OfType<RegisterDefinitionEditor>());
                Assert.Empty(window.OwnedWindows); Assert.Equal(bounds, window.ClientSize);
                Find<TextBox>(window, "RegisterDefinitionName").Text = "Регистр встроенной формы";
                Capture(window, "register", dark);
                Click(window, Find<Button>(window, "SaveRegisterDefinition"));
                Click(window, "Применить к профилю");
                Assert.False(vm.IsHardwareOpen); Assert.True(vm.IsWorkspaceOpen);
                Assert.Empty(window.GetVisualDescendants().OfType<HardwareEditorView>());
                Assert.Equal(2, vm.TestVM.RootGraph.Hardware!.Devices.Count);
                Assert.Contains(vm.TestVM.RootGraph.Hardware.Devices[0].Registers, r => r.Name == "Регистр встроенной формы");
                var applied = GraphSerializer.Serialize(vm.TestVM, "После применения");
                Click(window, open); Click(window, Find<Button>(window, "AddProfileEquipment"));
                Click(window, "Отменить изменения оборудования");
                Assert.False(vm.IsHardwareOpen); Assert.Empty(window.OwnedWindows);
                Assert.Equal(applied, GraphSerializer.Serialize(vm.TestVM, "После применения"));
            }
            finally { window.Close(); Application.Current.RequestedThemeVariant = previous; }
        }, CancellationToken.None);
    }

    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Layout(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); Dispatcher.UIThread.RunJobs(); }
    private static void Click(Window window, string caption) => Click(window, window.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && Equals(b.Content, caption)));
    private static void Click(Window window, Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window); }
    private static void Capture(Window window, string step, bool dark)
    {
        var folder = Path.Combine(Path.GetTempPath(), "tfortis-hardware-inline-20261123"); Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(folder, step + (dark ? "-dark" : "-light") + ".png"));
    }
}
