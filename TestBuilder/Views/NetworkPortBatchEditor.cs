using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

internal sealed class NetworkPortBatchEditor : UserControl
{
    public event Action? Cancelled;

    public NetworkPortBatchEditor(BenchNetworkPlan source, Action<BenchNetworkPlan> apply, bool pairCount = false,
        double maxFormHeight = double.PositiveInfinity)
    {
        var root = new DockPanel();
        var form = new StackPanel { Spacing = 10 };
        var suggested = source.CreateNextAddress();
        var count = new NumericUpDown { Name = "BatchCount", Minimum = 1, Maximum = pairCount ? NetworkPortBatch.MaximumBatchSize / 2 : NetworkPortBatch.MaximumBatchSize,
            Increment = 1, FormatString = "0", Value = pairCount ? 1 : 24 };
        var firstNumber = new NumericUpDown { Name = "BatchFirstNumber", Minimum = 1, Maximum = int.MaxValue, Increment = 1, FormatString = "0", Value = source.Addresses.Count + 1 };
        var prefix = new TextBox { Name = "BatchPrefix", Text = "Порт" };
        var role = new TextBox { Name = "BatchRole", Text = "Медь" };
        var speed = new TextBox { Name = "BatchSpeed", Text = "100", Watermark = "1–1000" };
        var ip = new TextBox { Name = "BatchFirstIp", Text = suggested.Ip };
        var temporary = new TextBox { Name = "BatchFirstTemporaryIp", Text = suggested.TemporaryIp };
        var mask = new TextBox { Name = "BatchMask", Text = source.Mask };
        var pairs = new CheckBox { Name = "BatchCreatePairs", Content = "Объединить соседние порты в пары DataTest", IsChecked = true, IsVisible = !pairCount };
        form.Children.Add(new TextBlock { Text = pairCount ? "Добавить пары DataTest" : "Добавить порты", FontSize = 18, FontWeight = FontWeight.SemiBold });
        form.Children.Add(new TextBlock { Text = pairCount ? "Для каждой пары создаются два порта с одинаковой скоростью. После добавления выберите для них сетевые карты." :
            "Новые порты дополнят текущий список. Проверьте адреса и назначьте сетевые карты после сохранения.", TextWrapping = TextWrapping.Wrap });
        var basic = new Grid { ColumnDefinitions = new("*,16,*") };
        basic.Children.Add(Field(pairCount ? "Количество пар" : "Добавить портов", count));
        var speedField = Field("Мбит/с теста", speed); Grid.SetColumn(speedField, 2); basic.Children.Add(speedField); form.Children.Add(basic);
        if (!pairCount)
        {
            var sizes = new WrapPanel();
            foreach (var size in new[] { 12, 24, 48 })
            {
                var button = new Button { Content = $"{size} портов", Margin = new Thickness(0, 0, 8, 0) };
                button.Click += (_, _) => count.Value = size; sizes.Children.Add(button);
            }
            form.Children.Add(sizes);
        }
        var details = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        details.Children.Add(Field("Начальный номер порта", firstNumber)); details.Children.Add(Field("Название портов", prefix));
        details.Children.Add(Field("Назначение / разъём", role)); details.Children.Add(Field("Маска IPv4", mask));
        details.Children.Add(Field("Первый адрес карты", ip)); details.Children.Add(Field("Первый временный адрес", temporary));
        var advanced = new Expander { Name = "BatchAddressSettings", Header = "Названия и адреса", IsExpanded = !pairCount,
            HorizontalAlignment = HorizontalAlignment.Stretch, Content = details };
        form.Children.Add(advanced); form.Children.Add(pairs);
        var summary = new TextBlock { Name = "BatchSummary", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
        var error = new TextBlock { Name = "BatchError", TextWrapping = TextWrapping.Wrap };
        error.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("DangerTextBrush"));
        var footer = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(summary); footer.Children.Add(error);
        void UpdateSummary()
        {
            var number = count.Value ?? 0;
            summary.Text = pairCount ? $"Добавится пар: {number:0}, портов: {number * 2:0}. Всего портов: {source.Addresses.Count + number * 2:0}." :
                $"Добавится {number:0} портов. Всего в плане: {source.Addresses.Count + number:0}." +
                (pairs.IsChecked == true ? number % 2 == 0 ? $" Новых пар: {number / 2:0}." : " Для пар нужно чётное число портов." : " Пары можно настроить позже.");
        }
        count.ValueChanged += (_, _) => UpdateSummary(); pairs.IsCheckedChanged += (_, _) => UpdateSummary(); UpdateSummary();
        var actions = new WrapPanel();
        var add = new Button { Name = "ConfirmNetworkBatch", Content = pairCount ? "Добавить пары" : "Добавить в план", Margin = new Thickness(0, 0, 8, 0), Classes = { "primary" } };
        add.Click += (_, _) =>
        {
            try
            {
                if (DataValidationErrors.GetHasErrors(count) || DataValidationErrors.GetHasErrors(firstNumber) ||
                    count.Value != decimal.Truncate(count.Value ?? 0) || firstNumber.Value != decimal.Truncate(firstNumber.Value ?? 0))
                    throw new InvalidOperationException("Количество и начальный номер должны быть целыми числами.");
                var ports = checked((int)(count.Value ?? 0) * (pairCount ? 2 : 1));
                var draft = TestViewModel.CopyDefinition(source); draft.Mask = mask.Text?.Trim() ?? "";
                var batch = new NetworkPortBatch(ports, checked((int)(firstNumber.Value ?? 0)), prefix.Text ?? "", role.Text ?? "",
                    DataTestSpeed.Parse(speed.Text), ip.Text ?? "", temporary.Text ?? "", pairCount || pairs.IsChecked == true);
                apply(batch.AppendTo(draft));
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        var cancel = new Button { Name = "CancelNetworkBatch", Content = "Отмена" }; cancel.Click += (_, _) => Cancelled?.Invoke();
        actions.Children.Add(add); actions.Children.Add(cancel); footer.Children.Add(actions);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = form, MaxHeight = maxFormHeight,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Content = root;
    }
    private static Control Field(string label, Control input)
    {
        AutomationProperties.SetName(input, label);
        var field = new StackPanel { Spacing = 4 }; field.Children.Add(new TextBlock { Text = label }); field.Children.Add(input); return field;
    }
}
