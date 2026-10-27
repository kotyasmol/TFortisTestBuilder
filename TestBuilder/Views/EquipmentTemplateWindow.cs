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
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public sealed class EquipmentTemplateWindow : Window
{
    public EquipmentTemplateWindow(EquipmentLibrary library, DeviceDefinition source, string? id = null)
    {
        Title = id == null ? "Новое оборудование в каталоге" : "Изменить оборудование в каталоге";
        Width = 980; Height = 720; MinWidth = 800; MinHeight = 550;
        var draft = TestViewModel.CopyDefinition(source);
        var registers = new ObservableCollection<RegisterDefinition>(draft.Registers);
        var root = new DockPanel { Margin = new Thickness(20) };
        var fields = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 12) };
        var inputs = new System.Collections.Generic.List<TextBox>();
        foreach (var (label, property) in new[] { ("Название оборудования", "Name"),
            ("Регистр типа (Holding)", "IdentificationAddress"),
            ("Допустимые значения типа, например 9, 10", "IdentificationValuesText"),
            ("Регистр серийного номера стенда (необязательно)", "StandSerialNumberAddress") })
        {
            fields.Children.Add(new TextBlock { Text = label });
            var input = new TextBox { DataContext = draft, Name = "Equipment" + property };
            input.Bind(TextBox.TextProperty, new Binding(property) { Mode = BindingMode.TwoWay });
            fields.Children.Add(input); inputs.Add(input);
        }
        fields.Children.Add(new TextBlock { Text = "Slave-адрес назначается при добавлении оборудования в профиль." });
        DockPanel.SetDock(fields, Dock.Top); root.Children.Add(fields);
        var grid = new DataGrid { AutoGenerateColumns = false, ItemsSource = registers };
        foreach (var (label, property) in new[] { ("Адрес", "Address"), ("Название регистра", "Name"), ("Единица", "Unit"), ("Группа", "Category") })
            grid.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(property) { Mode = BindingMode.TwoWay },
                Width = property == "Name" ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(100) });
        grid.Columns.Add(new DataGridTemplateColumn { Header = "Область", Width = new DataGridLength(110),
            CellTemplate = new FuncDataTemplate<RegisterDefinition>((r, _) => new TextBlock { Text = r?.Area.ToString() }),
            CellEditingTemplate = new FuncDataTemplate<RegisterDefinition>((r, _) =>
            {
                var box = new ComboBox { ItemsSource = Enum.GetValues<ModbusArea>() };
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
            Close(saved);
        });
        AddButton("Отмена", Close);
        footer.Children.Add(status); footer.Children.Add(actions);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(grid); Content = root;
    }
}
