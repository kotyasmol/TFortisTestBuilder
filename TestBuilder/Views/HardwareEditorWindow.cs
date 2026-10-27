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
using TestBuilder.Services;

namespace TestBuilder.Views;

public sealed partial class HardwareEditorWindow : Window
{
    private readonly TestViewModel _vm;
    private HardwareConfiguration _draft;
    private readonly ObservableCollection<DeviceDefinition> _devices;
    private readonly DataGrid _devicesGrid = new() { AutoGenerateColumns = false, MinHeight = 140 };
    private readonly DataGrid _registersGrid = new() { AutoGenerateColumns = false, MinHeight = 220 };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBox _baud = new(), _bits = new(), _poll = new();
    private readonly TextBox _standName = new() { Watermark = "Например, RTL-SW", MinWidth = 300 };
    private readonly AutoCompleteBox _port = new() { MinWidth = 320, Watermark = "Пусто — найти Modbus автоматически" };
    private readonly ObservableCollection<ConsoleDeviceDefinition> _consoles = new();
    private readonly ListBox _consoleList = new();
    private readonly StackPanel _consoleFields = new() { Spacing = 10 };
    private readonly List<Control> _consoleInputs = new();
    private readonly ComboBox _parity = new() { ItemsSource = Enum.GetValues<Parity>() };
    private readonly ComboBox _stop = new() { ItemsSource = new[] { StopBits.One, StopBits.Two, StopBits.OnePointFive } };
    private readonly EquipmentLibrary _library;
    private readonly TabControl _tabs = new();
    private readonly CheckBox _legacy = new() { Content = "Оборудование не задано (подключение недоступно)" };
    private readonly TextBlock _registerTitle = new() { FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold };
    private readonly TextBox _registerSearch = new() { Watermark = "Найти регистр" };
    private readonly StackPanel _identification = new() { Spacing = 8 };

    public HardwareEditorWindow(TestViewModel vm) : this(vm, new EquipmentLibrary()) { }
    internal HardwareEditorWindow(TestViewModel vm, EquipmentLibrary library)
    {
        _library = library;
        _vm = vm; Title = "Оборудование текущего профиля"; Width = 1180; Height = 780; MinWidth = 1000; MinHeight = 650;
        _draft = vm.RootGraph.Hardware == null ? new HardwareConfiguration() : TestViewModel.CopyDefinition(vm.RootGraph.Hardware);
        _legacy.IsChecked = vm.RootGraph.Hardware == null;
        _devices = new ObservableCollection<DeviceDefinition>(_draft.Devices);
        foreach (var console in _draft.ConsoleDevices) _consoles.Add(console);
        _port.ItemsSource = SerialPort.GetPortNames().OrderBy(p => p).ToArray();
        _parity.ItemTemplate = new FuncDataTemplate<Parity>((value, _) => new TextBlock { Text = RussianUiText.Display(value) });
        _stop.ItemTemplate = new FuncDataTemplate<StopBits>((value, _) => new TextBlock { Text = RussianUiText.Display(value) });
        _devicesGrid.ItemsSource = _devices;
        _devicesGrid.Name = "ProfileEquipmentGrid";
        AddColumn(_devicesGrid, "Slave", "SlaveId"); AddColumn(_devicesGrid, "Название", "Name");

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
        var stand = new StackPanel { Spacing = 6 };
        stand.Children.Add(new TextBlock { Text = "Название стенда" });
        stand.Children.Add(_standName);
        connection.Children.Add(stand);
        connection.Children.Add(new TextBlock { Text = "Добавьте готовые устройства из каталога, затем назначьте slave-адреса в «Оборудование профиля». Здесь настраивается общая линия Modbus.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var serial = new WrapPanel();
        connection.Children.Add(new TextBlock { Text = "COM-порт определяется автоматически по ответам устройств профиля." });
        Field(serial, "Скорость, бод", _baud); Field(serial, "Биты данных", _bits); Field(serial, "Чётность", _parity); Field(serial, "Стоп-биты", _stop); Field(serial, "Опрос, мс", _poll);
        connection.Children.Add(serial);
        connection.Children.Add(new TextBlock { Text = "Параметры линии и ожидаемые устройства сохраняются в профиле. Номер COM-порта стенда не нужен. Отдельный USB-UART процессора добавляется на вкладке «Консоли».", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var transfer = new WrapPanel();
        transfer.Children.Add(Button("Импорт оборудования…", async () => await ImportAsync()));
        var export = Button("Экспорт оборудования…", async () => await ExportAsync()); transfer.Children.Add(export);
        connection.Children.Add(transfer);

        var devicesPage = new Grid { ColumnDefinitions = new ColumnDefinitions("290,16,*"), Margin = new Thickness(0, 16, 0, 0) };
        var devices = new DockPanel();
        var deviceActions = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        deviceActions.Children.Add(new TextBlock { Text = "Устройства этого профиля", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        deviceActions.Children.Add(new TextBlock { Text = "Назначьте каждой единице свой slave-адрес. Изменения здесь относятся только к этому профилю.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        deviceActions.Children.Add(Button("Добавить из каталога…", () => _tabs.SelectedIndex = 0));
        DockPanel.SetDock(deviceActions, Dock.Top); devices.Children.Add(deviceActions);
        var deviceBottom = new StackPanel { Spacing = 8 };
        deviceBottom.Children.Add(Button("Убрать из профиля", () => { if (_devicesGrid.SelectedItem is DeviceDefinition d) { _devices.Remove(d); _devicesGrid.SelectedIndex = _devices.Count > 0 ? 0 : -1; ShowRegisters(); } }));
        deviceBottom.Children.Add(Button("Сохранить как новое в каталоге…", async () =>
        {
            try
            {
                CommitGrid();
                if (_devicesGrid.SelectedItem is DeviceDefinition d)
                    await EditTemplateAsync(null, d);
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }));
        foreach (var (label, property) in new[] { ("Регистр типа", "IdentificationAddress"), ("Допустимые значения типа (через запятую)", "IdentificationValuesText"), ("Регистр серийного номера стенда (Holding, необязательно)", "StandSerialNumberAddress") })
        {
            var input = new TextBox(); input.Bind(TextBox.TextProperty, new Binding(property) { Mode = BindingMode.TwoWay });
            _identification.Children.Add(new TextBlock { Text = label }); _identification.Children.Add(input);
        }
        deviceBottom.Children.Add(new Expander { Header = "Тип устройства и номер стенда", Content = _identification, HorizontalAlignment = HorizontalAlignment.Stretch });
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
        var deviceTab = new TabItem { Header = "Оборудование профиля", Content = devicesPage };
        var consoleTab = new TabItem { Header = "Консоли", Content = CreateConsolePage() };
        _tabs.Items.Add(new TabItem { Header = "Каталог оборудования", Content = CreateCatalogPage() });
        _tabs.Items.Add(deviceTab);
        _tabs.Items.Add(new TabItem { Header = "Подключение", Content = connection });
        _tabs.Items.Add(consoleTab);
        void ModeChanged() { var manual = _legacy.IsChecked != true; stand.IsEnabled = manual; serial.IsEnabled = manual; consoleTab.IsEnabled = manual; export.IsEnabled = manual; }
        _legacy.IsCheckedChanged += (_, _) => ModeChanged(); ModeChanged();
        root.Children.Add(_tabs); Content = root;
        LoadConnection(); _devicesGrid.SelectedIndex = _devices.Count == 0 ? -1 : 0; ShowRegisters();
        _tabs.SelectedIndex = _devices.Count == 0 ? 0 : 1;
    }
    private void CommitGrid()
    {
        if (!_devicesGrid.CommitEdit() || !_registersGrid.CommitEdit() || _identification.Children.OfType<TextBox>().Any(DataValidationErrors.GetHasErrors) || _consoleInputs.Any(DataValidationErrors.GetHasErrors))
            throw new InvalidOperationException("Исправьте некорректное значение в оборудовании.");
    }
    private void LoadConnection() { _standName.Text = _draft.StandName; _port.Text = _draft.PortName ?? AppSettings.Instance.StandPort; _baud.Text = _draft.BaudRate.ToString(); _bits.Text = _draft.DataBits.ToString(); _poll.Text = _draft.PollIntervalMs.ToString(); _parity.SelectedItem = _draft.Parity; _stop.SelectedItem = _draft.StopBits; }
    private HardwareConfiguration GetDraft()
    {
        CommitGrid();
        _draft.BaudRate = int.Parse(_baud.Text ?? ""); _draft.DataBits = int.Parse(_bits.Text ?? ""); _draft.PollIntervalMs = int.Parse(_poll.Text ?? "");
        _draft.Parity = (Parity)_parity.SelectedItem!; _draft.StopBits = (StopBits)_stop.SelectedItem!;
        _draft.PortName = "";
        _draft.StandName = string.IsNullOrWhiteSpace(_standName.Text) ? null : _standName.Text.Trim();
        _draft.ConsoleDevices = _consoles.ToList();
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
            _consoles.Clear(); foreach (var d in config.ConsoleDevices) _consoles.Add(d); _consoleList.SelectedIndex = _consoles.Count > 0 ? 0 : -1;
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
    private Control CreateConsolePage()
    {
        var page = new Grid { ColumnDefinitions = new ColumnDefinitions("280,20,*"), Margin = new Thickness(8, 20) };
        var left = new DockPanel();
        var actions = new WrapPanel();
        actions.Children.Add(Button("Добавить консоль", () =>
        {
            var device = new ConsoleDeviceDefinition(); _consoles.Add(device); _consoleList.SelectedItem = device;
        }));
        actions.Children.Add(Button("Удалить", () =>
        {
            if (_consoleList.SelectedItem is ConsoleDeviceDefinition device) _consoles.Remove(device);
            _consoleList.SelectedIndex = _consoles.Count > 0 ? 0 : -1;
        }));
        DockPanel.SetDock(actions, Dock.Bottom); left.Children.Add(actions);
        _consoleList.ItemsSource = _consoles;
        _consoleList.ItemTemplate = new FuncDataTemplate<ConsoleDeviceDefinition>((device, _) =>
        {
            var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            text.Bind(TextBlock.TextProperty, new Binding("Name") { Source = device }); return text;
        });
        _consoleList.SelectionChanged += (_, _) =>
        {
            _consoleFields.DataContext = _consoleList.SelectedItem;
            _consoleFields.IsVisible = _consoleList.SelectedItem != null;
        };
        left.Children.Add(_consoleList); page.Children.Add(left);
        var right = new StackPanel { Spacing = 16 };
        right.Children.Add(new TextBlock { Text = "Порт может появиться после включения платы. В ноде «Консоль платы» включите «Дождаться подключения» и задайте время ожидания.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var advanced = new StackPanel { Spacing = 8 };
        void TextField(string label, string property, bool extra = false)
        {
            var input = new TextBox { Name = property + "Input" }; input.Bind(TextBox.TextProperty, new Binding(property) { Mode = BindingMode.TwoWay });
            _consoleInputs.Add(input); Field(extra ? advanced : _consoleFields, label, input);
        }
        TextField("Название устройства", "Name");
        var port = new AutoCompleteBox { ItemsSource = SerialPort.GetPortNames().OrderBy(p => p).ToArray(), Watermark = "Пусто — поиск по описанию или USB VID/PID" };
        port.Bind(AutoCompleteBox.TextProperty, new Binding("PortName") { Mode = BindingMode.TwoWay });
        Field(advanced, "Фиксированный COM-порт (необязательно)", port);
        TextField("Автопоиск: часть названия адаптера", "PortDescriptionContains");
        TextField("Автопоиск: USB VID (4 цифры, например 1A86)", "UsbVendorId", true);
        TextField("Автопоиск: USB PID (4 цифры, например 7523)", "UsbProductId", true);
        advanced.Children.Add(new TextBlock { Text = "Для поиска достаточно совпадения названия или пары VID/PID. Указанный COM-порт отключает автопоиск.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        TextField("Скорость, бод", "BaudRate"); TextField("Биты данных", "DataBits", true);
        void Selector<T>(string label, string property, IEnumerable<T> values, Func<T, string> display)
        {
            var input = new ComboBox { ItemsSource = values, HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemTemplate = new FuncDataTemplate<T>((value, _) => new TextBlock { Text = display(value!) }) };
            input.Bind(ComboBox.SelectedItemProperty, new Binding(property) { Mode = BindingMode.TwoWay });
            Field(advanced, label, input);
        }
        Selector("Чётность", "Parity", Enum.GetValues<Parity>(), p => RussianUiText.Display(p));
        Selector("Стоп-биты", "StopBits", new[] { StopBits.One, StopBits.Two, StopBits.OnePointFive }, p => RussianUiText.Display(p));
        Selector("Кодировка", "EncodingName", new[] { "utf-8", "us-ascii", "iso-8859-1" }, s => s);
        Selector("Окончание команды", "LineEnding", new[] { "\n", "\r", "\r\n", "" }, s => s switch
        { "\n" => "Перевод строки (LF)", "\r" => "Возврат каретки (CR)", "\r\n" => "Возврат и перевод строки (CR+LF)", _ => "Не добавлять" });
        foreach (var (label, property) in new[] { ("Включить DTR", "DtrEnable"), ("Включить RTS", "RtsEnable") })
        {
            var input = new CheckBox { Content = label }; input.Bind(CheckBox.IsCheckedProperty, new Binding(property) { Mode = BindingMode.TwoWay });
            advanced.Children.Add(input);
        }
        _consoleFields.Children.Add(new Expander { Header = "Дополнительные параметры", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch });
        right.Children.Add(_consoleFields);
        var scroll = new ScrollViewer { Content = right }; Grid.SetColumn(scroll, 2); page.Children.Add(scroll);
        _consoleList.SelectedIndex = _consoles.Count > 0 ? 0 : -1;
        _consoleFields.IsVisible = _consoles.Count > 0;
        return page;
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
