using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using TestBuilder.Services;

namespace TestBuilder.Views;
public sealed class LabelTemplatesWindow : Window
{
    public LabelTemplatesWindow(LabelTemplateDefinition? profileTemplate = null)
    {
        Title = "Шаблоны этикеток"; Width = 1040; Height = 800; MinWidth = 800; MinHeight = 600;
        var templates = new ObservableCollection<LabelTemplateDefinition>(AppSettings.Instance.LabelTemplates.Select(t => t.Copy()));
        if (profileTemplate != null)
        {
            var existing = templates.FirstOrDefault(t => t.Id == profileTemplate.Id);
            if (existing != null) templates.Remove(existing);
            templates.Add(profileTemplate.Copy());
        }
        var root = new DockPanel { Margin = new Thickness(20) };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var footer = new StackPanel { Spacing = 8 };
        footer.Children.Add(status);
        var save = new Button { Content = "Сохранить и выбрать шаблон" }; footer.Children.Add(save);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("260,16,*") };
        var left = new DockPanel(); var actions = new StackPanel { Spacing = 8 };
        var list = new ListBox { ItemsSource = templates };
        var add = new Button { Content = "Новый шаблон" };
        add.Click += (_, _) => { var t = new LabelTemplateDefinition(); templates.Add(t); list.SelectedItem = t; };
        var copy = new Button { Content = "Создать копию" };
        copy.Click += (_, _) => { if (list.SelectedItem is LabelTemplateDefinition t) { var c = t.Copy(); c.Id = Guid.NewGuid().ToString(); c.Name += " (копия)"; c.MatchServerSerial = false; templates.Add(c); list.SelectedItem = c; } };
        var remove = new Button { Content = "Удалить шаблон" };
        remove.Click += (_, _) => { if (list.SelectedItem is LabelTemplateDefinition t) { templates.Remove(t); list.SelectedItem = templates.FirstOrDefault(); } };
        actions.Children.Add(add); actions.Children.Add(copy); actions.Children.Add(remove); DockPanel.SetDock(actions, Dock.Bottom); left.Children.Add(actions); left.Children.Add(list);
        grid.Children.Add(left);
        var fields = new StackPanel { Spacing = 6 }; var inputs = new List<TextBox>();
        fields.Children.Add(new TextBlock { Text = "Пример этикетки", FontWeight = Avalonia.Media.FontWeight.SemiBold });
        var preview = new LabelPreviewControl { Height = 150, MaxWidth = 500 };
        var previewMessage = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        fields.Children.Add(preview); fields.Children.Add(previewMessage);
        void UpdatePreview()
        {
            preview.Preview = list.SelectedItem is LabelTemplateDefinition t ? LabelPreview.FromInput(t, "", "") : null;
            previewMessage.Text = preview.Preview?.Message;
        }
        var previewTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        previewTimer.Tick += (_, _) => { previewTimer.Stop(); UpdatePreview(); };
        void SchedulePreview() { previewTimer.Stop(); previewTimer.Start(); }
        Closed += (_, _) => previewTimer.Stop();
        foreach (var (label, property) in new[] { ("Название изделия / шаблона", "Name"), ("Язык (ZPL или EPL)", "Language"),
            ("Ширина, точки", "WidthDots"), ("Разрешение EPL, dpi (203 или 300)", "PrinterDpi"), ("Смещение полного серийника", "SerialOffset"), ("Максимум короткого номера", "ShortSerialMax"),
            ("Цифр короткого номера", "ShortSerialDigits"), ("Код изделия для отчёта", "DeviceCode"), ("Префикс штрихкода", "BarcodePrefix") })
        {
            fields.Children.Add(new TextBlock { Text = label });
            var box = new TextBox(); box.Bind(TextBox.TextProperty, new Binding(property) { Mode = BindingMode.TwoWay }); fields.Children.Add(box); inputs.Add(box);
            box.TextChanged += (_, _) => SchedulePreview();
        }
        foreach (var (label, property) in new[] { ("Использовать полный и короткий серийный номер", "UseShortSerial"),
            ("Требовать MAC", "RequiresMac"), ("Определять шаблон по диапазону серийника с сервера", "MatchServerSerial") })
        {
            var box = new CheckBox { Content = label }; box.Bind(CheckBox.IsCheckedProperty, new Binding(property) { Mode = BindingMode.TwoWay }); fields.Children.Add(box);
            box.PropertyChanged += (_, e) => { if (e.Property == CheckBox.IsCheckedProperty) SchedulePreview(); };
        }
        fields.Children.Add(new TextBlock { Text = "Команда принтера. Подстановки: {{Name}}, {{Serial}}, {{ShortSerial}}, {{Mac}}, {{Barcode}}, {{WidthDots}}, {{BarcodeX}}, {{TextX}}, {{BarcodeType}}.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var command = new TextBox { AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 180 };
        command.Bind(TextBox.TextProperty, new Binding("Command") { Mode = BindingMode.TwoWay }); fields.Children.Add(command);
        command.TextChanged += (_, _) => SchedulePreview();
        list.SelectionChanged += (_, _) => { fields.DataContext = list.SelectedItem; UpdatePreview(); };
        var scroll = new ScrollViewer { Content = fields }; Grid.SetColumn(scroll, 2); grid.Children.Add(scroll);
        root.Children.Add(grid); Content = root;
        list.SelectedItem = templates.FirstOrDefault(t => t.Id == profileTemplate?.Id) ?? templates.FirstOrDefault();
        save.Click += (_, _) =>
        {
            try
            {
                if (inputs.Any(DataValidationErrors.GetHasErrors)) throw new InvalidOperationException("Исправьте значения шаблона.");
                LabelTemplateCatalog.Validate(templates);
                var selected = list.SelectedItem as LabelTemplateDefinition ?? throw new InvalidOperationException("Выберите шаблон.");
                var old = AppSettings.Instance.LabelTemplates;
                AppSettings.Instance.LabelTemplates = templates.Select(t => t.Copy()).ToList();
                try { AppSettings.Instance.SaveChecked(); } catch { AppSettings.Instance.LabelTemplates = old; throw; }
                Close(selected.Copy());
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
    }
}
