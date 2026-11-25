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
using TestBuilder.Services;

namespace TestBuilder.Views;

public sealed class DataTestPairsView : WorkspacePage
{
    public DataTestPairsView(RunDataTestNodeViewModel node)
    {
        Title = "Пары DataTest";
        var draft = new DataTestPairEditorViewModel(node);
        var root = new DockPanel { Margin = new Thickness(20) };
        var top = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(new TextBlock { Text = "Каждая строка — проверка между двумя картами стенда. Выберите адреса и скорость каждой пары. Снятая галочка исключает пару из проверки. При одном направлении трафик идёт B → A.", TextWrapping = TextWrapping.Wrap });
        top.Children.Add(new TextBlock { Text = "Скорость — целое число от 1 до 1000 Мбит/с. Выше 100 Мбит/с нужен гигабитный линк на обоих концах. Адреса карт настраиваются во вкладке «Сеть»; IP Windows здесь не меняются.", TextWrapping = TextWrapping.Wrap });
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var grid = new DataGrid { Name = "DataTestPairsGrid", AutoGenerateColumns = false, ItemsSource = draft.Rows };
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
                var speed = new TextBox { Name = "PairSpeed", Watermark = "1–1000", HorizontalAlignment = HorizontalAlignment.Stretch };
                speed.Bind(TextBox.TextProperty, new Binding("MbpsText") { Mode = BindingMode.TwoWay }); return speed;
            }) });
        var bottom = new StackPanel { Spacing = 10, Margin = new Thickness(0, 12, 0, 0) };
        var error = new TextBlock { Name = "DataTestPairsError", TextWrapping = TextWrapping.Wrap }; bottom.Children.Add(error);
        var buttons = new WrapPanel();
        void Commit() { if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) throw new InvalidOperationException("Исправьте значение в таблице."); }
        void AddButton(string title, Action action)
        {
            var button = new Button { Content = title, Margin = new Thickness(0, 0, 8, 8) };
            button.Click += (_, _) => { try { Commit(); action(); error.Text = ""; } catch (Exception ex) { error.Text = ex.Message; } }; buttons.Children.Add(button);
        }
        var allSpeed = new TextBox { Name = "AllPairsSpeed", Text = "100", Width = 100, Watermark = "1–1000" };
        var applySpeed = new Button { Content = "Применить всем", Margin = new Thickness(8, 0, 0, 0) };
        applySpeed.Click += (_, _) => { try { Commit(); draft.SetAllSpeeds(DataTestSpeed.Parse(allSpeed.Text)); error.Text = ""; } catch (Exception ex) { error.Text = ex.Message; } };
        var speedRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        speedRow.Children.Add(new TextBlock { Text = "Всем парам, Мбит/с", VerticalAlignment = VerticalAlignment.Center });
        speedRow.Children.Add(allSpeed); speedRow.Children.Add(applySpeed); bottom.Children.Add(speedRow);
        AddButton("Добавить пару", () => { var i = 1; while (draft.Rows.Any(r => r.Name == $"Пара {i}")) i++; draft.Rows.Add(new DataTestPairRow { Name = $"Пара {i}" }); });
        AddButton("Удалить пару", () => { if (grid.SelectedItem is DataTestPairRow row) draft.Rows.Remove(row); });
        AddButton("Всем 100 Мбит/с", () => draft.SetAllSpeeds(100));
        AddButton("Всем 1000 Мбит/с", () => draft.SetAllSpeeds(1000));
        AddButton("Применить к ноде", () => { draft.Apply(node); Close(true); });
        var cancel = new Button { Content = "Отмена", Margin = new Thickness(0, 0, 8, 8) }; cancel.Click += (_, _) => Close(false); buttons.Children.Add(cancel);
        bottom.Children.Add(buttons); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom); root.Children.Add(grid); Content = root;
    }
}
