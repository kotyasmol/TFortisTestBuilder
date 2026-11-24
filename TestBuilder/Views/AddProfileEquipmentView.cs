using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

internal sealed class AddProfileEquipmentView : InlineEquipmentEditor<IReadOnlyList<DeviceDefinition>>
{
    private readonly EquipmentTemplateBrowser browser;
    private readonly TextBox count = new() { Name = "EquipmentCount", Text = "1" };
    private readonly TextBox first = new() { Name = "EquipmentFirstSlave" };
    private readonly TextBox step = new() { Name = "EquipmentSlaveStep", Text = "1" };
    private readonly TextBlock status = new() { Name = "EquipmentAddressPreview", TextWrapping = TextWrapping.Wrap };
    private readonly Button add = new() { Name = "ConfirmAddEquipment", Content = "Добавить в профиль", IsDefault = true, Classes = { "primary" } };
    private readonly HashSet<byte> used;
    private readonly bool hasSerialSource;

    public AddProfileEquipmentView(EquipmentLibrary library, IEnumerable<byte> usedIds, string? selected = null, bool hasSerialSource = false)
    {
        used = usedIds.ToHashSet();
        this.hasSerialSource = hasSerialSource;
        var root = new DockPanel { Margin = new Thickness(0) };
        var bottom = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("110,16,150,16,110,*") };
        Field("Количество", count, 0); Field("Первый Slave", first, 2); Field("Шаг адресов", step, 4);
        bottom.Children.Add(fields); bottom.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(add);
        var cancel = new Button { Content = "Отмена", IsCancel = true }; cancel.Click += (_, _) => Cancel(); actions.Children.Add(cancel);
        bottom.Children.Add(actions); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        browser = new EquipmentTemplateBrowser(library); root.Children.Add(browser); Content = root;
        if (selected != null) browser.Reload(selected);
        first.Text = Enumerable.Range(1, 247).FirstOrDefault(n => !used.Contains((byte)n)).ToString(CultureInfo.InvariantCulture);
        count.TextChanged += (_, _) => Update(); first.TextChanged += (_, _) => Update(); step.TextChanged += (_, _) => Update();
        browser.SelectionChanged += Update;
        add.Click += (_, _) =>
        {
            try
            {
                var addresses = Addresses(); var template = browser.Selected ?? throw new InvalidOperationException("Выберите шаблон оборудования.");
                var result = addresses.Select(address => { var device = TestViewModel.CopyDefinition(template.Device); device.SlaveId = address; return device; }).ToArray();
                Complete(result);
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        Update();
        void Field(string label, TextBox input, int column)
        {
            Avalonia.Automation.AutomationProperties.SetName(input, label);
            var field = new StackPanel { Spacing = 4 }; field.Children.Add(new TextBlock { Text = label }); field.Children.Add(input);
            Grid.SetColumn(field, column); fields.Children.Add(field);
        }
    }
    private byte[] Addresses()
    {
        if (browser.Selected == null) throw new InvalidOperationException("Выберите шаблон оборудования.");
        if (used.Count == 247) throw new InvalidOperationException("В профиле заняты все 247 Slave-адресов.");
        int Read(TextBox box, string label) => int.TryParse(box.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value is >= 1 and <= 247
            ? value : throw new InvalidOperationException($"{label}: введите целое число от 1 до 247.");
        var amount = Read(count, "Количество"); var start = Read(first, "Первый Slave"); var increment = Read(step, "Шаг адресов");
        if (browser.Selected.Device.StandSerialNumberAddress.HasValue && (hasSerialSource || amount > 1))
            throw new InvalidOperationException("Источник серийного номера стенда может быть только у одного устройства. Измените состав или шаблон.");
        if (start + (amount - 1) * increment > 247) throw new InvalidOperationException("Последний Slave-адрес больше 247. Уменьшите количество или шаг.");
        var addresses = Enumerable.Range(0, amount).Select(i => (byte)(start + i * increment)).ToArray();
        var conflict = addresses.Where(used.Contains).ToArray();
        if (conflict.Length > 0) throw new InvalidOperationException("Адреса уже заняты в профиле: " + string.Join(", ", conflict) + ". Измените первый адрес или шаг.");
        return addresses;
    }
    private void Update()
    {
        try
        {
            var addresses = Addresses(); add.IsEnabled = true;
            status.Text = "Slave-адреса: " + string.Join(", ", addresses.Take(12)) + (addresses.Length > 12 ? $"… (всего {addresses.Length})" : "") + ". Проверьте адреса физических плат.";
        }
        catch (Exception ex) { add.IsEnabled = false; status.Text = ex.Message; }
    }
}
