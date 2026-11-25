using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

/// <summary>The same inline editor is used by shared types and profile template copies.</summary>
public sealed class LabelTemplateEditorView : UserControl
{
    public LabelLayoutEditorViewModel Editor { get; }
    public TabControl Tabs { get; } = new() { Name = "LabelTypeEditorTabs", Padding = new Thickness(0) };
    public ContentControl PrintControls { get; } = new();
    private readonly List<Control> _inputs = new();
    private readonly Grid _columns = new() { ColumnDefinitions = new("*,14,1.3*") };
    private readonly ScrollViewer _previewScroll;
    public LabelTemplateEditorView(LabelLayoutEditorViewModel editor, IReadOnlyList<LabelFieldSourceViewModel>? sources = null)
    {
        Editor = editor; DataContext = editor; Name = "UnifiedLabelEditor";
        var preview = new StackPanel { Spacing = 8 };
        preview.Children.Add(new TextBlock { Text = "Образец этикетки", FontWeight = FontWeight.SemiBold });
        preview.Children.Add(new LabelLayoutCanvas(editor) { Height = 220, MinHeight = 160 });
        var elements = new ComboBox { Name = "LabelTypeElements", ItemsSource = editor.Elements, HorizontalAlignment = HorizontalAlignment.Stretch };
        elements.Bind(ComboBox.SelectedItemProperty, new Binding("Selected", BindingMode.TwoWay));
        preview.Children.Add(Field("Элемент", elements));
        var actions = new WrapPanel();
        foreach (var kind in LabelLayoutEditorViewModel.Kinds)
            actions.Children.Add(ActionButton("+ " + kind.Title.Replace(" Code 128", ""), () => editor.Add(kind.Key)));
        actions.Children.Add(ActionButton("Копировать", editor.CopySelected)); actions.Children.Add(ActionButton("Удалить", editor.Remove)); actions.Children.Add(ActionButton("По центру", editor.Center));
        preview.Children.Add(actions);
        var drag = Check("Перемещать и растягивать мышью", "AllowCanvasEditing"); drag.Name = "EnableLabelDragging"; preview.Children.Add(drag);
        var result = new SelectableTextBlock { Name = "LabelBarcodeSummary", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        void Summary() => result.Text = editor.Preview?.Elements.FirstOrDefault(e => e.Bars.Length > 0) is { } bar ? "Штрихкод: " + bar.EncodedData : editor.Preview?.Message;
        editor.Changed += Summary; Summary();
        DetachedFromVisualTree += (_, _) => editor.Changed -= Summary;
        preview.Children.Add(result); preview.Children.Add(PrintControls);
        var export = new Button { Content = "Экспорт PNG…", Name = "ExportInlineLabelPreview", Padding = new Thickness(8, 5) };
        var exportStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
        export.Click += async (_, _) =>
        {
            try
            {
                if (HasInputErrors()) throw new InvalidOperationException("Исправьте поля этикетки.");
                editor.Apply();
                if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
                var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Предпросмотр этикетки", SuggestedFileName = "label-preview.png", DefaultExtension = "png" });
                if (file == null) return;
                var paper = new LabelPreviewControl { Preview = editor.Preview, Width = editor.Template.WidthDots + 4, Height = (editor.Template.HeightDots ?? 150) + 4 };
                paper.Measure(Size.Infinity); paper.Arrange(new Rect(paper.DesiredSize));
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)paper.Width, (int)paper.Height)); bitmap.Render(paper);
                await using var stream = await file.OpenWriteAsync(); stream.SetLength(0); bitmap.Save(stream);
                exportStatus.Text = "PNG сохранён: один пиксель на точку принтера. Шрифты приблизительные.";
            }
            catch (Exception ex) when (LabelTypesViewModel.IsCatalogError(ex)) { exportStatus.Text = ex.Message; }
        };
        preview.Children.Add(export); preview.Children.Add(exportStatus);
        _previewScroll = Scroll(preview); _columns.Children.Add(_previewScroll);
        var data = new LabelDataSettingsView { DataContext = editor.Template };
        data.Changed += editor.Refresh;
        Tabs.ItemsSource = new[] {
            new TabItem { Header = "Макет", Content = Scroll(LayoutFields()) },
            new TabItem { Header = "Источники данных", Content = Scroll(SourceFields(sources ?? CreateSources(editor))) },
            new TabItem { Header = "SN и штрихкод", Content = Scroll(data) },
            new TabItem { Header = "Проба и проверка", Content = Scroll(SampleFields()) }
        };
        Grid.SetColumn(Tabs, 2); _columns.Children.Add(Tabs); Content = _columns;
        SizeChanged += (_, _) =>
        {
            var stacked = Bounds.Width < 620;
            _columns.ColumnDefinitions = new(stacked ? "*" : "*,14,1.3*");
            _columns.RowDefinitions = new(stacked ? "220,10,*" : "*");
            Grid.SetColumn(Tabs, stacked ? 0 : 2); Grid.SetRow(Tabs, stacked ? 2 : 0);
        };
    }

    private Control LayoutFields()
    {
        var content = new StackPanel { Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };
        var quick = new LabelFieldGrid { Name = "LabelQuickFields", MaxColumns = 3, MinColumnWidth = 125 };
        quick.Children.Add(Field("Отступ всего макета сверху, мм", Number("TopMargin", "LabelTopMargin", 0, 200, .25m)));
        var height = Field("Высота штрихкода, мм", Number("BarcodeHeight", "LabelBarcodeHeight", .1m, 200, .5m)); height.Bind(IsEnabledProperty, new Binding("HasBarcode")); quick.Children.Add(height);
        var module = Field("Ширина полосы, точек", Number("BarcodeModule", "LabelBarcodeModule", 1, 10, 1, "0")); module.Bind(IsEnabledProperty, new Binding("HasBarcode")); quick.Children.Add(module);
        content.Children.Add(quick);
        var fields = new StackPanel { Spacing = 8 }; fields.Bind(IsEnabledProperty, new Binding("HasSelection"));
        var heading = new TextBlock { FontWeight = FontWeight.SemiBold }; heading.Bind(TextBlock.TextProperty, new Binding("ElementName")); fields.Children.Add(heading);
        var contentPicker = new ComboBox { Name = "LabelContentSource", ItemsSource = LabelLayoutEditorViewModel.ContentFields, HorizontalAlignment = HorizontalAlignment.Stretch };
        contentPicker.Bind(ComboBox.SelectedItemProperty, new Binding("ContentField", BindingMode.TwoWay));
        var sourceField = Field("Что печатать в элементе", contentPicker); sourceField.Bind(IsVisibleProperty, new Binding("HasValue")); fields.Children.Add(sourceField);
        var text = Text("Value", "LabelElementValue"); var textField = Field("Текст / подстановка", text); textField.Bind(IsVisibleProperty, new Binding("IsCustomContent")); fields.Children.Add(textField);
        var position = new LabelFieldGrid { Name = "LabelElementGeometry", MinColumnWidth = 90 };
        foreach (var (title, property) in new[] { ("X, мм", "X"), ("Y, мм", "Y"), ("Ширина, мм", "Width"), ("Высота, мм", "Height") })
            position.Children.Add(Field(title, Number(property, "Layout" + property, property is "X" or "Y" ? 0 : .08m, 2500, .1m)));
        fields.Children.Add(position);
        var options = new LabelFieldGrid { MaxColumns = 3, MinColumnWidth = 115 };
        options.Children.Add(Conditional(Field("Ширина шрифта, мм", Number("FontWidth", "LayoutFontWidth", .08m, 85, .1m)), "HasTextOptions"));
        var align = new ComboBox { ItemsSource = LabelLayoutEditorViewModel.Alignments, HorizontalAlignment = HorizontalAlignment.Stretch };
        align.Bind(ComboBox.SelectedItemProperty, new Binding("SelectedAlignment", BindingMode.TwoWay)); options.Children.Add(Conditional(Field("Выравнивание", align), "HasTextOptions"));
        options.Children.Add(Conditional(Field("Толщина рамки, точек", Number("Stroke", "LayoutStroke", 1, 100, 1, "0")), "HasBoxOptions"));
        options.Children.Add(Conditional(Field("Узкая полоса, точек", Number("Module", "LayoutModule", 1, 10, 1, "0")), "HasBarcodeOptions"));
        fields.Children.Add(options);
        var flags = new WrapPanel();
        flags.Children.Add(Check("Печатать элемент", "Visible")); flags.Children.Add(Conditional(Check("Подгонять текст", "FitText"), "HasTextOptions")); fields.Children.Add(flags);
        content.Children.Add(fields);
        var advanced = new LabelFieldGrid { MaxColumns = 3, MinColumnWidth = 130 };
        advanced.Children.Add(Field("Название элемента", Text("ElementName", "LayoutElementName")));
        var kind = new ComboBox { ItemsSource = LabelLayoutEditorViewModel.Kinds, HorizontalAlignment = HorizontalAlignment.Stretch }; kind.Bind(ComboBox.SelectedItemProperty, new Binding("Kind", BindingMode.TwoWay)); advanced.Children.Add(Field("Тип элемента", kind));
        var order = new WrapPanel();
        void Move(int delta) { if (Editor.Selected == null) return; var index = Editor.Elements.IndexOf(Editor.Selected); Editor.Elements.Move(index, Math.Clamp(index + delta, 0, Editor.Elements.Count - 1)); Editor.Refresh(); }
        order.Children.Add(ActionButton("Выше", () => Move(-1))); order.Children.Add(ActionButton("Ниже", () => Move(1)));
        advanced.Children.Add(Field("Порядок печати", order));
        content.Children.Add(new Expander { Header = "Параметры элемента", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch });
        var paper = new LabelFieldGrid { Name = "LabelPaperFields", MaxColumns = 3, MinColumnWidth = 125 };
        paper.Children.Add(Field("Ширина этикетки, мм", Number("PaperWidth", "LabelPaperWidth", 1, 800, .5m)));
        paper.Children.Add(Field("Высота этикетки, мм", Number("PaperHeight", "LabelPaperHeight", 1, 2000, .5m)));
        paper.Children.Add(Field("Сдвиг печати по X, мм", Number("Origin", "LabelPrintOrigin", 0, 1800, .25m)));
        var printArea = Number("PrintAreaWidth", "LabelPrintAreaWidth", 0, 2700, .25m);
        ToolTip.SetTip(printArea, "Ширина области принтера, не белой этикетки. 0 — оставить настройку принтера для прежних макетов со сдвигом X.");
        paper.Children.Add(Field("Область печати принтера, мм", printArea));
        var dpi = new ComboBox { ItemsSource = new[] { 203, 300 }, HorizontalAlignment = HorizontalAlignment.Stretch }; dpi.Bind(ComboBox.SelectedItemProperty, new Binding("Dpi", BindingMode.TwoWay)); paper.Children.Add(Field("Разрешение принтера, dpi", dpi));
        var language = new ComboBox { ItemsSource = new[] { "ZPL", "EPL" }, HorizontalAlignment = HorizontalAlignment.Stretch }; language.Bind(ComboBox.SelectedItemProperty, new Binding("Language", BindingMode.TwoWay)); paper.Children.Add(Field("Язык принтера", language));
        paper.Children.Add(Field("Яркость ZPL", Number("Darkness", "LabelDarkness", -30, 30, 1, "0")));
        content.Children.Add(new Expander { Header = "Размер этикетки и принтер", Content = paper, HorizontalAlignment = HorizontalAlignment.Stretch });
        return content;
    }

    internal static IReadOnlyList<LabelFieldSourceViewModel> CreateSources(LabelLayoutEditorViewModel editor, LabelTemplateDefinition? defaults = null)
    {
        var initial = (defaults ?? editor.Template).Copy();
        return LabelDataBindings.Fields.Select(field => {
            var row = new LabelFieldSourceViewModel(field, (editor.Template.DataBindings ?? new()).Get(field));
            row.Changed += () => {
                editor.Template.DataBindings = (editor.Template.DataBindings ?? new()).With(field, row.Binding);
                editor.Template.Name = initial.Name; editor.Template.DeviceCode = initial.DeviceCode; editor.Template.BarcodePrefix = initial.BarcodePrefix; editor.Template.CabinetName = initial.CabinetName;
                try { editor.Template.DataBindings.ApplyPresentation(editor.Template); editor.Cabinet = editor.Template.CabinetName ?? ""; }
                catch (InvalidOperationException) { /* The editor diagnostic and explicit save report invalid input. */ }
                editor.Refresh();
            }; return row;
        }).ToArray();
    }
    private Control SourceFields(IReadOnlyList<LabelFieldSourceViewModel> sources)
    {
        var content = new StackPanel { Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };
        content.Children.Add(new TextBlock { Text = "Источники новой печати из профиля. Переменные заполняются шагами до печати; образец их не читает.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        var grid = new LabelFieldGrid { Name = "LabelSourcesGrid", MaxColumns = 3, MinColumnWidth = 180 };
        foreach (var row in sources)
        {
            var panel = new StackPanel { Spacing = 4, DataContext = row };
            panel.Children.Add(new TextBlock { Text = row.Title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            var source = new ComboBox { Name = "LabelSource" + row.Field, ItemsSource = row.Options, HorizontalAlignment = HorizontalAlignment.Stretch }; source.Bind(ComboBox.SelectedItemProperty, new Binding("Selected", BindingMode.TwoWay)); panel.Children.Add(source);
            var value = new TextBox { Name = "LabelSourceValue" + row.Field }; value.Bind(TextBox.TextProperty, new Binding("Value", BindingMode.TwoWay)); value.Bind(TextBox.WatermarkProperty, new Binding("ValueTitle")); value.Bind(IsVisibleProperty, new Binding("HasValue")); panel.Children.Add(value);
            var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, MaxLines = 2 }; hint.Bind(TextBlock.TextProperty, new Binding("Hint")); hint.Bind(ToolTip.TipProperty, new Binding("Hint")); panel.Children.Add(hint); grid.Children.Add(panel);
        }
        content.Children.Add(grid); return content;
    }
    private Control SampleFields()
    {
        var content = new StackPanel { Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };
        var grid = new LabelFieldGrid { Name = "LabelSampleFields", MaxColumns = 2, MinColumnWidth = 170 };
        foreach (var (title, property) in new[] { ("Серийный номер для примера", "Serial"), ("MAC для примера", "Mac"), ("Тип шкафа для примера", "Cabinet"), ("Код, считанный сканером", "ScannedCode") })
            grid.Children.Add(Field(title, Text(property, "Layout" + property)));
        content.Children.Add(grid);
        var diagnostics = new SelectableTextBlock { Name = "LabelBarcodeDiagnostics", TextWrapping = TextWrapping.Wrap, FontSize = 12 }; diagnostics.Bind(SelectableTextBlock.TextProperty, new Binding("Diagnostics")); content.Children.Add(diagnostics);
        var command = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 }; command.Bind(SelectableTextBlock.TextProperty, new Binding("Command")); content.Children.Add(new Expander { Header = "Команда принтера", Content = command, HorizontalAlignment = HorizontalAlignment.Stretch });
        return content;
    }
    public bool HasInputErrors() => _inputs.SelectMany(c => c.GetVisualDescendants().OfType<Control>().Prepend(c))
        .Any(c => c.IsEffectivelyEnabled && (DataValidationErrors.GetHasErrors(c) || c is NumericUpDown n &&
            (n.IsAttachedToVisualTree() || !string.IsNullOrEmpty(n.Text)) &&
            (!decimal.TryParse(n.Text, n.ParsingNumberStyle, n.NumberFormat, out var value) || value < n.Minimum || value > n.Maximum || n.FormatString == "0" && value != decimal.Truncate(value))));
    private NumericUpDown Number(string property, string name, decimal min, decimal max, decimal step, string format = "0.###")
    {
        var box = new FocusedWheelNumber { Name = name, Minimum = min, Maximum = max, Increment = step, FormatString = format, HorizontalAlignment = HorizontalAlignment.Stretch };
        box.Bind(NumericUpDown.ValueProperty, new Binding(property, BindingMode.TwoWay)); _inputs.Add(box); return box;
    }
    private sealed class FocusedWheelNumber : NumericUpDown
    {
        protected override Type StyleKeyOverride => typeof(NumericUpDown);
        protected override void OnPointerWheelChanged(Avalonia.Input.PointerWheelEventArgs e)
        {
            if (IsKeyboardFocusWithin) base.OnPointerWheelChanged(e);
        }
    }
    private static TextBox Text(string property, string name) { var box = new TextBox { Name = name }; box.Bind(TextBox.TextProperty, new Binding(property, BindingMode.TwoWay)); return box; }
    internal static Control Field(string title, Control input)
    {
        Avalonia.Automation.AutomationProperties.SetName(input, title);
        var panel = new StackPanel { Spacing = 3 }; panel.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, FontSize = 12 }); panel.Children.Add(input); return panel;
    }
    private static CheckBox Check(string title, string property) { var check = new CheckBox { Content = title, Margin = new Thickness(0, 0, 10, 0) }; check.Bind(CheckBox.IsCheckedProperty, new Binding(property, BindingMode.TwoWay)); return check; }
    private static Control Conditional(Control control, string property) { control.Bind(IsVisibleProperty, new Binding(property)); return control; }
    private static ScrollViewer Scroll(Control content) => new() { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    private static Button ActionButton(string title, Action action) { var button = new Button { Content = title, Padding = new Thickness(8, 5), Margin = new Thickness(0, 0, 6, 6) }; button.Click += (_, _) => action(); return button; }
}
