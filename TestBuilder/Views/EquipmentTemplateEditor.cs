using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Presentation;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

internal sealed class EquipmentTemplateEditor : InlineEquipmentEditor<EquipmentTemplate>
{
    public EquipmentTemplateEditor(EquipmentLibrary library, DeviceDefinition source, string? id = null)
    {
        var draft = TestViewModel.CopyDefinition(source);
        var registers = new ObservableCollection<RegisterDefinition>(draft.Registers);
        var root = new DockPanel { Margin = new Thickness(0) };
        var fields = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 12) };
        var inputs = new System.Collections.Generic.List<TextBox>();
        var identification = new StackPanel { Spacing = 4 };
        foreach (var (label, property) in new[] { ("Название оборудования", "Name"),
            ("Регистр типа (Holding)", "IdentificationAddress"),
            ("Допустимые значения типа, например 9, 10", "IdentificationValuesText"),
            ("Регистр серийного номера стенда (необязательно)", "StandSerialNumberAddress") })
        {
            var target = property == "Name" ? fields : identification;
            target.Children.Add(new TextBlock { Text = label });
            var input = new TextBox { DataContext = draft, Name = "Equipment" + property };
            input.Bind(TextBox.TextProperty, new Binding(property) { Mode = BindingMode.TwoWay });
            target.Children.Add(input); inputs.Add(input);
        }
        fields.Children.Add(new Expander { Header = "Определение типа и номер стенда", Content = identification, HorizontalAlignment = HorizontalAlignment.Stretch });
        fields.Children.Add(new TextBlock { Text = "Slave-адрес назначается при добавлении оборудования в профиль." });
        DockPanel.SetDock(fields, Dock.Top); root.Children.Add(fields);
        var grid = new DataGrid { Name = "TemplateRegistersGrid", AutoGenerateColumns = false, ItemsSource = registers };
        foreach (var (label, property) in new[] { ("Адрес", "Address"), ("Название регистра", "Name"), ("Единица", "Unit"), ("Группа", "Category") })
            grid.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(property) { Mode = BindingMode.TwoWay },
                Width = property == "Name" ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(100) });
        grid.Columns.Add(new DataGridTemplateColumn { Header = "Область", Width = new DataGridLength(160),
            CellTemplate = new FuncDataTemplate<RegisterDefinition>((r, _) => new TextBlock { Text = r == null ? "" : RussianUiText.Display(r.Area), TextWrapping = Avalonia.Media.TextWrapping.Wrap }),
            CellEditingTemplate = new FuncDataTemplate<RegisterDefinition>((r, _) =>
            {
                var box = new ComboBox { ItemsSource = Enum.GetValues<ModbusArea>(),
                    ItemTemplate = new FuncDataTemplate<ModbusArea>((area, _) => new TextBlock { Text = RussianUiText.Display(area) }) };
                box.Bind(ComboBox.SelectedItemProperty, new Binding("Area") { Source = r, Mode = BindingMode.TwoWay });
                return box;
            }) });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Запись", Binding = new Binding("Writable") { Mode = BindingMode.TwoWay } });
        var footer = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var actions = new WrapPanel();
        void AddButton(string text, Action action)
        {
            var button = new Button { Name = text == "Сохранить в каталог" ? "SaveEquipmentTemplate" : null, Content = text, Margin = new Thickness(0, 0, 8, 0) };
            button.Click += (_, _) => { try { action(); } catch (Exception ex) { status.Text = ex.Message; } };
            actions.Children.Add(button);
        }
        AddButton("Добавить регистр", () =>
        {
            if (!grid.CommitEdit()) throw new InvalidOperationException("Исправьте значение регистра.");
            var used = registers.Where(r => r.Area == ModbusArea.Holding).Select(r => r.Address).ToHashSet();
            var address = Enumerable.Range(0, 65536).First(n => !used.Contains(n));
            var register = new RegisterDefinition { Address = address };
            registers.Add(register); grid.SelectedItem = register; grid.ScrollIntoView(register, null);
        });
        AddButton("Удалить регистр", () => { if (grid.SelectedItem is RegisterDefinition r) registers.Remove(r); });
        AddButton("Сохранить в каталог", () =>
        {
            if (!grid.CommitEdit() || inputs.Any(DataValidationErrors.GetHasErrors))
                throw new InvalidOperationException("Исправьте значения в полях оборудования.");
            draft.Registers = registers.ToList();
            var saved = library.Save(draft, id);
            Complete(saved);
        });
        AddButton("Отмена", Cancel);
        footer.Children.Add(status); footer.Children.Add(actions);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(grid); Content = root;
    }
}
