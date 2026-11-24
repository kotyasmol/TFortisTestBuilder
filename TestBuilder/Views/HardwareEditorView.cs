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
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using TestBuilder.Domain.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.Services;

namespace TestBuilder.Views;

public sealed partial class HardwareEditorView : UserControl
{
    private readonly TestViewModel _vm;
    private HardwareConfiguration _draft;
    private readonly ObservableCollection<DeviceDefinition> _devices;
    private readonly DataGrid _devicesGrid = new() { AutoGenerateColumns = false, MinHeight = 140 };
    private readonly DataGrid _registersGrid = new() { Name = "ProfileRegistersGrid", AutoGenerateColumns = false, MinHeight = 220 };
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

    public HardwareEditorView(TestViewModel vm) : this(vm, new EquipmentLibrary()) { }
    internal HardwareEditorView(TestViewModel vm, EquipmentLibrary library)
    {
        _library = library;
        _vm = vm;
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
        var repeatHeader = new TextBlock { Text = "Повтор состояния" };
        ToolTip.SetTip(repeatHeader, "Повторить запись при несовпадении контрольного чтения. Только для установки состояния; для командных регистров оставьте выключенным.");
        _registersGrid.Columns.Add(new DataGridCheckBoxColumn { Header = repeatHeader, Binding = new Binding("RetryWriteOnMismatch") { Mode = BindingMode.TwoWay } });
        _devicesGrid.SelectionChanged += (_, _) => DeviceSelectionChanged();
        var root = new DockPanel { Margin = new Thickness(20) };
        var scope = new TextBlock { Name = "HardwareProfileContext", Text = "Текущий профиль: " + vm.CurrentProfileName,
            FontSize = 20, FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        DockPanel.SetDock(scope, Dock.Top); root.Children.Add(scope);
        var bottom = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        bottom.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(Button("Применить к профилю", () => {
            try { CommitGrid(); _vm.ApplyHardware(_legacy.IsChecked == true ? null : GetDraft()); Finish(); }
            catch (Exception ex) { _status.Text = ex.Message; }
        }));
        var cancel = Button("Отмена", Finish); cancel.IsCancel = true; buttons.Children.Add(cancel);
        bottom.Children.Add(buttons);
        bottom.Children.Add(new TextBlock { Text = "После применения сохраните профиль в редакторе.", Classes = { "hint" } });
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);

        var connection = new StackPanel { Spacing = 18, Margin = new Thickness(8, 20), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        connection.Children.Add(_legacy);
        var stand = new StackPanel { Spacing = 6 };
        stand.Children.Add(new TextBlock { Text = "Название стенда" });
        stand.Children.Add(_standName);
        connection.Children.Add(stand);
        connection.Children.Add(new TextBlock { Text = "Параметры линии Modbus этого профиля. Добавьте устройства и назначьте slave-адреса на вкладке «Оборудование профиля».", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var serial = new WrapPanel();
        connection.Children.Add(new TextBlock { Text = "COM-порт определяется автоматически по ответам устройств профиля." });
        Field(serial, "Скорость, бод", _baud); Field(serial, "Биты данных", _bits); Field(serial, "Чётность", _parity); Field(serial, "Стоп-биты", _stop); Field(serial, "Опрос, мс", _poll);
        connection.Children.Add(serial);
        connection.Children.Add(new TextBlock { Text = "Параметры линии и ожидаемые устройства сохраняются в профиле. Номер COM-порта стенда не нужен. Отдельный USB-UART процессора добавляется на вкладке «Консоли».", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var transfer = new WrapPanel();
        transfer.Children.Add(Button("Импорт оборудования…", async () => await ImportAsync()));
        var export = Button("Экспорт оборудования…", async () => await ExportAsync()); transfer.Children.Add(export);
        connection.Children.Add(transfer);

        var devicesPage = CreateProfileEquipmentPage();
        var deviceTab = new TabItem { Header = "Оборудование профиля", Content = devicesPage };
        var consoleTab = new TabItem { Header = "Консоли", Content = CreateConsolePage() };
        _tabs.Items.Add(deviceTab);
        _tabs.Items.Add(new TabItem { Header = "Подключение", Content = new ScrollViewer { Content = connection } });
        _tabs.Items.Add(consoleTab);
        void ModeChanged() { var manual = _legacy.IsChecked != true; stand.IsEnabled = manual; serial.IsEnabled = manual; consoleTab.IsEnabled = manual; export.IsEnabled = manual; }
        _legacy.IsCheckedChanged += (_, _) => ModeChanged(); ModeChanged();
        root.Children.Add(_tabs); _mainPage = root; _pageHost.Content = root; Content = _pageHost;
        LoadConnection(); _devicesGrid.SelectedIndex = _devices.Count == 0 ? -1 : 0; ShowRegisters();
        _tabs.SelectedIndex = 0;
        ConfigureProfileColumns(); UpdateProfileState();
    }
    private void CommitGrid()
    {
        if (!_devicesGrid.CommitEdit() || !_registersGrid.CommitEdit() || _deviceInputs.Any(DataValidationErrors.GetHasErrors) || _identification.Children.OfType<TextBox>().Any(DataValidationErrors.GetHasErrors) || _consoleInputs.Any(DataValidationErrors.GetHasErrors))
            throw new InvalidOperationException("Исправьте некорректное значение в оборудовании.");
        var invalid = _devices.FirstOrDefault(d => d.SlaveId is < 1 or > 247);
        if (invalid != null) throw new InvalidOperationException($"Устройство «{invalid.Name}»: укажите Slave-адрес от 1 до 247.");
        var duplicate = _devices.GroupBy(d => d.SlaveId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null) throw new InvalidOperationException($"Slave-адрес {duplicate.Key} повторяется. Назначьте каждому устройству отдельный адрес.");
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
        var changed = !ReferenceEquals(device, _shownDevice);
        _identification.DataContext = device;
        _deviceFields.DataContext = device; _shownDevice = device;
        _shownDeviceName = device?.Name; _shownSlave = device?.SlaveId ?? 0;
        UpdateProfileState();
        _registerTitle.Text = device == null ? "Выберите устройство" : $"Регистры: {RussianUiText.Display(device.Name)}";
        if (changed) _registerSearch.Text = "";
        FilterRegisters();
    }
    private void FilterRegisters()
    {
        var query = _registerSearch.Text?.Trim() ?? "";
        var selected = _registersGrid.SelectedItem;
        var rows = (_devicesGrid.SelectedItem as DeviceDefinition)?.Registers
            .Where(r => query.Length == 0 || r.Address.ToString().Contains(query) || r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || RussianUiText.Display(r.Name).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        _registersGrid.ItemsSource = rows;
        if (selected is RegisterDefinition register && rows?.Contains(register) == true) _registersGrid.SelectedItem = register;
        UpdateRegisterActions();
    }
    private async Task ImportAsync()
    {
        try
        {
            var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Описание оборудования JSON", AllowMultiple = false });
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
            var file = await TopLevel.GetTopLevel(this)!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Экспорт оборудования", SuggestedFileName = "equipment.json", DefaultExtension = "json" });
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
    private static void AddColumn(DataGrid grid, string title, string path)
    {
        DataGridTextColumn column = path == "Name" ? new TranslatedNameColumn()
            : new DataGridTextColumn { Binding = new Binding(path) { Mode = BindingMode.TwoWay } };
        column.Header = title;
        column.Width = path == "Name" ? new DataGridLength(1, DataGridLengthUnitType.Star)
            : new DataGridLength(path == "Category" ? 110 : path == "Unit" ? 65 : 75);
        column.MinWidth = path == "Name" ? 180 : 60;
        grid.Columns.Add(column);
    }
    private static void Field(Panel panel, string name, Control input) { input.MinWidth = 85; var stack = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; stack.Children.Add(new TextBlock { Text = name }); stack.Children.Add(input); panel.Children.Add(stack); }
    private static Button Button(string title, Action action) { var b = new Button { Content = title, Margin = new Thickness(0, 4, 8, 4) }; b.Click += (_, _) => action(); return b; }
}
