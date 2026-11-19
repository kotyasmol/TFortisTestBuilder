using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TestBuilder.Services;

namespace TestBuilder.Views;

public sealed class NetworkPortBatchWindow : Window
{
    internal NetworkPortBatchWindow(BenchNetworkPlan source)
    {
        Title = "Добавить порты в план"; Width = 620; Height = 630; MinWidth = 540; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(20) };
        var form = new StackPanel { Spacing = 10 };
        var suggested = source.CreateNextAddress();
        var count = new NumericUpDown { Name = "BatchCount", Minimum = 1, Maximum = NetworkPortBatch.MaximumBatchSize, Increment = 1, FormatString = "0", Value = 24 };
        var firstNumber = new NumericUpDown { Name = "BatchFirstNumber", Minimum = 1, Maximum = int.MaxValue, Increment = 1, FormatString = "0", Value = source.Addresses.Count + 1 };
        var prefix = new TextBox { Name = "BatchPrefix", Text = "Порт" };
        var role = new TextBox { Name = "BatchRole", Text = "Медь", Watermark = "Например, Медь или SFP" };
        var speed = new TextBox { Name = "BatchSpeed", Text = "100", Watermark = "1–1000", HorizontalAlignment = HorizontalAlignment.Stretch };
        var ip = new TextBox { Name = "BatchFirstIp", Text = suggested.Ip };
        var temporary = new TextBox { Name = "BatchFirstTemporaryIp", Text = suggested.TemporaryIp };
        var pairs = new CheckBox { Name = "BatchCreatePairs", Content = "Объединить соседние порты в пары DataTest", IsChecked = true };
        var summary = new TextBlock { Name = "BatchSummary", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
        form.Children.Add(new TextBlock { Text = "Порты добавятся к текущему плану. Существующие строки сохранятся; IP Windows здесь не меняются.", TextWrapping = TextWrapping.Wrap });
        void Field(string label, Control input)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("210,*") };
            row.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
            Grid.SetColumn(input, 1); row.Children.Add(input); form.Children.Add(row);
        }
        Field("Добавить портов", count);
        var sizes = new WrapPanel();
        foreach (var size in new[] { 12, 24, 48 })
        {
            var button = new Button { Content = $"{size} портов", Margin = new Thickness(0, 0, 8, 0) };
            button.Click += (_, _) => count.Value = size; sizes.Children.Add(button);
        }
        form.Children.Add(sizes);
        Field("Начальный номер", firstNumber); Field("Название портов", prefix); Field("Назначение / разъём", role);
        Field("Мбит/с теста", speed); Field("Первый адрес карты", ip); Field("Первый временный адрес", temporary);
        form.Children.Add(pairs);
        var footer = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) }; footer.Children.Add(summary);
        void UpdateSummary() => summary.Text = $"Добавится {count.Value:0} портов. Всего в плане: {source.Addresses.Count + (count.Value ?? 0):0}." +
            (pairs.IsChecked == true ? count.Value % 2 == 0 ? $" Новых пар: {(count.Value ?? 0) / 2:0}." : " Для пар нужно чётное число портов." : " Пары можно настроить позже.");
        count.ValueChanged += (_, _) => UpdateSummary(); pairs.IsCheckedChanged += (_, _) => UpdateSummary(); UpdateSummary();
        var error = new TextBlock { Name = "BatchError", TextWrapping = TextWrapping.Wrap };
        error.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("DangerTextBrush")); footer.Children.Add(error);
        var buttons = new WrapPanel();
        var add = new Button { Content = "Добавить в план", Margin = new Thickness(0, 0, 8, 0) };
        add.Click += (_, _) =>
        {
            try
            {
                if (count.Value != decimal.Truncate(count.Value ?? 0) || firstNumber.Value != decimal.Truncate(firstNumber.Value ?? 0))
                    throw new InvalidOperationException("Количество и начальный номер должны быть целыми числами.");
                var batch = new NetworkPortBatch(checked((int)(count.Value ?? 0)), checked((int)(firstNumber.Value ?? 0)),
                    prefix.Text ?? "", role.Text ?? "", DataTestSpeed.Parse(speed.Text), ip.Text ?? "", temporary.Text ?? "", pairs.IsChecked == true);
                Close(batch.AppendTo(source));
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        var cancel = new Button { Content = "Отмена" }; cancel.Click += (_, _) => Close(null);
        buttons.Children.Add(add); buttons.Children.Add(cancel); footer.Children.Add(buttons);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = form, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Content = root;
    }
}
