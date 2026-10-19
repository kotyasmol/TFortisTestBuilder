using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using TestBuilder.Presentation;
using TestBuilder.Converters;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using TestBuilder.Domain.Modbus;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public sealed class HardwareEditorWindow : Window
{
    private readonly TestViewModel _vm;
    private HardwareConfiguration _draft;
    private readonly ObservableCollection<DeviceDefinition> _devices;
    private readonly DataGrid _devicesGrid = new() { AutoGenerateColumns = false, MinHeight = 140 };
    private readonly DataGrid _registersGrid = new() { AutoGenerateColumns = false, MinHeight = 220 };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBox _baud = new(), _bits = new(), _poll = new();
    private readonly ComboBox _parity = new() { ItemsSource = Enum.GetValues<Parity>() };
    private readonly ComboBox _stop = new() { ItemsSource = new[] { StopBits.One, StopBits.Two, StopBits.OnePointFive } };
    private readonly List<DeviceDefinition> _presets;
    private readonly ComboBox _preset = new() { MinWidth = 160 };
    private readonly CheckBox _legacy = new() { Content = "Определять оборудование АПК-03 автоматически" };
    private readonly TextBlock _registerTitle = new() { FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold };
    private readonly TextBox _registerSearch = new() { Watermark = "Найти регистр" };
    private readonly StackPanel _identification = new() { Spacing = 8 };

    public HardwareEditorWindow(TestViewModel vm)
    {
        _vm = vm; Title = "Оборудование текущего профиля"; Width = 1180; Height = 780; MinWidth = 1000; MinHeight = 650;
        _draft = vm.RootGraph.Hardware == null ? new HardwareConfiguration() : TestViewModel.CopyDefinition(vm.RootGraph.Hardware);
        _legacy.IsChecked = vm.RootGraph.Hardware == null;
        _devices = new ObservableCollection<DeviceDefinition>(_draft.Devices);
        using var resource = AssetLoader.Open(new Uri("avares://TestBuilder/Assets/hardware-presets.json"));
        _presets = JsonSerializer.Deserialize<List<DeviceDefinition>>(resource) ?? new();
        _parity.ItemTemplate = new FuncDataTemplate<Parity>((value, _) => new TextBlock { Text = RussianUiText.Display(value) });
        _stop.ItemTemplate = new FuncDataTemplate<StopBits>((value, _) => new TextBlock { Text = RussianUiText.Display(value) });
        _preset.ItemsSource = _presets; _preset.SelectedIndex = 0;
        _devicesGrid.ItemsSource = _devices;
        AddColumn(_devicesGrid, "Адрес", "SlaveId"); AddColumn(_devicesGrid, "Название", "Name");

        AddColumn(_registersGrid, "Адрес", "Address"); AddColumn(_registersGrid, "Название", "Name");
        AddAreaColumn(); AddColumn(_registersGrid, "Единица", "Unit"); AddColumn(_registersGrid, "Категория", "Category");
        _registersGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Запись", Binding = new Binding("Writable") { Mode = BindingMode.TwoWay } });
        _devicesGrid.SelectionChanged += (_, _) => ShowRegisters();
        var root = new DockPanel { Margin = new Thickness(20) };
        var bottom = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        bottom.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(Button("Применить к профилю", () => {
            try { CommitGrid(); _vm.ApplyHardware(_legacy.IsChecked == true ? null : GetDraft()); Close(); }
            catch (Exception ex) { _status.Text = ex.Message; }
        }));
        buttons.Children.Add(Button("Отмена", Close));
        bottom.Children.Add(buttons);
        bottom.Children.Add(new TextBlock { Text = "После применения сохраните профиль в редакторе.", Classes = { "hint" } });
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);

        var connection = new StackPanel { Spacing = 18, Margin = new Thickness(8, 20), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        connection.Children.Add(_legacy);
        connection.Children.Add(new TextBlock { Text = "Для готового стенда АПК-03 можно оставить автоматическое определение. Для плат и другого оборудования задайте устройства на соседней вкладке.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var serial = new WrapPanel();
        Field(serial, "Скорость, бод", _baud); Field(serial, "Биты данных", _bits); Field(serial, "Чётность", _parity); Field(serial, "Стоп-биты", _stop); Field(serial, "Опрос, мс", _poll);
        connection.Children.Add(serial);
        connection.Children.Add(new TextBlock { Text = "COM-порт выбирается в настройках приложения. Параметры линии и список устройств сохраняются в профиле.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var transfer = new WrapPanel();
        transfer.Children.Add(Button("Импорт оборудования…", async () => await ImportAsync()));
        var export = Button("Экспорт оборудования…", async () => await ExportAsync()); transfer.Children.Add(export);
        connection.Children.Add(transfer);

        var devicesPage = new Grid { ColumnDefinitions = new ColumnDefinitions("290,16,*"), Margin = new Thickness(0, 16, 0, 0) };
        var devices = new DockPanel();
        var deviceActions = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        deviceActions.Children.Add(new TextBlock { Text = "Устройства", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        _preset.HorizontalAlignment = HorizontalAlignment.Stretch;
        deviceActions.Children.Add(_preset);
        var add = new WrapPanel();
        add.Children.Add(Button("Добавить из шаблона", () => { if (_preset.SelectedItem is DeviceDefinition p) AddDevice(TestViewModel.CopyDefinition(p)); }));
        add.Children.Add(Button("Пустое устройство", () => AddDevice(new DeviceDefinition { Registers = new() { new RegisterDefinition() } })));
        deviceActions.Children.Add(add);
        DockPanel.SetDock(deviceActions, Dock.Top); devices.Children.Add(deviceActions);
        var deviceBottom = new StackPanel { Spacing = 8 };
        deviceBottom.Children.Add(Button("Удалить устройство", () => { if (_devicesGrid.SelectedItem is DeviceDefinition d) { _devices.Remove(d); _devicesGrid.SelectedIndex = _devices.Count > 0 ? 0 : -1; ShowRegisters(); } }));
        foreach (var (label, property) in new[] { ("Регистр типа", "IdentificationAddress"), ("Ожидаемое значение", "IdentificationValue") })
        {
            var input = new TextBox(); input.Bind(TextBox.TextProperty, new Binding(property) { Mode = BindingMode.TwoWay });
            _identification.Children.Add(new TextBlock { Text = label }); _identification.Children.Add(input);
        }
        deviceBottom.Children.Add(new Expander { Header = "Проверка типа устройства", Content = _identification, HorizontalAlignment = HorizontalAlignment.Stretch });
        DockPanel.SetDock(deviceBottom, Dock.Bottom); devices.Children.Add(deviceBottom);
        devices.Children.Add(_devicesGrid); devicesPage.Children.Add(devices);

        var registers = new DockPanel();
        var regTop = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        regTop.Children.Add(_registerTitle); regTop.Children.Add(_registerSearch);
        _registerSearch.TextChanged += (_, _) => FilterRegisters();
        var regActions = new WrapPanel();
        regActions.Children.Add(Button("Добавить регистр", () => { if (_devicesGrid.SelectedItem is DeviceDefinition d) { d.Registers.Add(new RegisterDefinition { Address = d.Registers.Count == 0 ? 0 : Math.Min(65535, d.Registers.Max(r => r.Address) + 1) }); _registerSearch.Text = ""; ShowRegisters(); } }));
        regActions.Children.Add(Button("Удалить регистр", () => { if (_devicesGrid.SelectedItem is DeviceDefinition d && _registersGrid.SelectedItem is RegisterDefinition reg) { d.Registers.Remove(reg); ShowRegisters(); } }));
        regTop.Children.Add(regActions);
        regTop.Children.Add(new TextBlock { Text = "Двойной щелчок — изменить ячейку. Адрес регистра указывается с нуля.", Classes = { "hint" } });
        DockPanel.SetDock(regTop, Dock.Top); registers.Children.Add(regTop); registers.Children.Add(_registersGrid);
        Grid.SetColumn(registers, 2); devicesPage.Children.Add(registers);
        var deviceTab = new TabItem { Header = "Устройства и регистры", Content = devicesPage };
        var tabs = new TabControl { Items = { new TabItem { Header = "Подключение", Content = connection }, deviceTab } };
        void ModeChanged() { var manual = _legacy.IsChecked != true; serial.IsEnabled = manual; deviceTab.IsEnabled = manual; export.IsEnabled = manual; }
        _legacy.IsCheckedChanged += (_, _) => ModeChanged(); ModeChanged();
        root.Children.Add(tabs); Content = root;
        LoadConnection(); _devicesGrid.SelectedIndex = _devices.Count == 0 ? -1 : 0; ShowRegisters();
        tabs.SelectedIndex = _legacy.IsChecked == true ? 0 : 1;
    }
    private void CommitGrid()
    {
        if (!_devicesGrid.CommitEdit() || !_registersGrid.CommitEdit() || _identification.Children.OfType<TextBox>().Any(DataValidationErrors.GetHasErrors))
            throw new InvalidOperationException("Исправьте некорректное значение в оборудовании.");
    }
    private void LoadConnection() { _baud.Text = _draft.BaudRate.ToString(); _bits.Text = _draft.DataBits.ToString(); _poll.Text = _draft.PollIntervalMs.ToString(); _parity.SelectedItem = _draft.Parity; _stop.SelectedItem = _draft.StopBits; }
    private HardwareConfiguration GetDraft()
    {
        CommitGrid();
        _draft.BaudRate = int.Parse(_baud.Text ?? ""); _draft.DataBits = int.Parse(_bits.Text ?? ""); _draft.PollIntervalMs = int.Parse(_poll.Text ?? "");
        _draft.Parity = (Parity)_parity.SelectedItem!; _draft.StopBits = (StopBits)_stop.SelectedItem!;
        _draft.Devices = _devices.ToList(); _draft.Validate(); return _draft;
    }
    private void ShowRegisters()
    {
        var device = _devicesGrid.SelectedItem as DeviceDefinition;
        _identification.DataContext = device;
        _registerTitle.Text = device == null ? "Выберите устройство" : $"Регистры: {RussianUiText.Display(device.Name)}";
        FilterRegisters();
    }
    private void FilterRegisters()
    {
        var query = _registerSearch.Text?.Trim() ?? "";
        _registersGrid.ItemsSource = (_devicesGrid.SelectedItem as DeviceDefinition)?.Registers
            .Where(r => query.Length == 0 || r.Address.ToString().Contains(query) || RussianUiText.Display(r.Name).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    private void AddDevice(DeviceDefinition d)
    {
        var used = _devices.Select(x => (int)x.SlaveId).ToHashSet();
        var id = Enumerable.Range(1, 247).FirstOrDefault(n => !used.Contains(n));
        if (id == 0) { _status.Text = "Все адреса устройств заняты."; return; }
        d.SlaveId = (byte)id; _devices.Add(d); _devicesGrid.SelectedItem = d; _legacy.IsChecked = false;
    }
    private async Task ImportAsync()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Описание оборудования JSON", AllowMultiple = false });
            if (files.Count == 0) return;
            using var stream = await files[0].OpenReadAsync();
            if (stream.Length > 4_000_000) throw new InvalidOperationException("Файл слишком велик.");
            var config = await JsonSerializer.DeserializeAsync<HardwareConfiguration>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("Пустой файл.");
            config.Validate(); _draft = config; _devices.Clear(); foreach (var d in config.Devices) _devices.Add(d);
            LoadConnection(); _devicesGrid.SelectedIndex = 0; _legacy.IsChecked = false; _status.Text = "Таблица загружена. Примените её и сохраните профиль.";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private async Task ExportAsync()
    {
        try
        {
            var config = GetDraft();
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Экспорт оборудования", SuggestedFileName = "equipment.json", DefaultExtension = "json" });
            if (file == null) return;
            await using var stream = await file.OpenWriteAsync(); stream.SetLength(0);
            await JsonSerializer.SerializeAsync(stream, config, new JsonSerializerOptions { WriteIndented = true }); _status.Text = "Описание экспортировано.";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private void AddAreaColumn()
    {
        _registersGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Тип регистра", Width = new DataGridLength(155),
            CellTemplate = new FuncDataTemplate<RegisterDefinition>((register, _) =>
                new TextBlock { Text = RussianUiText.Display(register?.Area) }),
            CellEditingTemplate = new FuncDataTemplate<RegisterDefinition>((register, _) =>
            {
                var selector = new ComboBox
                {
                    ItemsSource = RussianUiOptions.RegisterAreas,
                    ItemTemplate = new FuncDataTemplate<ModbusArea>((area, _) => new TextBlock { Text = RussianUiText.Display(area) }),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                selector.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(RegisterDefinition.Area)) { Source = register, Mode = BindingMode.TwoWay });
                return selector;
            })
        });
    }
    private static void AddColumn(DataGrid grid, string title, string path) => grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path) { Mode = BindingMode.TwoWay, Converter = path == "Name" ? new RussianUiTextConverter() : null }, Width = path == "Name" ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(path == "Category" ? 110 : path == "Unit" ? 65 : 75), MinWidth = path == "Name" ? 180 : 60 });
    private static void Field(Panel panel, string name, Control input) { input.MinWidth = 85; var stack = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; stack.Children.Add(new TextBlock { Text = name }); stack.Children.Add(input); panel.Children.Add(stack); }
    private static Button Button(string title, Action action) { var b = new Button { Content = title, Margin = new Thickness(0, 4, 8, 4) }; b.Click += (_, _) => action(); return b; }
}
