using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;
public sealed class NetworkPlanWindow : Window
{
    public NetworkPlanWindow() : this(AppSettings.Instance.NetworkPlan, SaveStandalone) { }

    internal NetworkPlanWindow(BenchNetworkPlan source, Action<BenchNetworkPlan> savePlan)
    {
        Title = "План сети стенда"; Width = 1200; MinWidth = 900; Height = 600;
        var plan = TestViewModel.CopyDefinition(source);
        var addresses = new ObservableCollection<BenchNetworkAddress>(plan.Addresses);
        var root = new DockPanel { Margin = new Thickness(20) };
        var top = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(new TextBlock { Text = "Маска IPv4" });
        var mask = new TextBox { Name = "NetworkPlanMask", Text = plan.Mask }; top.Children.Add(mask);
        top.Children.Add(new TextBlock { Text = "«Добавить карту» подставляет адреса, которых ещё нет в плане. Проверьте основной и временный адреса перед сохранением. Затем выберите физическую карту в разделе «Сеть»; IP Windows меняются кнопкой «Применить адреса».", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        top.Children.Add(new TextBlock { Text = "Задайте название, разъём и пару для каждого порта. Для DataTest отметьте по два порта с одинаковым именем пары и скоростью. Назначение разъёма подтверждается кабелем, а не IP или скоростью линка.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var grid = new DataGrid { Name = "NetworkPlanAddresses", AutoGenerateColumns = false, ItemsSource = addresses };
        foreach (var (title, path) in new[] { ("Название порта", "Name"), ("Назначение / разъём", "Role"), ("Пара DataTest", "Pair"), ("Мбит/с теста", "TestMbps"), ("Адрес карты", "Ip"), ("Временный адрес", "TemporaryIp") })
            grid.Columns.Add(new DataGridTextColumn { Header = new TextBlock { Text = title, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                Width = new DataGridLength(1, DataGridLengthUnitType.Star), Binding = new Binding(path) { Mode = BindingMode.TwoWay } });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "DataTest", Binding = new Binding("IncludeInDataTest") { Mode = BindingMode.TwoWay } });
        var bottom = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        var status = new TextBlock { Name = "NetworkPlanError", TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
        status.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("DangerTextBrush"));
        var errorBox = new Border { Padding = new Thickness(10), Child = status, IsVisible = false };
        errorBox.Bind(Border.BackgroundProperty, new DynamicResourceExtension("DangerSoftBrush"));
        bottom.Children.Add(errorBox);
        var buttons = new WrapPanel();
        var summary = new TextBlock { Name = "NetworkPlanCount" };
        void UpdateCount() => summary.Text = $"Адресов в плане: {addresses.Count}";
        addresses.CollectionChanged += (_, _) => UpdateCount(); UpdateCount(); bottom.Children.Add(summary);
        void Commit()
        {
            if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true))
                throw new InvalidOperationException($"Строка {grid.SelectedIndex + 1}, поле «{(grid.CurrentColumn?.Header as TextBlock)?.Text ?? grid.CurrentColumn?.Header?.ToString()}»: исправьте введённое значение.");
        }
        void Button(string title, Action action, bool commit = true)
        {
            var b = new Button { Content = title, Margin = new Thickness(0, 0, 8, 0) };
            b.Click += (_, _) =>
            {
                try { if (commit) Commit(); action(); status.Text = ""; errorBox.IsVisible = false; }
                catch (Exception ex) { status.Text = "Изменения не сохранены. " + ex.Message; errorBox.IsVisible = true; }
            };
            buttons.Children.Add(b);
        }
        Button("Добавить карту", () =>
        {
            plan.Mask = mask.Text?.Trim() ?? ""; plan.Addresses = addresses.ToList();
            var row = plan.CreateNextAddress(); addresses.Add(row);
            grid.SelectedItem = row; grid.ScrollIntoView(row, grid.Columns[4]);
        });
        var bulk = new Button { Content = "Добавить порты…", Margin = new Thickness(0, 0, 8, 0) };
        bulk.Click += async (_, _) =>
        {
            try
            {
                Commit(); plan.Mask = mask.Text?.Trim() ?? ""; plan.Addresses = addresses.ToList();
                var result = await new NetworkPortBatchWindow(TestViewModel.CopyDefinition(plan)).ShowDialog<BenchNetworkPlan?>(this);
                if (result == null) return;
                addresses.Clear(); foreach (var row in result.Addresses) addresses.Add(row);
                var last = addresses.LastOrDefault(); if (last != null) { grid.SelectedItem = last; grid.ScrollIntoView(last, grid.Columns[4]); }
                status.Text = ""; errorBox.IsVisible = false;
            }
            catch (Exception ex) { status.Text = "Изменения не сохранены. " + ex.Message; errorBox.IsVisible = true; }
        };
        buttons.Children.Add(bulk);
        Button("Удалить строку", () =>
        {
            grid.CancelEdit(DataGridEditingUnit.Cell); grid.CancelEdit(DataGridEditingUnit.Row);
            if (grid.SelectedItem is BenchNetworkAddress row) addresses.Remove(row);
        }, commit: false);
        Button("Всем 100 Мбит/с", () => { foreach (var row in addresses) row.TestMbps = 100; });
        Button("Всем 1000 Мбит/с", () => { foreach (var row in addresses) row.TestMbps = 1000; });
        Button("Сохранить план", () =>
        {
            plan.Mask = mask.Text?.Trim() ?? ""; plan.Addresses = addresses.ToList();
            foreach (var row in addresses)
            {
                row.Name = row.Name?.Trim() ?? ""; row.Role = row.Role?.Trim() ?? ""; row.Pair = row.Pair?.Trim() ?? "";
                row.Ip = row.Ip?.Trim() ?? ""; row.TemporaryIp = row.TemporaryIp?.Trim() ?? "";
            }
            plan.Validate();
            savePlan(TestViewModel.CopyDefinition(plan));
            Close(true);
        });
        Button("Отмена", () => Close(false), commit: false);
        bottom.Children.Add(buttons); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        root.Children.Add(grid); Content = root;
    }

    private static void SaveStandalone(BenchNetworkPlan plan)
    {
        var settings = AppSettings.Instance; var previous = settings.NetworkPlan;
        settings.NetworkPlan = plan;
        try { settings.SaveChecked(); }
        catch { settings.NetworkPlan = previous; throw; }
    }
}
