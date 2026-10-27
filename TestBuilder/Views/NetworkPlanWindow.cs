using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;
public sealed class NetworkPlanWindow : Window
{
    public NetworkPlanWindow()
    {
        Title = "План сети стенда"; Width = 760; Height = 600;
        var plan = TestViewModel.CopyDefinition(AppSettings.Instance.NetworkPlan);
        var addresses = new ObservableCollection<BenchNetworkAddress>(plan.Addresses);
        var root = new DockPanel { Margin = new Thickness(20) };
        var top = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(new TextBlock { Text = "Маска IPv4" });
        var mask = new TextBox { Text = plan.Mask }; top.Children.Add(mask);
        top.Children.Add(new TextBlock { Text = "Временные адреса используются при обмене адресами между картами. Они должны быть свободны и отличаться от основных.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var grid = new DataGrid { AutoGenerateColumns = false, ItemsSource = addresses };
        foreach (var (title, path) in new[] { ("Адрес карты", "Ip"), ("Временный адрес", "TemporaryIp") })
            grid.Columns.Add(new DataGridTextColumn { Header = title, Width = new DataGridLength(1, DataGridLengthUnitType.Star), Binding = new Binding(path) { Mode = BindingMode.TwoWay } });
        var bottom = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; bottom.Children.Add(status);
        var buttons = new WrapPanel();
        void Button(string title, Action action)
        {
            var b = new Button { Content = title, Margin = new Thickness(0, 0, 8, 0) };
            b.Click += (_, _) => { try { action(); } catch (Exception ex) { status.Text = ex.Message; } }; buttons.Children.Add(b);
        }
        Button("Добавить карту", () => addresses.Add(new()));
        Button("Удалить строку", () => { if (grid.SelectedItem is BenchNetworkAddress row) addresses.Remove(row); });
        Button("Сохранить план", () =>
        {
            if (!grid.CommitEdit()) throw new InvalidOperationException("Исправьте значения адресов.");
            plan.Mask = mask.Text?.Trim() ?? ""; plan.Addresses = addresses.ToList(); plan.Validate();
            var old = AppSettings.Instance.NetworkPlan; AppSettings.Instance.NetworkPlan = plan;
            try { AppSettings.Instance.SaveChecked(); } catch { AppSettings.Instance.NetworkPlan = old; throw; }
            Close(true);
        });
        bottom.Children.Add(buttons); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        root.Children.Add(grid); Content = root;
    }
}
