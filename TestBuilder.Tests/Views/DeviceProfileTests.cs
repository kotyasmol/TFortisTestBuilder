using Avalonia;
using Avalonia.Automation;
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
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class DeviceProfileTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task NewDeviceCanBeAddedToCatalogAndSelectedInSavedProfiles(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "device-profile-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var oldFolder = AppSettings.Instance.GraphsFolder;
            var oldTheme = Application.Current!.RequestedThemeVariant;
            AppSettings.Instance.GraphsFolder = folder;
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            using var view = new GraphEditorView { DataContext = vm };
            var window = new Window { Width = 1000, Height = 750, Content = view };
            try
            {
                vm.NewProfileCommand.Execute(null); window.Show(); Layout();
                var kind = view.FindControl<ComboBox>("ProfileProductKindInput")!;
                kind.SelectedItem = "Плата"; Layout();
                Assert.Equal("Board", vm.RootGraph.ProductKind);
                var model = view.FindControl<ComboBox>("DeviceModelPicker")!;
                Assert.Null(model.SelectedItem);
                Click(view.FindControl<Button>("AddCatalogModelButton")!, window);
                var dialog = Assert.Single(window.OwnedWindows.OfType<CatalogSerialsWindow>());
                var modelName = dialog.GetVisualDescendants().OfType<TextBox>()
                    .Single(field => AutomationProperties.GetName(field) == "Название изделия");
                var saveModel = dialog.GetVisualDescendants().OfType<Button>()
                    .Single(button => Equals(button.Content, "Сохранить в справочник"));
                modelName.Text = "PSW 2G6F plus";
                Click(saveModel, dialog);
                Assert.True(dialog.IsVisible);
                Assert.Null(model.SelectedItem);
                Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(),
                    field => field.Text?.Contains("уже есть в справочнике") == true);
                modelName.Text = "PSV2G4H+";
                Click(saveModel, dialog);
                Assert.False(dialog.IsVisible);
                var added = Assert.IsType<EquipmentModel>(model.SelectedItem);
                Assert.Equal("PSV2G4H+", added.Name);
                Assert.Equal(added.Id, EquipmentCatalog.Load(folder).Resolve(null, "PSV2G4H+")?.Id);
                Assert.False(vm.UseCatalogSerials);
                var title = Field("Название профиля");
                var titleText = title.GetVisualDescendants().OfType<TextBox>().Single();
                Assert.Equal("PSV2G4H+", vm.DeviceName);
                Assert.Equal("PSV2G4H+", vm.CurrentProfileName);
                titleText.Focus(); titleText.SelectAll(); window.KeyTextInput("Проверка питания");
                SaveClick();
                await vm.SaveGraphCommand.ExecutionTask!; Layout();
                Assert.Equal("PSV2G4H+ · Проверка питания", vm.CurrentProfileName);
                Assert.Equal("Проверка питания", vm.RootGraph.ConfigurationName);
                Assert.Equal("PSV2G4H+", Assert.Single(vm.EngineerProfileGroups).Name);
                Assert.Single(vm.Profiles);
                var first = vm.SelectedProfile!;
                Assert.Equal("Board", first.ProductKind);
                var bytes = File.ReadAllBytes(first.FilePath);
                Assert.Equal("PSV2G4H+", first.DeviceModel);
                Assert.Equal(added.Id, first.DeviceModelId);
                Assert.Contains("Проверка питания", vm.ProfileNameSuggestions);
                titleText.Focus(); titleText.SelectAll(); window.KeyTextInput("Проверка"); Layout();
                Assert.True(title.IsDropDownOpen);
                // Suggestions are optional: free text must survive Save even while the popup is open.
                titleText.SelectAll(); window.KeyTextInput("Другой профиль");
                SaveClick(); await vm.SaveGraphCommand.ExecutionTask!; Layout();
                Assert.Equal(2, vm.Profiles.Count);
                Assert.Equal("PSV2G4H+ · Другой профиль", vm.CurrentProfileName);
                Assert.Equal("Другой профиль", vm.RootGraph.ConfigurationName);
                Assert.Equal(bytes, File.ReadAllBytes(first.FilePath));
                Assert.Equal(2, Assert.Single(vm.StationProfileGroups).Profiles.Count);
                vm.NewProfileCommand.Execute(null); Layout(); Assert.Null(model.SelectedItem);
                Assert.Equal("Коммутатор", vm.ProfileProductKind);
                model.SelectedItem = model.Items.OfType<EquipmentModel>().Single(item => item.Id == added.Id);
                Layout(); Assert.Equal("PSV2G4H+", vm.DeviceName);
                Assert.Equal("Плата", vm.ProfileProductKind);
                using var reloaded = new TestViewModel(bus, new SlaveManager(bus));
                reloaded.SelectedProfile = reloaded.Profiles.First();
                Assert.Equal("PSV2G4H+", reloaded.DeviceName);
                Assert.Equal("Board", reloaded.RootGraph.ProductKind);
                Assert.Equal(added.Id, reloaded.SelectedEquipmentModel?.Id);
                var artifacts = Environment.GetEnvironmentVariable("TFORTIS_UI_ARTIFACTS");
                if (artifacts != null)
                {
                    Directory.CreateDirectory(artifacts);
                    using var frame = window.CaptureRenderedFrame();
                    frame?.Save(Path.Combine(artifacts, $"device-profile-{dark}.png"));
                }
            }
            finally
            {
                window.Close(); AppSettings.Instance.GraphsFolder = oldFolder;
                Application.Current.RequestedThemeVariant = oldTheme; Directory.Delete(folder, true);
            }
            return true;
            AutoCompleteBox Field(string name) => view.GetVisualDescendants().OfType<AutoCompleteBox>().Single(x => AutomationProperties.GetName(x) == name);
            void Layout() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); Dispatcher.UIThread.RunJobs(); }
            void SaveClick()
            {
                var save = view.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.SaveGraphCommand));
                Click(save, window);
            }
            void Click(Button button, Window host)
            {
                host.UpdateLayout();
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), host)!.Value;
                host.MouseDown(point, MouseButton.Left); host.MouseUp(point, MouseButton.Left); Layout();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CatalogMenuKeepsSavedVariantAndSelectsTheCodeShownWithItsName()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            GraphSerializer.Deserialize("""
                {"name":"Проверка","deviceModelId":"psw-2g-plus-ups-box",
                 "deviceModel":"PSW-2G+UPS-Box","useCatalogSerials":true,"labelVariant":8,
                 "nodes":[],"connections":[]}
                """, vm);
            using var view = new GraphEditorView { DataContext = vm };
            var window = new Window { Width = 780, Height = 600, Content = view };
            try
            {
                window.Show();
                Layout();
                var catalogButton = view.FindControl<Button>("CatalogToolsButton")!;
                var flyout = Assert.IsType<Flyout>(catalogButton.Flyout);
                flyout.ShowAt(catalogButton);
                Layout();

                var variants = Assert.IsType<StackPanel>(flyout.Content).GetVisualDescendants()
                    .OfType<ComboBox>().Single();
                Assert.True(variants.IsEffectivelyVisible);
                Assert.Equal(8, vm.ProfileLabelVariant);
                Assert.Equal(8, Assert.IsType<CatalogLabelVariant>(variants.SelectedItem).Code);
                var option = variants.Items.OfType<CatalogLabelVariant>().Single(item => item.Code == 2);
                Assert.Equal("2 · Plastim + PWR300", option.DisplayName);
                variants.SelectedItem = option;
                Layout();
                Assert.Equal(2, vm.ProfileLabelVariant);

                vm.SelectedEquipmentModel = vm.EquipmentModels.Single(model => model.Id == "psw-2g6f-plus");
                Layout();
                Assert.True(vm.UseCatalogSerials);
                Assert.Equal(0, vm.ProfileLabelVariant);
                Assert.False(variants.IsEffectivelyVisible);
            }
            finally { window.Close(); }

            void Layout()
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                Dispatcher.UIThread.RunJobs();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void UnknownDeviceRemainsInProfile_AndChangingModelCanBeUndone()
    {
        var folder = Path.Combine(Path.GetTempPath(), "device-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "catalog"));
        File.WriteAllText(Path.Combine(folder, "catalog", "equipment-models.json"), """{"schemaVersion":1,"models":[{"id":"known","name":"Fixture-2G6F+","aliases":["Fixture 2G6F plus"]}]}""");
        var old = AppSettings.Instance.GraphsFolder;
        AppSettings.Instance.GraphsFolder = folder;
        try
        {
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
            GraphSerializer.Deserialize("""
                {"name":"Старый профиль","deviceModelId":"legacy-id","deviceModel":"Старое изделие",
                 "configurationName":"Проверка","nodes":[],"connections":[]}
                """, vm);
            Assert.Null(vm.SelectedEquipmentModel);
            Assert.DoesNotContain(vm.EquipmentModels, model => model.Id == "legacy-id");
            Assert.Equal("Старое изделие · вне справочника", vm.EquipmentPlaceholder);
            Assert.Equal("Старое изделие", vm.NewCatalogModel().Name);

            vm.SelectedEquipmentModel = vm.EquipmentModels.Single(model => model.Id == "known");
            Assert.Equal("known", vm.RootGraph.DeviceModelId);
            Assert.Equal("Fixture-2G6F+", vm.DeviceName);
            Assert.Equal("Fixture-2G6F+ · Проверка", vm.CurrentProfileName);
            vm.SelectedEquipmentModel = vm.EquipmentModels.Single(model => model.Id == "psw-2g");
            Assert.Equal("psw-2g", vm.RootGraph.DeviceModelId);
            vm.UndoCommand.Execute(null);
            Assert.Equal("known", vm.RootGraph.DeviceModelId);
            Assert.Equal("Fixture-2G6F+", vm.DeviceName);
            vm.UndoCommand.Execute(null);
            Assert.Null(vm.SelectedEquipmentModel);
            Assert.Equal("legacy-id", vm.RootGraph.DeviceModelId);
            Assert.Equal("Старое изделие", vm.DeviceName);
            Assert.Equal("Старый профиль", vm.CurrentProfileName);
            vm.RedoCommand.Execute(null); Assert.Equal("known", vm.RootGraph.DeviceModelId);
        }
        finally { AppSettings.Instance.GraphsFolder = old; Directory.Delete(folder, true); }
    }
}
