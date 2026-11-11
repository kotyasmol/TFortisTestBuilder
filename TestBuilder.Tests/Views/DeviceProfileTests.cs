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
using TestBuilder.Tests.SerializationTests;
using TestBuilder.ViewModels;
using TestBuilder.Views;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class DeviceProfileTests
{
    [Fact]
    public async Task CatalogWindowCreatesAndEditsRtlModelWithoutSwitchingTheOpenProfile()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var folder = new ProfileCopyTests.ProfileFolder();
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            vm.SelectedEquipmentModel = vm.EquipmentModels.Single(model => model.Id == "psw-plus-box-8x2pro");
            var openProfile = GraphSerializer.Serialize(vm, vm.CurrentProfileName);
            var catalog = new EquipmentCatalogWindow(vm);
            try
            {
                catalog.Show();
                Layout(catalog);
                Press(catalog, "Добавить изделие…");
                var card = Assert.Single(catalog.OwnedWindows.OfType<CatalogSerialsWindow>());
                Layout(card);
                Field(card, "Название изделия").Text = "Тестовая плата X1";
                Field(card, "Альтернативные названия").Text = "Fixture X1";
                Field(card, "Код модели на этикетке").Text = "321";
                Field(card, "Название комплектации 2").Text = "DIN";
                SelectTab(card, 1);
                Field(card, "Тип на сервере").Text = "RTL-X1";
                SelectTab(card, 2);
                Toggle(card, "Использовать отчёт RTL").IsChecked = true;
                Field(card, "Числовой тип отчёта на сервере").Text = "17";
                Field(card, "Переменная идентификатора платы").Text = "McuId";
                Field(card, "Служебный идентификатор отказов").Text = "fixture-failures";
                Toggle(card, "Получать серийный номер из ответа на отчёт").IsChecked = true;
                Press(card, "Сохранить в справочник");
                Assert.False(card.IsVisible);
                var created = Assert.IsType<EquipmentModel>(EquipmentCatalog.Load(folder.Path).Resolve(null, "Fixture X1"));
                Assert.Equal("RTL-X1", created.EffectiveServerDeviceType);
                Assert.Equal(321, created.LabelCode);
                Assert.Equal("DIN", created.LabelVariants![2]);
                Assert.Equal(new RtlReportSettings(17, true) { DeviceIdentifierVariable = "McuId", FailureDeviceIdentifier = "fixture-failures" }, created.RtlReport);

                var search = Assert.Single(catalog.GetVisualDescendants().OfType<TextBox>());
                search.Text = "Fixture X1";
                Layout(catalog);
                Assert.Equal(created.Id, Assert.IsType<EquipmentModel>(Assert.Single(catalog.GetVisualDescendants().OfType<ListBox>()).SelectedItem).Id);
                Press(catalog, "Редактировать изделие…");
                card = Assert.Single(catalog.OwnedWindows.OfType<CatalogSerialsWindow>());
                Layout(card);
                Field(card, "Название изделия").Text = "Плата X1";
                SelectTab(card, 2);
                Field(card, "Служебный идентификатор отказов").Text = "fixture-new-failures";
                Press(card, "Сохранить в справочник");
                Assert.False(card.IsVisible);
                var renamed = Assert.IsType<EquipmentModel>(EquipmentCatalog.Load(folder.Path).Resolve(null, "Тестовая плата X1"));
                Assert.Equal(created.Id, renamed.Id);
                Assert.Equal("Плата X1", renamed.Name);
                Assert.Equal("fixture-new-failures", renamed.RtlReport!.FailureDeviceIdentifier);
                Assert.Equal(openProfile, GraphSerializer.Serialize(vm, vm.CurrentProfileName));
            }
            finally { catalog.Close(); }

            static void Layout(Window window)
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }

            static void Press(Window window, string caption)
            {
                window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, caption))
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Layout(window);
            }

            static TextBox Field(Window window, string caption) => window.GetVisualDescendants().OfType<TextBox>()
                .Single(field => AutomationProperties.GetName(field) == caption);

            static CheckBox Toggle(Window window, string caption) => window.GetVisualDescendants().OfType<CheckBox>()
                .Single(field => Equals(field.Content, caption));

            static void SelectTab(Window window, int index)
            {
                window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = index;
                Layout(window);
            }
        }, CancellationToken.None);
    }

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
    public async Task SeriesBadgeShowsFullRangeAndRefreshesWhenModelOrRangeChanges()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var folder = new ProfileCopyTests.ProfileFolder();
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            using var view = new GraphEditorView { DataContext = vm };
            var window = new Window { Width = 780, Height = 600, Content = view };
            try
            {
                window.Show();
                var badge = view.FindControl<TextBlock>("CatalogSerialSeriesBadge")!;
                Layout();
                Assert.False(badge.IsEffectivelyVisible);

                SelectModel("psw-plus-ex-8x2pro");
                Assert.True(badge.IsEffectivelyVisible);
                Assert.Equal("Серийные номера: 3500001–3565535", badge.Text);
                Assert.Equal("Тип на сервере: PSW+Ex 8x2Pro\nСерийные номера: от 3500001 до 3565535", ToolTip.GetTip(Assert.IsType<Border>(badge.Parent)));
                var modelPicker = view.FindControl<ComboBox>("DeviceModelPicker")!;
                var kindPicker = view.FindControl<ComboBox>("ProfileProductKindInput")!;
                var modelCenter = modelPicker.TranslatePoint(new Rect(modelPicker.Bounds.Size).Center, view)!.Value.Y;
                var kindCenter = kindPicker.TranslatePoint(new Rect(kindPicker.Bounds.Size).Center, view)!.Value.Y;
                Assert.InRange(Math.Abs(modelCenter - kindCenter), 0, 1);
                foreach (var picker in new[] { modelPicker, kindPicker })
                {
                    var label = picker.GetVisualDescendants().OfType<TextBlock>().First(text => !string.IsNullOrWhiteSpace(text.Text));
                    Assert.True(label.TextLayout.WidthIncludingTrailingWhitespace <= label.Bounds.Width + 1);
                    Assert.True(label.TextLayout.Height <= label.Bounds.Height + 1);
                }
                var artifacts = Environment.GetEnvironmentVariable("TFORTIS_UI_ARTIFACTS");
                if (artifacts != null)
                {
                    Directory.CreateDirectory(artifacts);
                    using var frame = window.CaptureRenderedFrame();
                    frame?.Save(Path.Combine(artifacts, "graph-serial-series.png"));
                }
                Assert.InRange(view.FindControl<AutoCompleteBox>("ProfileNameInput")!.Bounds.Width, 130, window.ClientSize.Width);

                vm.UseCatalogSerials = false;
                Layout();
                Assert.Equal("Серийные номера: 3500001–3565535", badge.Text);
                Assert.Contains("Профиль использует настройки нод.", Assert.IsType<string>(ToolTip.GetTip((Border)badge.Parent!)));
                SelectModel("psw-plus-box-8x2pro");
                Assert.Equal("Серийные номера: 3300001–3365535", badge.Text);
                SelectModel("rtl-sw");
                Assert.Equal("Серийные номера: выдаёт сервер", badge.Text);
                SelectModel("psw-plus-ups-ex-8x2pro");
                Assert.Equal("Серийные номера: не настроены", badge.Text);

                SelectModel("psw-plus-ex-8x2pro");
                var model = vm.SelectedEquipmentModel!;
                var series = model.SerialSeries! with { FirstSerialNumber = 3500020, LastSerialNumber = 3565500 };
                vm.SaveCatalogModel(model with { SerialSeries = series }, createNew: false);
                Layout();
                Assert.Equal("Серийные номера: 3500020–3565500", badge.Text);
            }
            finally { window.Close(); }

            void SelectModel(string id)
            {
                var picker = view.FindControl<ComboBox>("DeviceModelPicker")!;
                picker.SelectedItem = picker.Items.OfType<EquipmentModel>().Single(model => model.Id == id);
                Layout();
            }

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
