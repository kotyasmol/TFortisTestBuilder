using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Services;

namespace TestBuilder.Views;

/// <summary>Preserves commands which the visual parser cannot represent.</summary>
internal sealed class LabelRawTemplateEditorView : UserControl
{
    public LabelTemplateDefinition Template { get; }
    public LabelRawTemplateEditorView(LabelTemplateDefinition template)
    {
        Template = template.Copy(); DataContext = Template;
        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(new TextBlock { Text = "Команда содержит элементы, которые не поддерживает визуальный редактор. Можно изменить команду здесь или выбрать другой шаблон.", TextWrapping = TextWrapping.Wrap });
        var command = new TextBox { Name = "ProfileLabelRawCommand", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 130 };
        command.Bind(TextBox.TextProperty, new Binding("Command", BindingMode.TwoWay)); root.Children.Add(command);
        var fields = new LabelFieldGrid { MaxColumns = 4, MinColumnWidth = 125 };
        foreach (var (title, property) in new[] { ("Язык ZPL/EPL", "Language"), ("Разрешение, dpi", "PrinterDpi"), ("Ширина, точки", "WidthDots"), ("Высота, точки", "HeightDots"),
            ("Смещение серии", "SerialOffset"), ("Максимум короткого SN", "ShortSerialMax"), ("Цифр короткого SN", "ShortSerialDigits"), ("Код изделия", "DeviceCode") })
        { var text = new TextBox(); text.Bind(TextBox.TextProperty, new Binding(property, BindingMode.TwoWay)); fields.Children.Add(LabelTemplateEditorView.Field(title, text)); }
        var advanced = new StackPanel { Spacing = 6 }; advanced.Children.Add(fields);
        foreach (var (title, property) in new[] { ("Использовать короткий SN", "UseShortSerial"), ("Требовать MAC", "RequiresMac") })
        { var check = new CheckBox { Content = title }; check.Bind(CheckBox.IsCheckedProperty, new Binding(property, BindingMode.TwoWay)); advanced.Children.Add(check); }
        root.Children.Add(new Expander { Header = "Параметры команды", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch });
        var data = new LabelDataSettingsView { DataContext = Template }; root.Children.Add(data);
        var preview = new LabelPreviewControl { Height = 180 }; root.Children.Add(preview);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        void Refresh() => preview.Preview = LabelPreview.FromInput(Template, "", "");
        void Schedule() { timer.Stop(); timer.Start(); }
        timer.Tick += (_, _) => { timer.Stop(); Refresh(); };
        command.TextChanged += (_, _) => Schedule(); data.Changed += Schedule;
        DetachedFromVisualTree += (_, _) => timer.Stop(); Refresh();
        Content = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }
    public LabelTemplateDefinition Apply()
    {
        if (this.GetVisualDescendants().OfType<Control>().Any(DataValidationErrors.GetHasErrors)) throw new InvalidOperationException("Исправьте параметры команды.");
        Template.CommitDataFormats(); Template.Validate(); return Template.Copy();
    }
}
