using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using TestBuilder.Domain.Modbus;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public sealed partial class HardwareEditorView
{
    private readonly Grid _deviceFields = new() { ColumnDefinitions = new ColumnDefinitions("*,16,150"), Name = "ProfileDeviceFields" };
    private readonly List<TextBox> _deviceInputs = new();
    private readonly List<DataGridColumn> _extraRegisterColumns = new();
    private readonly TextBlock _profileCount = new() { FontSize = 18, FontWeight = FontWeight.SemiBold };
    private readonly StackPanel _profileEmpty = new() { Name = "ProfileEquipmentEmpty", Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 460 };
    private readonly Grid _profileBody = new() { Name = "ProfileEquipmentBody", ColumnDefinitions = new ColumnDefinitions("250,16,*") };
    private readonly StackPanel _selectedDevicePane = new() { Name = "SelectedDeviceFields", Spacing = 8 };
    private readonly Button _removeDevice = new() { Content = "Убрать устройство", Name = "RemoveProfileDevice" };
    private readonly Button _saveDevice = new() { Content = "Сохранить как новое в каталоге…" };
    private readonly Button _editRegister = new() { Content = "Изменить регистр…", Name = "EditProfileRegister", Margin = new Thickness(0, 4, 8, 4) };
    private readonly Button _removeRegister = new() { Content = "Удалить регистр", Name = "RemoveProfileRegister", Margin = new Thickness(0, 4, 8, 4) };
    private readonly Button _addRegister = new() { Content = "Добавить регистр", Name = "AddProfileRegister", Margin = new Thickness(0, 4, 8, 4) };
    private DeviceDefinition? _shownDevice;
    private string? _shownDeviceName;
    private byte _shownSlave;
    private bool _changingDevice;

    private Control CreateProfileEquipmentPage()
    {
        var page = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        _profileEmpty.Children.Add(new TextBlock { Text = "В профиле пока нет оборудования", FontSize = 22, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        _profileEmpty.Children.Add(new TextBlock { Text = "Выберите шаблон и задайте адреса плат. Можно добавить несколько одинаковых устройств за один раз.", TextWrapping = TextWrapping.Wrap });
        var start = Button("Добавить устройства…", async () => await AddEquipmentAsync()); start.Name = "AddFirstProfileEquipment";
        start.Classes.Add("primary");
        _profileEmpty.Children.Add(start); page.Children.Add(_profileEmpty);
        var left = new DockPanel();
        var top = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(_profileCount); var add = Button("Добавить устройства…", async () => await AddEquipmentAsync()); add.Name = "AddProfileEquipment"; add.Classes.Add("primary"); top.Children.Add(add);
        DockPanel.SetDock(top, Dock.Top); left.Children.Add(top);
        var bottom = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        _removeDevice.Click += (_, _) => RemoveSelectedDevice(); bottom.Children.Add(_removeDevice);
        _saveDevice.Click += async (_, _) =>
        {
            try { CommitGrid(); if (_devicesGrid.SelectedItem is DeviceDefinition device) await EditTemplateAsync(null, device); }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
        var more = new Expander { Header = "Каталог шаблонов", Content = _saveDevice, HorizontalAlignment = HorizontalAlignment.Stretch };
        bottom.Children.Add(more); DockPanel.SetDock(bottom, Dock.Bottom); left.Children.Add(bottom);
        left.Children.Add(_devicesGrid); _profileBody.Children.Add(left);
        var right = new DockPanel();
        var header = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 10) };
        foreach (var (label, property, column) in new[] { ("Название устройства", "Name", 0), ("Slave-адрес", "SlaveId", 2) })
        {
            var field = new StackPanel { Spacing = 4 };
            var input = new TextBox { Name = "ProfileDevice" + property }; input.Bind(TextBox.TextProperty, new Binding(property) { Mode = BindingMode.TwoWay });
            Avalonia.Automation.AutomationProperties.SetName(input, label);
            _deviceInputs.Add(input); input.LostFocus += (_, _) => RefreshDeviceLabels();
            field.Children.Add(new TextBlock { Text = label }); field.Children.Add(input); Grid.SetColumn(field, column); _deviceFields.Children.Add(field);
        }
        _selectedDevicePane.Children.Add(_deviceFields);
        foreach (var (label, property) in new[] { ("Регистр типа (хранения)", "IdentificationAddress"), ("Допустимые значения типа через запятую", "IdentificationValuesText"), ("Регистр серийного номера стенда (необязательно)", "StandSerialNumberAddress") })
        {
            var input = new TextBox { Name = "ProfileDevice" + property }; input.Bind(TextBox.TextProperty, new Binding(property) { Mode = BindingMode.TwoWay });
            _identification.Children.Add(new TextBlock { Text = label }); _identification.Children.Add(input);
        }
        _selectedDevicePane.Children.Add(new Expander { Header = "Определение типа и номер стенда", Content = _identification, HorizontalAlignment = HorizontalAlignment.Stretch });
        header.Children.Add(_selectedDevicePane); header.Children.Add(_registerTitle); header.Children.Add(_registerSearch);
        var actions = new WrapPanel();
        _addRegister.Click += async (_, _) => await EditRegisterAsync(false);
        _editRegister.Click += async (_, _) => await EditRegisterAsync(true);
        _removeRegister.Click += (_, _) => RemoveSelectedRegister();
        actions.Children.Add(_addRegister); actions.Children.Add(_editRegister); actions.Children.Add(_removeRegister);
        var all = new CheckBox { Content = "Все поля", Name = "ShowAllRegisterFields", Margin = new Thickness(8, 4) };
        all.IsCheckedChanged += (_, _) => { foreach (var column in _extraRegisterColumns) column.IsVisible = all.IsChecked == true; };
        actions.Children.Add(all); header.Children.Add(actions);
        DockPanel.SetDock(header, Dock.Top); right.Children.Add(header); right.Children.Add(_registersGrid);
        Grid.SetColumn(right, 2); _profileBody.Children.Add(right); page.Children.Add(_profileBody);
        _devices.CollectionChanged += (_, _) => UpdateProfileState();
        _registerSearch.TextChanged += (_, _) => FilterRegisters();
        _registersGrid.SelectionChanged += (_, _) => UpdateRegisterActions();
        return page;
    }

    private void ConfigureProfileColumns()
    {
        _devicesGrid.Columns[0].Width = new DataGridLength(85);
        _devicesGrid.Columns[0].MinWidth = 85;
        if (_devicesGrid.Columns[1] is TranslatedNameColumn deviceName) { deviceName.WrapText = true; deviceName.MinWidth = 130; }
        foreach (var column in _registersGrid.Columns)
        {
            if (column is TranslatedNameColumn name) { name.WrapText = true; name.MinWidth = 220; }
            else if (column is DataGridTextColumn text && text.Binding is Binding binding && binding.Path is "Unit" or "Category" || column.Header is TextBlock)
            { column.IsVisible = false; _extraRegisterColumns.Add(column); }
        }
        _registersGrid.Columns[0].Width = new DataGridLength(100);
        _registersGrid.Columns[0].MinWidth = 100;
        _registersGrid.Columns[2].Width = new DataGridLength(180);
    }
    private void UpdateProfileState()
    {
        _profileEmpty.IsVisible = _devices.Count == 0; _profileBody.IsVisible = _devices.Count > 0;
        _profileCount.Text = $"Устройства · {_devices.Count}";
        var selected = _devicesGrid.SelectedItem is DeviceDefinition;
        _selectedDevicePane.IsVisible = selected; _removeDevice.IsEnabled = _saveDevice.IsEnabled = selected;
        _registerSearch.IsEnabled = _addRegister.IsEnabled = selected;
        UpdateRegisterActions();
    }
    private void UpdateRegisterActions()
    {
        var selected = _registersGrid.SelectedItem is RegisterDefinition;
        _editRegister.IsEnabled = _removeRegister.IsEnabled = selected;
    }
    private void DeviceSelectionChanged()
    {
        if (_changingDevice) return;
        if (_shownDevice != null && _devices.Contains(_shownDevice) && (_deviceInputs.Any(DataValidationErrors.GetHasErrors) || _identification.Children.OfType<TextBox>().Any(DataValidationErrors.GetHasErrors)))
        {
            _changingDevice = true; _devicesGrid.SelectedItem = _shownDevice; _changingDevice = false;
            _status.Text = "Исправьте выделенное поле устройства перед переключением."; return;
        }
        ShowRegisters();
    }
    private void RefreshDeviceLabels()
    {
        if (_changingDevice || _deviceInputs.Any(DataValidationErrors.GetHasErrors)) return;
        if (_devicesGrid.SelectedItem is not DeviceDefinition device || device.Name == _shownDeviceName && device.SlaveId == _shownSlave) return;
        var selected = _devicesGrid.SelectedItem;
        _changingDevice = true; _devicesGrid.ItemsSource = null; _devicesGrid.ItemsSource = _devices; _devicesGrid.SelectedItem = selected; _changingDevice = false;
        ShowRegisters();
    }
    private void RemoveSelectedDevice()
    {
        if (_devicesGrid.SelectedItem is not DeviceDefinition device) return;
        var index = _devices.IndexOf(device);
        _changingDevice = true; _devicesGrid.CancelEdit(); _registersGrid.CancelEdit(); _devices.Remove(device);
        _devicesGrid.SelectedItem = _devices.ElementAtOrDefault(Math.Min(index, _devices.Count - 1)); _changingDevice = false; ShowRegisters();
        if (_devices.Count == 0) _legacy.IsChecked = true;
    }
    private async Task AddEquipmentAsync(string? selected = null)
    {
        if (_cancelInline != null) return;
        try
        {
            CommitGrid();
            var result = await EditInlineAsync(new AddProfileEquipmentView(_library, _devices.Select(d => d.SlaveId), selected,
                _devices.Any(d => d.StandSerialNumberAddress.HasValue)), "Добавить устройства в профиль");
            if (result == null) return;
            foreach (var device in result) _devices.Add(device);
            _devicesGrid.SelectedItem = result[0]; _legacy.IsChecked = false; _tabs.SelectedIndex = 0;
            _devicesGrid.ScrollIntoView(result[0], _devicesGrid.Columns[0]);
            _status.Text = $"Добавлено устройств: {result.Count}. Изменения запишутся после «Применить к профилю».";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private async Task EditRegisterAsync(bool editing)
    {
        if (_cancelInline != null) return;
        try
        {
            CommitGrid();
            if (_devicesGrid.SelectedItem is not DeviceDefinition device) return;
            var source = editing ? _registersGrid.SelectedItem as RegisterDefinition : new RegisterDefinition { Address = device.Registers.Count == 0 ? 0 : Math.Min(65535, device.Registers.Max(r => r.Address) + 1), Name = "" };
            if (source == null) return;
            var result = await EditInlineAsync(new RegisterDefinitionEditor(source, device.Registers, editing), editing ? "Изменить регистр" : "Добавить регистр");
            if (result == null) return;
            if (editing) device.Registers[device.Registers.IndexOf(source)] = result; else device.Registers.Add(result);
            _registerSearch.Text = ""; FilterRegisters(); _registersGrid.SelectedItem = result;
            _registersGrid.ScrollIntoView(result, _registersGrid.Columns[1]);
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private void RemoveSelectedRegister()
    {
        if (_devicesGrid.SelectedItem is not DeviceDefinition device || _registersGrid.SelectedItem is not RegisterDefinition register) return;
        _registersGrid.CancelEdit(); device.Registers.Remove(register); FilterRegisters();
    }
}
