using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Views;

public sealed class DataTestPairsWindow : Window
{
    public DataTestPairsWindow(RunDataTestNodeViewModel node)
    {
        Title = "Пары DataTest"; Width = 1060; MinWidth = 900; Height = 620;
        var draft = new DataTestPairEditorViewModel(node);
        var root = new DockPanel { Margin = new Thickness(20) };
        var top = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(new TextBlock { Text = "Каждая строка — проверка между двумя картами стенда. Выберите адреса и скорость каждой пары. Снятая галочка исключает пару из проверки. При одном направлении трафик идёт B → A.", TextWrapping = TextWrapping.Wrap });
        top.Children.Add(new TextBlock { Text = "Адреса и названия карт настраиваются во вкладке «Сеть». Выбор здесь не меняет IP Windows. 1000 Мбит/с требует гигабитного линка на обоих концах.", TextWrapping = TextWrapping.Wrap });
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var grid = new DataGrid { AutoGenerateColumns = false, ItemsSource = draft.Rows };
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Проверять", Binding = new Binding("Enabled") { Mode = BindingMode.TwoWay } });
        grid.Columns.Add(new DataGridTextColumn { Header = "Название пары", Width = new DataGridLength(160), Binding = new Binding("Name") { Mode = BindingMode.TwoWay } });
        foreach (var (header, property) in new[] { ("Карта A / IP", "FirstIp"), ("Карта B / IP", "SecondIp") })
        {
            grid.Columns.Add(new DataGridTemplateColumn { Header = header, Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                CellTemplate = new FuncDataTemplate<DataTestPairRow>((row, _) => {
                    var combo = new ComboBox { ItemsSource = draft.Addresses, HorizontalAlignment = HorizontalAlignment.Stretch,
                        ItemTemplate = new FuncDataTemplate<string>((ip, _) => new TextBlock { Text = draft.AddressLabel(ip!), TextWrapping = TextWrapping.Wrap }) };
                    combo.Bind(ComboBox.SelectedItemProperty, new Binding(property) { Mode = BindingMode.TwoWay });
                    return combo;
                }) });
        }
        grid.Columns.Add(new DataGridTemplateColumn { Header = "Мбит/с", Width = new DataGridLength(130),
            CellTemplate = new FuncDataTemplate<DataTestPairRow>((row, _) => {
                var speed = new ComboBox { ItemsSource = new[] { 10, 100, 1000, row!.Mbps }.Distinct().OrderBy(x => x).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
                speed.Bind(ComboBox.SelectedItemProperty, new Binding("Mbps") { Mode = BindingMode.TwoWay }); return speed;
            }) });
        var bottom = new StackPanel { Spacing = 10, Margin = new Thickness(0, 12, 0, 0) };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; bottom.Children.Add(error);
        var buttons = new WrapPanel();
        void Commit() { if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) throw new InvalidOperationException("Исправьте значение в таблице."); }
        void AddButton(string title, Action action)
        {
            var button = new Button { Content = title, Margin = new Thickness(0, 0, 8, 8) };
            button.Click += (_, _) => { try { Commit(); action(); error.Text = ""; } catch (Exception ex) { error.Text = ex.Message; } }; buttons.Children.Add(button);
        }
        AddButton("Добавить пару", () => { var i = 1; while (draft.Rows.Any(r => r.Name == $"Пара {i}")) i++; draft.Rows.Add(new DataTestPairRow { Name = $"Пара {i}" }); });
        AddButton("Удалить пару", () => { if (grid.SelectedItem is DataTestPairRow row) draft.Rows.Remove(row); });
        AddButton("Всем 100 Мбит/с", () => draft.SetAllSpeeds(100));
        AddButton("Всем 1000 Мбит/с", () => draft.SetAllSpeeds(1000));
        AddButton("Применить к ноде", () => { draft.Apply(node); Close(true); });
        var cancel = new Button { Content = "Отмена", Margin = new Thickness(0, 0, 8, 8) }; cancel.Click += (_, _) => Close(false); buttons.Children.Add(cancel);
        bottom.Children.Add(buttons); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom); root.Children.Add(grid); Content = root;
    }
}
