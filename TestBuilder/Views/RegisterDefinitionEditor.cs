using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using TestBuilder.Domain.Modbus;
using TestBuilder.Presentation;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

internal sealed class RegisterDefinitionEditor : InlineEquipmentEditor<RegisterDefinition>
{
    public RegisterDefinitionEditor(RegisterDefinition source, IEnumerable<RegisterDefinition> existing, bool editing)
    {
        var draft = TestViewModel.CopyDefinition(source);
        var root = new DockPanel { Margin = new Thickness(0) };
        var status = new TextBlock { Name = "RegisterDefinitionStatus", TextWrapping = TextWrapping.Wrap };
        var footer = new StackPanel { Spacing = 8 }; footer.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var save = new Button { Name = "SaveRegisterDefinition", Content = editing ? "Сохранить регистр" : "Добавить регистр", IsDefault = true, Classes = { "primary" } };
        var cancel = new Button { Content = "Отмена", IsCancel = true }; cancel.Click += (_, _) => Cancel();
        actions.Children.Add(save); actions.Children.Add(cancel); footer.Children.Add(actions); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var form = new StackPanel { Spacing = 10, MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
        var address = new TextBox { Name = "RegisterDefinitionAddress", Text = source.Address.ToString(CultureInfo.InvariantCulture) };
        var name = new TextBox { Name = "RegisterDefinitionName", Text = source.Name };
        var area = new ComboBox { Name = "RegisterDefinitionArea", ItemsSource = Enum.GetValues<ModbusArea>(), SelectedItem = source.Area,
            HorizontalAlignment = HorizontalAlignment.Stretch, ItemTemplate = new FuncDataTemplate<ModbusArea>((value, _) => new TextBlock { Text = RussianUiText.Display(value) }) };
        var unit = new TextBox { Name = "RegisterDefinitionUnit", Text = source.Unit };
        var category = new TextBox { Name = "RegisterDefinitionCategory", Text = source.Category };
        var writable = new CheckBox { Name = "RegisterDefinitionWritable", Content = "Разрешена запись (RW)", IsChecked = source.Writable };
        var repeat = new CheckBox { Name = "RegisterDefinitionRepeat", Content = "Повторять запись состояния при несовпадении", IsChecked = source.RetryWriteOnMismatch };
        Field("Адрес регистра (с нуля)", address); Field("Название", name); Field("Тип регистра", area); Field("Единица измерения", unit); Field("Категория", category);
        form.Children.Add(writable); form.Children.Add(repeat);
        form.Children.Add(new TextBlock { Text = "Повтор включайте для установки состояния. Для разовых команд оставьте выключенным.", Classes = { "hint" }, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new ScrollViewer { Content = form }); Content = root;
        void Access()
        {
            writable.IsEnabled = area.SelectedItem is ModbusArea.Holding or ModbusArea.Coil;
            if (!writable.IsEnabled) writable.IsChecked = false;
            repeat.IsEnabled = writable.IsChecked == true; if (!repeat.IsEnabled) repeat.IsChecked = false;
        }
        area.SelectionChanged += (_, _) => Access(); writable.IsCheckedChanged += (_, _) => Access(); Access();
        foreach (var input in new[] { address, name, unit, category }) input.TextChanged += (_, _) => status.Text = "";
        area.SelectionChanged += (_, _) => status.Text = "";
        save.Click += (_, _) =>
        {
            try
            {
                if (!int.TryParse(address.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number is < 0 or > 65535)
                    throw new InvalidOperationException("Введите адрес от 0 до 65535.");
                if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("Введите название регистра.");
                if (area.SelectedItem is not ModbusArea selected) throw new InvalidOperationException("Выберите тип регистра.");
                if (existing.Any(r => (!editing || !ReferenceEquals(r, source)) && r.Address == number && r.Area == selected))
                    throw new InvalidOperationException("В этой области уже есть регистр с таким адресом.");
                draft.Address = number; draft.Name = name.Text.Trim(); draft.Area = selected; draft.Unit = unit.Text?.Trim() ?? ""; draft.Category = category.Text?.Trim() ?? "";
                draft.Writable = writable.IsChecked == true; draft.RetryWriteOnMismatch = repeat.IsChecked == true;
                Complete(draft);
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        void Field(string label, Control input) { Avalonia.Automation.AutomationProperties.SetName(input, label); var panel = new StackPanel { Spacing = 4 }; panel.Children.Add(new TextBlock { Text = label }); panel.Children.Add(input); form.Children.Add(panel); }
    }
}
