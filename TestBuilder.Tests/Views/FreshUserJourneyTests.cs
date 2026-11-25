using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nodify;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class FreshUserJourneyTests
{
    private static string Evidence => Path.Combine(Path.GetTempPath(), "tfortis-fresh-user-audit-20261121");

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SwitchingToBoardUsesPortableFormatsAndFlagsAnUnreadableCatalog(bool corrupt)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new CleanWorkstation();
            var boardFormat = LabelTemplateCatalog.Defaults().Single(t => !t.UseShortSerial && t.Language == "EPL");
            boardFormat.WidthDots = 700;
            var path = LabelTemplateStore.LocalPath(clean.Profiles);
            LabelTemplateStore.Save(path, new[] { boardFormat });
            if (corrupt) File.WriteAllText(path, "not json");
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            vm.SelectedEquipmentModel = vm.EquipmentModels.First(m => m.RtlReport == null && m.SerialSeries != null);
            var print = new PrintLabelNodeViewModel { LabelTemplate = LabelTemplateCatalog.Defaults().First(t => t.UseShortSerial).Copy() };
            vm.Nodes.Add(print);
            vm.SelectedEquipmentModel = vm.EquipmentModels.First(m => m.RtlReport != null);
            Assert.Equal("EPL", print.LabelTemplate!.Language); Assert.False(print.UseQtProZplFormat);
            if (corrupt) Assert.Contains("Не удалось прочитать каталог макетов", print.ModelReview!.Message);
            else Assert.Equal(700, print.LabelTemplate.WidthDots);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ANewNetworkPlanCanGrowWithoutInventingAdaptersOrTransferringTheirIds()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var clean = new CleanWorkstation();
            BenchAdapter[] cards = Array.Empty<BenchAdapter>();
            var vm = new NetworkSetupViewModel(() => cards, () => true, loadPresets: () => Array.Empty<NetworkConfigurationPreset>());
            var view = new NetworkSetupView { DataContext = vm };
            var window = new Window { Content = view, Width = 1400, Height = 1000 };
            try
            {
                window.Show(); Layout(window); Assert.Empty(vm.Adapters); Assert.False(vm.CanApply);
                Capture(window, "network-01-no-adapters");
                var empty = new BenchNetworkPlan { Addresses = new() };
                var batch = new NetworkPortBatchWindow(empty); var result = batch.ShowDialog<BenchNetworkPlan?>(window); Layout(batch);
                Click(batch, "24 портов");
                batch.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "BatchSpeed").Text = "125";
                Capture(batch, "network-02-new-24-port-plan");
                Click(batch, "Добавить в план"); var plan = Assert.IsType<BenchNetworkPlan>(await result);
                Assert.Empty(empty.Addresses);
                vm.SaveAddressPlan(plan); Assert.Equal(24, vm.Rows.Count); Assert.False(vm.CanApply);
                cards = plan.Addresses.Select((a, i) => new BenchAdapter("pc-a-" + i, "Карта " + i, $"00112233{i:0000}", "Up", a.Ip, 1000)).ToArray();
                Click(window, "Обновить состояние");
                view.FindControl<Expander>("NetworkPlanSection")!.IsExpanded = true; Layout(window);
                Click(window, "Сбросить выбор"); // Explicitly request saved bindings / assignment by current IP.
                Assert.True(vm.CanApply); Assert.Equal(24, vm.Rows.Count);
                Click(window, "Сохранить привязки");
                Assert.Equal(24, AppSettings.Load(clean.SettingsFile).DataTestAdapterIds.Count);
                var portable = new NetworkPresetCatalog { Presets = new() { new("qa", "Мой план 24", plan) } }.ToJson();
                Assert.DoesNotContain("pc-a-", portable);
                var restored = NetworkPresetCatalog.Read(portable).Presets.Single().CopyPlan();
                Assert.Equal(24, restored.Addresses.Count); Assert.All(restored.Addresses, a => Assert.Equal(125, a.TestMbps));
                Assert.Equal(12, restored.BuildDataTestPairs().Split(Environment.NewLine).Length);
                vm.Rows[0].ClearCommand.Execute(null); Assert.False(vm.CanApply);
                Click(window, "Обновить состояние"); Assert.Null(vm.Rows[0].SelectedAdapter);
                Capture(window, "network-03-24-ports-with-missing-binding");
                vm.Rows[0].SelectedAdapter = cards[0]; Assert.True(vm.CanApply);
                var saved = AppSettings.Load(clean.SettingsFile);
                Assert.Equal(24, saved.NetworkPlan.Addresses.Count);
                Assert.All(cards, c => Assert.Equal(1000, c.SpeedMbps));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InvalidNodeNumberDoesNotSilentlySaveThePreviousValue()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var clean = new CleanWorkstation();
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            vm.CurrentProfileName = "Проверка полей";
            vm.AddNodeAtLocation("Задержка", new Point(100, 100));
            var pause = Assert.IsType<DelayNodeViewModel>(Assert.Single(vm.Nodes));
            var path = ProfileFileStorage.DisplayPath(clean.Profiles, vm.CurrentProfileName);
            await vm.SaveGraphToPathAsync(path, createNew: true);
            var original = File.ReadAllText(path);
            var view = new GraphEditorView { DataContext = vm };
            var shell = new Window { Content = view, Width = 1500, Height = 950 };
            try
            {
                shell.Show(); Layout(shell);
                var field = shell.GetVisualDescendants().OfType<TextBox>().Single(t => ReferenceEquals(t.DataContext, pause) && t.Text == "1000");
                field.Text = "2000"; Layout(shell); Assert.Equal(2000, pause.Milliseconds);
                field.Text = "не число"; Layout(shell); Assert.True(DataValidationErrors.GetHasErrors(field));
                Click(shell, shell.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.SaveGraphCommand)));
                await vm.SaveGraphCommand.ExecutionTask!; Layout(shell); Capture(shell, "06-invalid-node-number");
                Assert.Equal(original, File.ReadAllText(path));
                Assert.Contains("пол", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
                var namePrompts = 0; var exports = 0;
                vm.PromptProfileNameAsync = (_, _, _, _) => { namePrompts++; return Task.FromResult<string?>("Копия"); };
                vm.WriteProfileExportAsync = (_, _) => { exports++; return Task.FromResult(true); };
                var profile = Assert.Single(vm.AllProfiles);
                await vm.CopyProfileCommand.ExecuteAsync(profile);
                await vm.ExportProfileCommand.ExecuteAsync(profile);
                Assert.Equal(0, namePrompts); Assert.Equal(0, exports);
                await vm.RunGraphCommand.ExecuteAsync(null);
                Assert.False(vm.IsTestRunning); Assert.Contains("пол", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
                field.Text = "3000"; Layout(shell);
                Click(shell, shell.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.SaveGraphCommand)));
                await vm.SaveGraphCommand.ExecutionTask!;
                Assert.NotEqual(original, File.ReadAllText(path));
            }
            finally { shell.Close(); view.Dispose(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PaletteNumericInputsReportConversionErrorsWithoutChangingStoredValues()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new CleanWorkstation();
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            using var view = new GraphEditorView { DataContext = vm };
            var window = new Window { Content = view, Width = 1600, Height = 1100 };
            var checkedFields = 0;
            try
            {
                window.Show();
                foreach (var sample in vm.AvailableNodes)
                {
                    vm.RootGraph.Clear(); vm.AddNodeAtLocation(sample.Title, new Point(100, 100)); Layout(window);
                    var node = Assert.Single(vm.Nodes);
                    var before = GraphSerializer.Serialize(vm, "Поля ноды");
                    var fields = window.GetVisualDescendants().OfType<TextBox>()
                        .Where(t => ReferenceEquals(t.DataContext, node) && t.IsEffectivelyVisible && t.IsEffectivelyEnabled &&
                            double.TryParse(t.Text, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.CurrentCulture, out _)).ToArray();
                    foreach (var field in fields)
                    {
                        var text = field.Text; field.Text = "не число"; Layout(window);
                        if (DataValidationErrors.GetHasErrors(field))
                        {
                            checkedFields++;
                            var localContext = field.IsSet(StyledElement.DataContextProperty);
                            Assert.False(vm.CheckEditorInputs());
                            Assert.Equal(localContext, field.IsSet(StyledElement.DataContextProperty));
                            Assert.Same(node, field.DataContext);
                            Assert.Contains(node.DisplayTitle, vm.StatusMessage);
                            Assert.Equal(before, GraphSerializer.Serialize(vm, "Поля ноды"));
                        }
                        field.Focus(); field.SelectAll();
                        foreach (var character in text ?? "") window.KeyTextInput(character.ToString());
                        Layout(window);
                        Assert.True(vm.CheckEditorInputs(), $"{node.GetType().Name}, {field.Name}, исходный текст {text}, сейчас {field.Text}: {vm.StatusMessage}; " +
                            string.Join(" | ", window.GetVisualDescendants().OfType<TextBox>().Where(DataValidationErrors.GetHasErrors)
                                .Select(t => t.Text + ": " + string.Join(",", DataValidationErrors.GetErrors(t)?.Cast<object>() ?? Array.Empty<object>()))));
                    }
                    Capture(window, "node-" + node.GetType().Name);
                    Assert.Equal(before, GraphSerializer.Serialize(vm, "Поля ноды"));
                }
                Assert.True(checkedFields >= 15, $"Проверено числовых полей: {checkedFields}");
                File.WriteAllText(Path.Combine(Evidence, "numeric-fields-count.txt"), checkedFields.ToString());
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EmptyWorkstationCanCreateANewModelProfileAndTransferItWithoutItsLocalLibrary(bool board)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var clean = new CleanWorkstation();
            var theme = Application.Current!.RequestedThemeVariant;
            Application.Current.RequestedThemeVariant = board ? ThemeVariant.Dark : ThemeVariant.Light;
            var shell = new MainWindow { Width = 1500, Height = 950 };
            var main = (MainWindowViewModel)shell.DataContext!; var vm = main.TestVM;
            var prefix = board ? "board" : "switch";
            try
            {
                shell.Show(); Layout(shell);
                Assert.Empty(vm.AllProfiles); Assert.False(main.ModbusService.IsConnected);
                Assert.False(main.IsEngineerWorkspace);
                Capture(shell, prefix + "-01-empty-workstation");
                Click(shell, "Настройки компьютера");
                shell.GetVisualDescendants().OfType<RadioButton>().Single(c => Equals(c.Content, "Инженерный режим")).IsChecked = true;
                Layout(shell); Assert.True(main.IsEngineerWorkspace);
                Capture(shell, prefix + "-02-settings"); Click(shell, "Вернуться к работе");
                Click(shell, "Новый"); await vm.NewProfileCommand.ExecutionTask!; Layout(shell); Assert.Empty(vm.Nodes);
                var addModel = shell.GetVisualDescendants().OfType<Button>().Single(c => AutomationProperties.GetName(c) == "Добавить изделие в справочник");
                Assert.Equal("Новое изделие…", addModel.Content);
                Assert.True(addModel.IsEffectivelyVisible);
                Click(shell, addModel);
                var card = Assert.Single(shell.OwnedWindows.OfType<CatalogSerialsWindow>());
                Capture(card, prefix + "-03-new-model-empty");
                Field(card, "Название изделия").Text = board ? "QA новая плата X1" : "QA новый коммутатор X24";
                Field(card, "Альтернативные названия").Text = "QA-ALIAS-" + prefix;
                Field(card, "Код модели на этикетке").Text = "не число";
                Click(card, "Сохранить в справочник"); Assert.True(card.IsVisible);
                Assert.False(File.Exists(EquipmentCatalog.LocalPath(clean.Profiles)));
                Field(card, "Код модели на этикетке").Text = "888";
                Field(card, "Название комплектации 0").Text = "QA комплект";
                Tab(card, 1); Field(card, "Тип на сервере").Text = board ? "QA-BOARD" : "QA-SWITCH";
                if (board)
                {
                    Tab(card, 2); Check(card, "Использовать отчёт RTL").IsChecked = true;
                    Field(card, "Числовой тип отчёта на сервере").Text = "77";
                    Field(card, "Переменная идентификатора платы").Text = "CpuId";
                    Field(card, "Служебный идентификатор отказов").Text = "QA-FAILURE-ID";
                    Check(card, "Получать серийный номер из ответа на отчёт").IsChecked = true;
                }
                else
                {
                    Field(card, "Стартовый серийный номер").Text = "8800001";
                    Field(card, "Последний серийный номер").Text = "8865535";
                    Field(card, "База короткого номера").Text = "8800000";
                    Field(card, "Номер для аварийного отчёта").Text = "8800001";
                    Field(card, "Префикс MAC").Text = "C0:11:A6:58";
                    Field(card, "Имя платы при записи Pro MAC").Text = "QA-BOARD-V1";
                }
                Tab(card, 3); Field(card, "Шкаф комплектации 0").Text = "QA корпус";
                Capture(card, prefix + "-04-model-filled");
                Click(card, "Сохранить в справочник"); Assert.False(card.IsVisible);
                var model = Assert.IsType<EquipmentModel>(vm.SelectedEquipmentModel);
                Assert.Equal(board ? "Board" : "Switch", vm.RootGraph.ProductKind);
                Assert.Equal("QA корпус", model.LabelCabinets![0]);
                Assert.Equal(888, model.LabelCode);

                var library = new EquipmentLibrary(Path.Combine(clean.Root, "equipment-library.json"));
                var catalogWindow = new Window { Content = new EquipmentLibraryView(library), Width = 1100, Height = 760 };
                catalogWindow.Show(shell); Layout(catalogWindow); Click(catalogWindow, "Новое оборудование…");
                var editor = Assert.Single(catalogWindow.GetVisualDescendants().OfType<EquipmentTemplateEditor>());
                editor.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "EquipmentName").Text = "QA новый прибор";
                editor.GetVisualDescendants().OfType<Expander>().Single().IsExpanded = true; Layout(catalogWindow);
                editor.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "EquipmentIdentificationValuesText").Text = "200";
                var grid = editor.GetVisualDescendants().OfType<DataGrid>().Single();
                var register = Assert.Single(grid.ItemsSource.Cast<RegisterDefinition>());
                EditCell(catalogWindow, grid, 0, 0, "100"); EditCell(catalogWindow, grid, 0, 1, "Напряжение");
                EditCell(catalogWindow, grid, 0, 2, "мВ"); EditCell(catalogWindow, grid, 0, 3, "Измерения");
                Assert.Equal(100, register.Address); Assert.Equal("Напряжение", register.Name);
                Capture(catalogWindow, prefix + "-05-new-hardware"); Click(catalogWindow, "Сохранить в каталог"); Assert.Null(editor.GetVisualRoot());
                catalogWindow.Close();
                var hardware = new HardwareEditorWindow(vm, library); hardware.Show(shell); Layout(hardware);
                Click(hardware, "Добавить устройства…");
                var picker = Assert.Single(hardware.GetVisualDescendants().OfType<AddProfileEquipmentView>()); Layout(hardware);
                var equipmentList = picker.GetVisualDescendants().OfType<ListBox>().Single(c => c.Name == "EquipmentCatalogList");
                equipmentList.SelectedItem = equipmentList.Items.OfType<EquipmentTemplate>().Single(e => e.Device.Name == "QA новый прибор"); Layout(hardware);
                Click(hardware, "Добавить в профиль"); Layout(hardware);
                Capture(hardware, prefix + "-06-hardware-profile");
                Click(hardware, "Применить к профилю"); Assert.False(hardware.IsVisible);
                Assert.Equal("QA новый прибор", Assert.Single(vm.RootGraph.Hardware!.Devices).Name);
                Assert.Empty(main.SlaveManager.Slaves);

                vm.AddNodeAtLocation("Старт", new Point(80, 80));
                vm.AddNodeAtLocation("Задержка", new Point(900, 80));
                vm.AddNodeAtLocation("Конец", new Point(1700, 80));
                var first = vm.Nodes[0]; var pause = vm.Nodes[1]; var last = vm.Nodes[2];
                vm.Connections.Add(new(first.Output[0], pause.Input[0]));
                vm.Connections.Add(new(pause.Output[0], last.Input[0]));
                var name = shell.GetVisualDescendants().OfType<AutoCompleteBox>().Single(c => c.Name == "ProfileNameInput");
                name.Text = "Полный путь с нуля"; name.Focus(); shell.Focus(); Layout(shell);
                Click(shell, shell.GetVisualDescendants().OfType<Button>().Single(c => ReferenceEquals(c.Command, vm.SaveGraphCommand)));
                await vm.SaveGraphCommand.ExecutionTask!; Layout(shell);
                var saved = Assert.Single(vm.AllProfiles);
                Assert.True(File.Exists(saved.FilePath));
                var zoomOut = shell.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Уменьшить масштаб");
                var canvas = shell.GetVisualDescendants().OfType<GraphEditorView>().Single().FindControl<NodifyEditor>("Editor")!;
                for (var zoom = 0; zoom < 30 && canvas.ViewportZoom > .4; zoom++) Click(shell, zoomOut);
                Click(shell, shell.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Центрировать граф"));
                Capture(shell, prefix + "-07-saved-profile");
                var json = File.ReadAllText(saved.FilePath);
                File.WriteAllText(Path.Combine(Evidence, prefix + "-profile.json"), json);
                await vm.SelectProfileAsync(saved, reload: true, discardChangesConfirmed: true);
                Assert.Equal(model.Id, vm.SelectedEquipmentModel!.Id);
                Assert.Equal(100, Assert.Single(vm.RootGraph.Hardware!.Devices.Single().Registers).Address);
                Assert.Equal("мВ", vm.RootGraph.Hardware.Devices.Single().Registers[0].Unit);

                var destination = Path.Combine(clean.Root, "another-workstation"); Directory.CreateDirectory(destination);
                var portable = ProfilePortableExport.Create(json, vm.CurrentEquipmentCatalog);
                await using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(portable));
                Assert.True(await ProfileFileStorage.ImportAsync(input, destination, Path.GetFileName(saved.FilePath)));
                Assert.True(File.Exists(EquipmentCatalog.LocalPath(destination)));
                var imported = Directory.GetFiles(destination, "*.json").Single();
                AppSettings.Instance.GraphsFolder = destination;
                using var otherBus = new ModbusService();
                using var reader = new TestViewModel(otherBus, new SlaveManager(otherBus)) { IsOperatorView = false };
                await reader.SelectProfileAsync(GraphSerializer.ReadProfile(imported), discardChangesConfirmed: true);
                Assert.Equal(model.Id, reader.SelectedEquipmentModel!.Id);
                Assert.Equal(board ? "Board" : "Switch", reader.RootGraph.ProductKind);
                Assert.Equal("QA новый прибор", reader.RootGraph.Hardware!.Devices.Single().Name);
                Assert.Contains("Напряжение, мВ", RegisterReference.Describe(100, hardware: reader.RootGraph.Hardware, slaveId: 1));
                Assert.False(otherBus.IsConnected);
                Capture(shell, prefix + "-08-source-still-open");
            }
            finally { shell.Close(); Application.Current.RequestedThemeVariant = theme; }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ChangingTheProfilesFolderInSettingsRefreshesTheListWithoutAnotherPageVisit()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new CleanWorkstation();
            var other = Path.Combine(clean.Root, "profiles-B"); Directory.CreateDirectory(other);
            var path = Path.Combine(other, "new.json");
            File.WriteAllText(path, """{"name":"Профиль папки B","nodes":[],"connections":[]}""");
            var shell = new MainWindow(); var main = (MainWindowViewModel)shell.DataContext!;
            try
            {
                shell.Show(); Layout(shell); Assert.Empty(main.TestVM.AllProfiles);
                var edited = new DelayNodeViewModel { Milliseconds = 42 };
                main.TestVM.Nodes.Add(edited);
                var snapshot = GraphSerializer.Serialize(main.TestVM, "Не сохранён");
                Click(shell, "Настройки компьютера");
                main.SettingsVM.GraphsFolder = other; // Same setter as the system folder picker result.
                Click(shell, "Вернуться к работе"); Capture(shell, "03-folder-changed");
                Assert.Equal(path, Assert.Single(main.TestVM.AllProfiles).FilePath);
                Assert.Equal(path, Assert.Single(main.TestVM.StationProfiles).FilePath);
                Assert.Same(edited, Assert.Single(main.TestVM.Nodes));
                Assert.Equal(snapshot, GraphSerializer.Serialize(main.TestVM, "Не сохранён"));
            }
            finally { shell.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RegisterAreaEditsFollowTheSelectedRowAfterScrolling(bool reusable)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new CleanWorkstation();
            var library = new EquipmentLibrary(Path.Combine(clean.Root, "library.json"));
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            var map = new DeviceDefinition { Name = "Прибор с большим числом регистров", Registers = Enumerable.Range(0, 100)
                .Select(i => new RegisterDefinition { Address = i, Name = "Регистр " + i }).ToList() };
            vm.RootGraph.Hardware = new() { Devices = new() { map } };
            Window window = reusable ? new EquipmentTemplateWindow(library, map) : new HardwareEditorWindow(vm, library);
            try
            {
                window.Show(); Layout(window);
                var grid = window.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Columns.Count > 2 && g.ItemsSource != null);
                var rows = grid.ItemsSource.Cast<RegisterDefinition>().ToArray();
                Area(0, ModbusArea.Input); Area(99, ModbusArea.Coil); Area(1, ModbusArea.Discrete);
                Assert.Equal(ModbusArea.Input, rows[0].Area); Assert.Equal(ModbusArea.Coil, rows[99].Area); Assert.Equal(ModbusArea.Discrete, rows[1].Area);
                Assert.All(rows.Skip(2).Take(97), r => Assert.Equal(ModbusArea.Holding, r.Area));
                Capture(window, "04-register-area-" + reusable);
                Click(window, reusable ? "Сохранить в каталог" : "Применить к профилю"); Assert.False(window.IsVisible);
                var saved = reusable ? library.Load().Single(t => !t.IsBuiltIn).Device : vm.RootGraph.Hardware!.Devices.Single();
                Assert.Equal(ModbusArea.Coil, saved.Registers[99].Area);
                void Area(int index, ModbusArea value)
                {
                    var column = reusable ? 4 : 2;
                    grid.SelectedIndex = index; grid.CurrentColumn = grid.Columns[column]; grid.ScrollIntoView(rows[index], grid.Columns[column]); Layout(window);
                    Assert.True(grid.BeginEdit()); Layout(window);
                    var input = grid.GetVisualDescendants().OfType<ComboBox>().Single(c => c.IsEffectivelyVisible && c.Items.Contains(ModbusArea.Holding));
                    input.SelectedItem = value; Assert.True(grid.CommitEdit(DataGridEditingUnit.Cell, true)); Assert.True(grid.CommitEdit(DataGridEditingUnit.Row, true)); Layout(window);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SavingPortableLabelsDoesNotLeakIntoAnotherProfilesFolderOrLegacySettings()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new CleanWorkstation();
            var settings = AppSettings.Instance;
            var legacy = JsonSerializer.Serialize(settings.LabelTemplates);
            var window = new LabelTemplatesWindow(settingsOnly: true);
            try
            {
                window.Show(); Layout(window); Capture(window, "01-label-catalog-before");
                var list = window.GetVisualDescendants().OfType<ListBox>().Single();
                var first = Assert.IsType<LabelTemplateDefinition>(list.SelectedItem);
                var name = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == first.Name);
                name.Text = "Только папка A"; Layout(window);
                Click(window, "Сохранить каталог");
                Assert.False(window.IsVisible);
                Assert.Contains(LabelTemplateStore.Read(LabelTemplateStore.LocalPath(clean.Profiles)).Templates, t => t.Name == "Только папка A");
                settings.GraphsFolder = Path.Combine(clean.Root, "profiles-B");
                var other = LabelTemplateStore.Load(settings.ResolvedGraphsFolder, settings.LabelTemplates);
                Assert.DoesNotContain(other, t => t.Name == "Только папка A");
                Assert.Equal(legacy, JsonSerializer.Serialize(settings.LabelTemplates));
                settings.SaveChecked();
                Assert.Equal(legacy, JsonSerializer.Serialize(AppSettings.Load(clean.SettingsFile).LabelTemplates));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AdaptingVisualLabelParameterizesTheOldModelInTextAndRestorableCommand()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new CleanWorkstation();
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            var source = vm.EquipmentModels.Single(m => m.Id == "psw-ups-box-8x2pro");
            var target = vm.EquipmentModels.Single(m => m.Id == "psw-2g8f-plus-box");
            vm.SelectedEquipmentModel = source;
            var label = LabelPrintContent.ForModel(LabelTemplateCatalog.Defaults()[0], source, 0);
            label.Command = label.Command.Replace("{{Name}}", source.Name);
            label.Layout = LabelLayoutDefinition.FromTemplate(label);
            var title = label.Layout.Elements.First(e => e.Kind == "Text");
            title.Value = "Модель: " + source.Name;
            var print = new PrintLabelNodeViewModel { LabelTemplate = label.Copy() };
            vm.Nodes.Add(print);
            var before = JsonSerializer.Serialize(print.LabelTemplate);
            vm.SelectedEquipmentModel = target;
            var result = print.LabelTemplate!;
            var visual = new Window { Content = new LabelTemplateEditorView(new LabelLayoutEditorViewModel(result)), Width = 1280, Height = 880 };
            try { visual.Show(); Layout(visual); Capture(visual, "05-label-after-model-change"); } finally { visual.Close(); }
            Assert.Contains("{{Name}}", result.Layout!.Elements.First(e => e.Kind == "Text").Value);
            Assert.Equal(target.Name, result.Layout.Elements.First(e => e.Kind == "Text").Name);
            Assert.DoesNotContain(source.Name, result.Layout.OriginalCommand!);
            var preview = LabelPreview.FromInput(result, "2600042", "C0:11:A6:1A:00:2A");
            Assert.Contains(preview.Elements, e => e.Text == "Модель: " + target.Name);
            Assert.DoesNotContain(preview.Elements, e => e.Text.Contains(source.Name));
            vm.UndoCommand.Execute(null);
            Assert.Equal(before, JsonSerializer.Serialize(print.LabelTemplate));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EquipmentEditorsCanDeleteAnInvalidNumericRowAndThenSave(bool reusable)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new CleanWorkstation();
            var library = new EquipmentLibrary(Path.Combine(clean.Root, "equipment-library.json"));
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            var source = new DeviceDefinition { Name = "Новый прибор", SlaveId = 7,
                Registers = new() { new() { Address = 100, Name = "Первый" }, new() { Address = 101, Name = "Второй" } } };
            vm.RootGraph.Hardware = new HardwareConfiguration { Devices = new() { source } };
            var original = JsonSerializer.Serialize(vm.RootGraph.Hardware);
            Window window = reusable ? new EquipmentTemplateWindow(library, source) : new HardwareEditorWindow(vm, library);
            try
            {
                window.Show(); Layout(window);
                var grid = window.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Columns.Count > 2 && g.ItemsSource != null);
                grid.SelectedIndex = 0; grid.CurrentColumn = grid.Columns[0]; grid.Focus();
                Assert.True(grid.BeginEdit()); Layout(window);
                grid.GetVisualDescendants().OfType<TextBox>().Single(t => t.IsEffectivelyVisible).Text = "не число";
                Layout(window); Capture(window, $"02-invalid-equipment-{reusable}");
                Click(window, "Удалить регистр");
                Assert.Single(grid.ItemsSource.Cast<RegisterDefinition>());
                Click(window, reusable ? "Сохранить в каталог" : "Применить к профилю");
                Assert.False(window.IsVisible);
                if (reusable)
                {
                    Assert.Equal(101, Assert.Single(library.Load().Single(t => !t.IsBuiltIn).Device.Registers).Address);
                    Assert.Equal(original, JsonSerializer.Serialize(vm.RootGraph.Hardware));
                }
                else Assert.Equal(101, Assert.Single(vm.RootGraph.Hardware!.Devices.Single().Registers).Address);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    internal sealed class CleanWorkstation : IDisposable
    {
        private readonly AppSettings settings = AppSettings.Instance;
        private readonly Dictionary<PropertyInfo, object?> previous;
        private readonly FieldInfo settingsPath = typeof(AppSettings).GetField("_settingsPath", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private readonly object? oldSettingsPath;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "tfortis-empty-workstation-" + Guid.NewGuid().ToString("N"));
        public string Profiles => Path.Combine(Root, "profiles-A");
        public string SettingsFile => Path.Combine(Root, "testbuilder.settings");
        public CleanWorkstation()
        {
            Directory.CreateDirectory(Profiles);
            previous = typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetMethod?.IsPublic == true && p.SetMethod?.IsPublic == true).ToDictionary(p => p, p => p.GetValue(settings));
            oldSettingsPath = settingsPath.GetValue(settings);
            var fresh = AppSettings.Load(SettingsFile);
            foreach (var property in previous.Keys) property.SetValue(settings, property.GetValue(fresh));
            settingsPath.SetValue(settings, SettingsFile);
            settings.GraphsFolder = Profiles;
        }
        public void Dispose()
        {
            settingsPath.SetValue(settings, oldSettingsPath);
            foreach (var (property, value) in previous) property.SetValue(settings, value);
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }

    private static void Layout(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); Dispatcher.UIThread.RunJobs(); }
    private static void Click(Window window, string caption)
    {
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && Equals(b.Content, caption));
        Click(window, button);
    }
    private static void Click(Window window, Button button)
    {
        Assert.True(button.IsEffectivelyEnabled);
        button.BringIntoView(); Layout(window);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        Layout(window);
    }
    private static void Capture(Window window, string name)
    {
        Directory.CreateDirectory(Evidence);
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); frame.Save(Path.Combine(Evidence, name + ".png"));
        var controls = window.GetVisualDescendants().OfType<Control>().Where(c => c is Button or TextBox or ComboBox or CheckBox or NumericUpDown or DataGrid)
            .Select(c => new { Type = c.GetType().Name, c.Name, AccessibleName = AutomationProperties.GetName(c), c.IsEffectivelyVisible, c.IsEffectivelyEnabled,
                Text = c switch { ContentControl b => b.Content?.ToString(), TextBox t => t.Text, _ => null } }).ToArray();
        File.WriteAllText(Path.Combine(Evidence, name + ".controls.json"), JsonSerializer.Serialize(controls, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static TextBox Field(Window window, string label) => window.GetVisualDescendants().OfType<TextBox>().Single(c => AutomationProperties.GetName(c) == label);
    private static CheckBox Check(Window window, string label) => window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, label));
    private static void Tab(Window window, int index) { window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = index; Layout(window); }
    private static void EditCell(Window window, DataGrid grid, int row, int column, string value)
    {
        grid.SelectedIndex = row; grid.CurrentColumn = grid.Columns[column]; grid.Focus(); Assert.True(grid.BeginEdit()); Layout(window);
        grid.GetVisualDescendants().OfType<TextBox>().Single(t => t.IsEffectivelyVisible).Text = value;
        Assert.True(grid.CommitEdit(DataGridEditingUnit.Cell, true)); Assert.True(grid.CommitEdit(DataGridEditingUnit.Row, true)); Layout(window);
    }
}
