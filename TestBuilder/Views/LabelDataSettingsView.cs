using System;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TestBuilder.Services;

namespace TestBuilder.Views;

internal sealed class LabelDataSettingsView : UserControl
{
    public event Action? Changed;
    private bool _updating;
    private readonly ComboBox _serialSource = new() { Name = "SerialTextSource", ItemsSource = new[] { "Короткий номер без типа", "Полный серийный номер", "Свой формат", "Последние цифры полного номера" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _barcodeSource = new() { Name = "BarcodeDataSource", ItemsSource = new[] { "Тип + короткий номер", "Служебный префикс + тип + короткий номер", "Только короткий номер", "Полный серийный номер", "Свой формат", "Последние цифры полного номера" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _serialDigits = Digits("PrintedSerialDigits"), _barcodeDigits = Digits("BarcodeSerialDigits"), _typeDigits = Digits("BarcodeTypeDigits");
    private readonly TextBox _serialFormat = new() { Name = "PrintedSerialFormat" }, _barcodeFormat = new() { Name = "BarcodeDataFormat" }, _service = new() { Name = "BarcodeServicePrefix" };

    public LabelDataSettingsView()
    {
        var root = new StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(0, 8, 0, 8) };
        Add(root, "Что печатать в строке SN", _serialSource); Add(root, "Цифр SN в тексте", _serialDigits);
        Add(root, "Формат надписи SN", _serialFormat);
        Add(root, "Что кодировать в штрихкоде", _barcodeSource);
        var digits = new Grid { ColumnDefinitions = new("*,12,*") };
        var a = new StackPanel { Spacing = 4 }; Add(a, "Цифр типа устройства", _typeDigits); digits.Children.Add(a);
        var b = new StackPanel { Spacing = 4 }; Add(b, "Цифр SN в штрихкоде", _barcodeDigits); Grid.SetColumn(b, 2); digits.Children.Add(b); root.Children.Add(digits);
        Add(root, "Служебный префикс (если включён в формат)", _service);
        Add(root, "Формат данных штрихкода", _barcodeFormat);
        root.Children.Add(new TextBlock { Text = "{{Serial}} — полный номер, {{ShortSerial:5}} — номер после вычитания смещения с нулями до 5 цифр, {{SerialTail:5}} — последние 5 цифр полного номера, {{DeviceCode:3}} — тип, {{ServicePrefix}} — служебный префикс, {{VariantCode}} — код комплектации, {{Mac}} — MAC. Короткий SN требует включённого разбора и смещения. Разрядность дополняет нулями; обрезание выполняет только SerialTail.", TextWrapping = TextWrapping.Wrap, Classes = { "hint" } });
        Content = root;
        DataContextChanged += (_, _) => Load();
        _serialSource.SelectionChanged += (_, _) => ApplyPreset(true); _serialDigits.SelectionChanged += (_, _) => ApplyPreset(true);
        foreach (var box in new[] { _barcodeSource, _barcodeDigits, _typeDigits }) box.SelectionChanged += (_, _) => ApplyPreset(false);
        _serialFormat.TextChanged += (_, _) => EditFormat(true); _barcodeFormat.TextChanged += (_, _) => EditFormat(false);
        _service.TextChanged += (_, _) => { if (!_updating && DataContext is LabelTemplateDefinition t) { t.ServicePrefix = _service.Text ?? ""; Changed?.Invoke(); } };
    }

    private void Load()
    {
        if (DataContext is not LabelTemplateDefinition t) return;
        _updating = true;
        try
        {
            _serialFormat.Text = t.SerialTextFormat ?? "{{Serial}}";
            _barcodeFormat.Text = t.BarcodeFormat ?? (t.UseShortSerial ? "{{BarcodePrefix}}{{ShortSerial}}" : "{{Serial}}");
            _serialSource.SelectedIndex = SerialMode(_serialFormat.Text);
            _barcodeSource.SelectedIndex = BarcodeMode(_barcodeFormat.Text);
            LoadDigits(t);
            _service.Text = t.ServicePrefix;
            UpdateEnabled();
        }
        finally { _updating = false; }
    }
    private static int Precision(string? pattern, string field, int fallback)
    {
        var match = Regex.Match(pattern ?? "", @"\{\{" + field + @":([0-9]+)\}\}");
        return match.Success && int.TryParse(match.Groups[1].Value, out var digits) && digits is >= 1 and <= 10 ? digits : fallback;
    }
    private static int SerialMode(string? pattern) => Regex.IsMatch(pattern ?? "", @"^\{\{ShortSerial(?::[0-9]+)?\}\}$") ? 0
        : pattern == "{{Serial}}" ? 1 : Regex.IsMatch(pattern ?? "", @"^\{\{SerialTail:[0-9]+\}\}$") ? 3 : 2;
    private static int BarcodeMode(string? pattern)
    {
        var normalized = Regex.Replace(pattern ?? "", @":[0-9]+\}\}", "}}");
        return normalized switch { "{{DeviceCode}}{{ShortSerial}}" => 0, "{{ServicePrefix}}{{DeviceCode}}{{ShortSerial}}" => 1,
            "{{SerialTail}}" => 5,
            "{{ShortSerial}}" => 2, "{{Serial}}" => 3, _ => 4 };
    }
    private void UpdateEnabled()
    {
        _serialDigits.IsEnabled = _serialSource.SelectedIndex is 0 or 3;
        _typeDigits.IsEnabled = _barcodeSource.SelectedIndex is 0 or 1;
        _barcodeDigits.IsEnabled = _barcodeSource.SelectedIndex is 0 or 1 or 2 or 5;
    }
    private void LoadDigits(LabelTemplateDefinition t)
    {
        _serialDigits.SelectedItem = Precision(_serialFormat.Text, _serialSource.SelectedIndex == 3 ? "SerialTail" : "ShortSerial", t.ShortSerialDigits);
        _barcodeDigits.SelectedItem = Precision(_barcodeFormat.Text, _barcodeSource.SelectedIndex == 5 ? "SerialTail" : "ShortSerial", t.ShortSerialDigits);
        _typeDigits.SelectedItem = Precision(_barcodeFormat.Text, "DeviceCode", 3);
    }
    private void ApplyPreset(bool serial)
    {
        if (_updating || DataContext is not LabelTemplateDefinition t) return;
        _updating = true;
        try
        {
            var sn = "{{ShortSerial:" + (_serialDigits.SelectedItem ?? 5) + "}}";
            var barSn = "{{ShortSerial:" + (_barcodeDigits.SelectedItem ?? 5) + "}}";
            var type = "{{DeviceCode:" + (_typeDigits.SelectedItem ?? 3) + "}}";
            var current = serial ? t.SerialTextFormat ?? "{{Serial}}" : t.BarcodeFormat ?? (t.UseShortSerial ? "{{BarcodePrefix}}{{ShortSerial}}" : "{{Serial}}");
            var next = serial ? _serialSource.SelectedIndex switch { 0 => sn, 3 => "{{SerialTail:" + (_serialDigits.SelectedItem ?? 5) + "}}", _ => "{{Serial}}" }
                : _barcodeSource.SelectedIndex switch { 0 => type + barSn, 1 => "{{ServicePrefix}}" + type + barSn, 2 => barSn, 5 => "{{SerialTail:" + (_barcodeDigits.SelectedItem ?? 5) + "}}", _ => "{{Serial}}" };
            // Avalonia can deliver selection events after the initial load guard has ended.
            // Equivalent presets must leave the stored rule (including null legacy rules) intact.
            var canonical = current.Replace("{{ShortSerial}}", "{{ShortSerial:" + t.ShortSerialDigits + "}}")
                .Replace("{{DeviceCode}}", "{{DeviceCode:3}}");
            if (serial && _serialSource.SelectedIndex == 2 || !serial && _barcodeSource.SelectedIndex == 4 || canonical == next)
            { UpdateEnabled(); return; }
            if (serial) t.SerialTextFormat = _serialFormat.Text = next;
            else t.BarcodeFormat = _barcodeFormat.Text = next;
            ConnectFields(t, serial); UpdateEnabled();
        }
        finally { _updating = false; }
        Changed?.Invoke();
    }
    private void EditFormat(bool serial)
    {
        if (_updating || DataContext is not LabelTemplateDefinition t) return;
        var current = serial ? t.SerialTextFormat ?? "{{Serial}}" : t.BarcodeFormat ?? (t.UseShortSerial ? "{{BarcodePrefix}}{{ShortSerial}}" : "{{Serial}}");
        if ((serial ? _serialFormat.Text : _barcodeFormat.Text) == current) return;
        if (serial) t.SerialTextFormat = _serialFormat.Text ?? ""; else t.BarcodeFormat = _barcodeFormat.Text ?? "";
        _updating = true;
        try { _serialSource.SelectedIndex = SerialMode(_serialFormat.Text); _barcodeSource.SelectedIndex = BarcodeMode(_barcodeFormat.Text); LoadDigits(t); UpdateEnabled(); ConnectFields(t, serial); }
        finally { _updating = false; }
        Changed?.Invoke();
    }
    private static void ConnectFields(LabelTemplateDefinition t, bool serial)
    {
        if (t.Layout != null)
        {
            foreach (var element in t.Layout.Elements)
            {
                if (serial && element.Kind == "Text" && element.Value is "{{Serial}}" or "SN: {{Serial}}") element.Value = element.Value.Replace("{{Serial}}", "{{SerialText}}");
                if (!serial && element.Kind == "Barcode" && element.Value is "{{Serial}}" or "{{ShortSerial}}") element.Value = "{{Barcode}}";
            }
        }
        else if (t.Language == "ZPL")
            t.Command = Regex.Replace(t.Command, @"\^FO[^\^]+.*?\^FS", m => m.Value.Contains("^BC")
                ? !serial ? m.Value.Replace("{{Serial}}", "{{Barcode}}") : m.Value
                : serial ? m.Value.Replace("{{Serial}}", "{{SerialText}}") : m.Value, RegexOptions.Singleline);
        else
            t.Command = Regex.Replace(t.Command, @"(?m)^[AB][^\r\n]+", m => m.Value[0] == 'B'
                ? !serial ? m.Value.Replace("{{Serial}}", "{{Barcode}}") : m.Value
                : serial ? m.Value.Replace("{{Serial}}", "{{SerialText}}") : m.Value);
    }
    private static ComboBox Digits(string name) => new() { Name = name, ItemsSource = Enumerable.Range(1, 10).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
    private static void Add(Panel panel, string label, Control control)
    {
        Avalonia.Automation.AutomationProperties.SetName(control, label);
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }); panel.Children.Add(control);
    }
}
