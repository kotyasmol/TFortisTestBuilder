using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

/// <summary>The shared label workspace. All ordinary edits stay inside the main window.</summary>
public sealed class LabelTypesView : UserControl
{
    private readonly ContentControl _editor = new();
    private LabelTypesViewModel? _model;
    private readonly System.Collections.Generic.List<Control> _inputs = new();
    private readonly Grid _body = new() { ColumnDefinitions = new("230,16,*") };
    private readonly Grid _list = new() { RowDefinitions = new("Auto,8,*") };
    public LabelTypesView()
    {
        Name = "LabelTypesWorkspace";
        var root = new DockPanel { Margin = new Thickness(20) };
        var header = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock { Text = "Типы этикеток", FontSize = 24, FontWeight = FontWeight.SemiBold });
        header.Children.Add(new TextBlock { Text = "Макеты и источники данных — в типе этикетки. Выбор типа для модели — в привязках изделий.", TextWrapping = TextWrapping.Wrap });
        var tools = new WrapPanel();
        var prototype = new ComboBox { Name = "NewLabelTypePrototype", Width = 290, Margin = new Thickness(0, 0, 8, 8),
            ItemTemplate = new FuncDataTemplate<TestBuilder.Services.LabelTypeDefinition>((item, _) => new TextBlock { Text = item?.Name, TextWrapping = TextWrapping.Wrap }) };
        prototype.Bind(ItemsControl.ItemsSourceProperty, new Binding("Prototypes")); prototype.Bind(ComboBox.SelectedItemProperty, new Binding("Prototype", BindingMode.TwoWay));
        prototype.Bind(IsVisibleProperty, new Binding("IsTypesSection")); tools.Children.Add(prototype);
        var create = CommandButton("Новый тип", "NewTypeCommand", "NewLabelType"); create.Bind(IsVisibleProperty, new Binding("IsTypesSection")); tools.Children.Add(create);
        var copy = CommandButton("Создать копию", "CopyTypeCommand", "CopyLabelType"); copy.Bind(IsVisibleProperty, new Binding("IsTypesSection")); tools.Children.Add(copy);
        tools.Children.Add(CommandButton("Обновить", "ReloadCommand"));
        tools.Children.Add(FileButton("Экспорт типов…", Export)); tools.Children.Add(FileButton("Импорт типов…", Import));
        tools.Bind(IsEnabledProperty, new Binding("CanEdit")); header.Children.Add(tools);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new StackPanel { Spacing = 6, Margin = new Thickness(0, 12, 0, 0) };
        var actions = new WrapPanel();
        var save = CommandButton("Сохранить тип", "SaveCommand", "SaveLabelType"); save.Classes.Add("primary"); actions.Children.Add(save);
        actions.Children.Add(CommandButton("Отменить изменения", "DiscardCommand"));
        actions.Children.Add(CommandButton("Макет по умолчанию", "DefaultLayoutCommand", "DefaultLabelLayout"));
        var dirty = new TextBlock { Text = "Есть несохранённые изменения", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 8) };
        dirty.Bind(IsVisibleProperty, new Binding("IsDirty")); actions.Children.Add(dirty); footer.Children.Add(actions);
        actions.Bind(IsVisibleProperty, new Binding("IsTypesSection"));
        var assignmentActions = new WrapPanel();
        var saveAssignments = CommandButton("Сохранить привязки", "SaveAssignmentsCommand", "SaveLabelAssignments"); saveAssignments.Classes.Add("primary"); assignmentActions.Children.Add(saveAssignments);
        assignmentActions.Children.Add(CommandButton("Отменить привязки", "DiscardAssignmentsCommand"));
        var assignmentDirty = new TextBlock { Text = "Есть несохранённые привязки", VerticalAlignment = VerticalAlignment.Center };
        assignmentDirty.Bind(IsVisibleProperty, new Binding("HasAssignmentDrafts")); assignmentActions.Children.Add(assignmentDirty);
        assignmentActions.Bind(IsVisibleProperty, new Binding("IsAssignmentsSection")); footer.Children.Add(assignmentActions);
        var status = new TextBlock { Name = "LabelTypeStatus", TextWrapping = TextWrapping.Wrap };
        status.Bind(TextBlock.TextProperty, new Binding("Status")); footer.Children.Add(status);
        var path = new SelectableTextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Opacity = .7 };
        path.Bind(SelectableTextBlock.TextProperty, new Binding("StoragePath")); footer.Children.Add(path);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var search = new TextBox { Watermark = "Найти тип", Margin = new Thickness(0, 12, 0, 0) }; search.Bind(TextBox.TextProperty, new Binding("Search", BindingMode.TwoWay)); _list.Children.Add(search);
        var list = new ListBox { Name = "LabelTypesList", Background = Brushes.Transparent };
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding("Types")); list.Bind(ListBox.SelectedItemProperty, new Binding("Selected", BindingMode.TwoWay));
        list.ItemTemplate = new FuncDataTemplate<LabelTypeItem>((item, _) => { var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6) }; text.Bind(TextBlock.TextProperty, new Binding("Name")); return text; });
        Grid.SetRow(list, 2); _list.Children.Add(list);
        _body.Children.Add(_list); Grid.SetColumn(_editor, 2); _body.Children.Add(_editor);
        var sections = new TabControl { Name = "LabelTypesSections", ItemsSource = new[] {
            new TabItem { Header = "Типы этикеток", Content = _body },
            new TabItem { Header = "Привязки изделий", Content = BuildAssignments() }
        } };
        sections.Bind(TabControl.SelectedIndexProperty, new Binding("SelectedSection", BindingMode.TwoWay)); root.Children.Add(sections); Content = root;
        _body.Bind(IsEnabledProperty, new Binding("CanEdit"));
        DataContextChanged += (_, _) => Attach();
        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => { if (_model != null) { _model.EditorChanged -= ShowEditor; _model.HasInputErrors = null; } _model = null; };
        SizeChanged += (_, _) =>
        {
            var compact = Bounds.Width < 900;
            _body.ColumnDefinitions = new(compact ? "*" : "230,16,*"); _body.RowDefinitions = new(compact ? "150,12,*" : "*");
            Grid.SetColumn(_editor, compact ? 0 : 2); Grid.SetRow(_editor, compact ? 2 : 0);
        };
    }
    private void Attach()
    {
        if (ReferenceEquals(_model, DataContext)) return;
        if (_model != null) { _model.EditorChanged -= ShowEditor; _model.HasInputErrors = null; }
        _model = DataContext as LabelTypesViewModel;
        if (_model != null) { _model.EditorChanged += ShowEditor; _model.HasInputErrors = HasInputErrors; }
        ShowEditor();
    }
    private bool HasInputErrors() => _inputs.SelectMany(c => c.GetVisualDescendants().OfType<Control>().Prepend(c))
        .Any(c => c.IsEffectivelyEnabled && (DataValidationErrors.GetHasErrors(c) || c is NumericUpDown n &&
            (n.IsAttachedToVisualTree() || !string.IsNullOrEmpty(n.Text)) &&
            (!decimal.TryParse(n.Text, n.ParsingNumberStyle, n.NumberFormat, out var value) || value < n.Minimum || value > n.Maximum || n.FormatString == "0" && value != decimal.Truncate(value))));
    private static Button CommandButton(string title, string command, string? name = null)
    {
        var button = new Button { Content = title, Margin = new Thickness(0, 0, 8, 8) };
        if (name != null) button.Name = name;
        button.Bind(Button.CommandProperty, new Binding(command)); return button;
    }
    private Button FileButton(string title, Func<System.Threading.Tasks.Task> action)
    {
        var button = new Button { Content = title, Margin = new Thickness(0, 0, 8, 8) };
        button.Click += async (_, _) =>
        {
            if (_model?.CanEdit != true) return;
            try { await action(); } catch (Exception ex) when (LabelTypesViewModel.IsCatalogError(ex)) { _model.Status = ex.Message; }
        };
        return button;
    }
    private async System.Threading.Tasks.Task Export()
    {
        var json = _model!.Export();
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Экспорт типов этикеток", SuggestedFileName = "label-types.json" });
        if (file == null) return;
        await using var stream = await file.OpenWriteAsync(); stream.SetLength(0);
        await using var writer = new StreamWriter(stream); await writer.WriteAsync(json);
        _model.Status = "Типы экспортированы. На другом компьютере откройте «Этикетки → Типы этикеток → Импорт типов…».";
    }
    private async System.Threading.Tasks.Task Import()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Импорт типов этикеток", AllowMultiple = false });
        if (files.Count == 0) return;
        await using var stream = await files[0].OpenReadAsync();
        // Bound reads also cover providers that do not expose a seekable local file.
        using var reader = new StreamReader(stream); var buffer = new char[4_000_001]; var length = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        if (length > 4_000_000) throw new InvalidDataException("Каталог типов этикеток больше 4 МБ.");
        if (_model?.CanEdit == true) _model.Import(new string(buffer, 0, length));
    }
    private void ShowEditor()
    {
        _inputs.Clear();
        if (_model?.Editor is not { } editor) { _editor.Content = new TextBlock { Text = "Выберите тип этикетки в списке.", TextWrapping = TextWrapping.Wrap }; return; }
        var content = new StackPanel { Spacing = 12 };
        var name = new TextBox { Name = "LabelTypeName", FontSize = 18, Watermark = "Название типа" }; name.Bind(TextBox.TextProperty, new Binding("TypeName", BindingMode.TwoWay));
        name.IsReadOnly = !_model.CanRename; content.Children.Add(Field("Название типа", name, double.NaN));
        var scope = new TextBlock { TextWrapping = TextWrapping.Wrap }; scope.Bind(TextBlock.TextProperty, new Binding("Scope")); content.Children.Add(scope);
        var tabs = new TabControl { Name = "LabelTypeEditorTabs" };
        var layout = new StackPanel { Spacing = 12, Margin = new Thickness(0, 12, 8, 0) };
        var canvas = new LabelLayoutCanvas(editor) { Height = 250, MinHeight = 180, HorizontalAlignment = HorizontalAlignment.Stretch };
        layout.Children.Add(canvas);
        var quick = new WrapPanel();
        quick.Children.Add(Field("Отступ всего макета сверху, мм", Number("TopMargin", "LabelTopMargin", 0, 200, .25m), 240));
        var barHeight = Field("Высота штрихкода, мм", Number("BarcodeHeight", "LabelBarcodeHeight", .1m, 200, .5m), 220); barHeight.Bind(IsEnabledProperty, new Binding("HasBarcode")); quick.Children.Add(barHeight);
        var barModule = Field("Ширина полосы, точек", Number("BarcodeModule", "LabelBarcodeModule", 1, 10, 1, "0"), 220); barModule.Bind(IsEnabledProperty, new Binding("HasBarcode")); quick.Children.Add(barModule);
        layout.Children.Add(quick);
        layout.Children.Add(new TextBlock { Text = "Выберите элемент на образце и перетащите. Размер — за нижний правый угол; точные значения — ниже.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        var element = new Grid { ColumnDefinitions = new("210,16,*"), DataContext = editor };
        var elements = new StackPanel { Spacing = 8 };
        var picker = new ComboBox { Name = "LabelTypeElements", ItemsSource = editor.Elements, HorizontalAlignment = HorizontalAlignment.Stretch };
        picker.Bind(ComboBox.SelectedItemProperty, new Binding("Selected", BindingMode.TwoWay)); elements.Children.Add(picker);
        var add = new WrapPanel();
        foreach (var kind in LabelLayoutEditorViewModel.Kinds)
        {
            var button = new Button { Content = "+ " + kind.Title, Margin = new Thickness(0, 0, 6, 6) };
            button.Click += (_, _) => editor.Add(kind.Key); add.Children.Add(button);
        }
        elements.Children.Add(add);
        var remove = new Button { Content = "Удалить элемент" }; remove.Click += (_, _) => editor.Remove(); elements.Children.Add(remove); element.Children.Add(elements);
        var fields = new StackPanel { Spacing = 8 };
        fields.Bind(IsEnabledProperty, new Binding("HasSelection"));
        var value = new TextBox { Name = "LabelElementValue" }; value.Bind(TextBox.TextProperty, new Binding("Value", BindingMode.TwoWay)); fields.Children.Add(Field("Текст / подстановка", value, double.NaN));
        var sizes = new WrapPanel();
        foreach (var field in new[] { ("X, мм", "X"), ("Y, мм", "Y"), ("Ширина, мм", "Width"), ("Высота, мм", "Height"), ("Ширина шрифта, мм", "FontWidth") })
            sizes.Children.Add(Field(field.Item1, Number(field.Item2, "Element" + field.Item2, field.Item2 is "X" or "Y" ? 0 : .1m, 200, .25m), 150));
        fields.Children.Add(sizes);
        var visible = new CheckBox { Content = "Печатать элемент" }; visible.Bind(CheckBox.IsCheckedProperty, new Binding("Visible", BindingMode.TwoWay)); fields.Children.Add(visible);
        Grid.SetColumn(fields, 2); element.Children.Add(fields); layout.Children.Add(element);
        var paper = new WrapPanel { DataContext = editor };
        paper.Children.Add(Field("Ширина этикетки, мм", Number("PaperWidth", "LabelPaperWidth", 1, 200, .5m), 190));
        paper.Children.Add(Field("Высота этикетки, мм", Number("PaperHeight", "LabelPaperHeight", 1, 200, .5m), 190));
        paper.Children.Add(Field("Сдвиг печати по X, мм", Number("Origin", "LabelPrintOrigin", 0, 200, .25m), 190));
        var dpi = new ComboBox { ItemsSource = new[] { 203, 300 }, HorizontalAlignment = HorizontalAlignment.Stretch }; dpi.Bind(ComboBox.SelectedItemProperty, new Binding("Dpi", BindingMode.TwoWay)); paper.Children.Add(Field("Разрешение принтера, dpi", dpi, 200));
        var language = new ComboBox { ItemsSource = new[] { "ZPL", "EPL" }, HorizontalAlignment = HorizontalAlignment.Stretch }; language.Bind(ComboBox.SelectedItemProperty, new Binding("Language", BindingMode.TwoWay)); paper.Children.Add(Field("Язык принтера", language, 160));
        paper.Children.Add(Field("Яркость ZPL", Number("Darkness", "LabelDarkness", -30, 30, 1, "0"), 160));
        layout.Children.Add(new Expander { Header = "Размер этикетки и принтер", Content = paper, HorizontalAlignment = HorizontalAlignment.Stretch });
        var data = new LabelDataSettingsView { DataContext = editor.Template }; data.Changed += editor.Refresh;
        var sample = new StackPanel { Spacing = 8, DataContext = editor, Margin = new Thickness(0, 12, 8, 0) };
        foreach (var field in new[] { ("Серийный номер для примера", "Serial"), ("MAC для примера", "Mac"), ("Тип шкафа для примера", "Cabinet"), ("Код, считанный сканером", "ScannedCode") })
        { var box = new TextBox(); box.Bind(TextBox.TextProperty, new Binding(field.Item2, BindingMode.TwoWay)); sample.Children.Add(Field(field.Item1, box, double.NaN)); }
        var diagnostics = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap }; diagnostics.Bind(SelectableTextBlock.TextProperty, new Binding("Diagnostics"));
        sample.Children.Add(diagnostics);
        var command = new SelectableTextBlock { FontFamily = FontFamily.Default, TextWrapping = TextWrapping.Wrap }; command.Bind(SelectableTextBlock.TextProperty, new Binding("Command"));
        sample.Children.Add(new Expander { Header = "Команда принтера", Content = command, HorizontalAlignment = HorizontalAlignment.Stretch });
        tabs.ItemsSource = new[] {
            new TabItem { Header = "Макет", Content = layout },
            new TabItem { Header = "Источники данных", Content = BuildSources() },
            new TabItem { Header = "SN и штрихкод", Content = data },
            new TabItem { Header = "Проба и проверка", Content = sample }
        };
        content.Children.Add(tabs);
        var print = new WrapPanel();
        var printer = new PrinterPicker { Width = 300, Margin = new Thickness(0, 0, 12, 8) }; printer.Bind(PrinterPicker.PrinterNameProperty, new Binding("PrinterName", BindingMode.TwoWay)); print.Children.Add(printer);
        print.Children.Add(CommandButton("Пробная печать: 1 шт.", "PrintSampleCommand", "PrintLabelTypeSample")); content.Children.Add(print);
        _editor.Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }
    private Control BuildSources()
    {
        var content = new StackPanel { Spacing = 12, Margin = new Thickness(0, 12, 8, 0) };
        content.Children.Add(new TextBlock { Text = "Источники для новой печати из профиля. При ручной печати SN и MAC берутся из введённых полей; повтор использует сохранённые данные.", TextWrapping = TextWrapping.Wrap });
        foreach (var row in _model!.Sources)
        {
            var panel = new StackPanel { Spacing = 6, DataContext = row };
            panel.Children.Add(new TextBlock { Text = row.Title, FontWeight = FontWeight.SemiBold });
            var fields = new WrapPanel();
            var source = new ComboBox { Name = "LabelSource" + row.Field, ItemsSource = row.Options, Width = 290, Margin = new Thickness(0, 0, 12, 4) };
            source.Bind(ComboBox.SelectedItemProperty, new Binding("Selected", BindingMode.TwoWay)); fields.Children.Add(source);
            var value = new TextBox { Name = "LabelSourceValue" + row.Field, Width = 290 };
            value.Bind(TextBox.TextProperty, new Binding("Value", BindingMode.TwoWay)); value.Bind(TextBox.WatermarkProperty, new Binding("ValueTitle")); value.Bind(IsVisibleProperty, new Binding("HasValue")); fields.Children.Add(value);
            panel.Children.Add(fields);
            var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 }; hint.Bind(TextBlock.TextProperty, new Binding("Hint")); panel.Children.Add(hint); content.Children.Add(panel);
        }
        content.Children.Add(new TextBlock { Text = "Состав SN и штрихкода задаётся на вкладке «SN и штрихкод». Образец использует нули; переменные проверки при его просмотре не читаются.", TextWrapping = TextWrapping.Wrap });
        return content;
    }
    private static Control BuildAssignments()
    {
        var content = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var header = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock { Text = "Выберите тип для каждой модели. Сначала сохраните новый тип. Автоматический выбор: платы — штрихкод/SN, изделия с MAC — второй тип, с назначенным шкафом — третий.", TextWrapping = TextWrapping.Wrap });
        var search = new TextBox { Name = "LabelAssignmentSearch", Watermark = "Найти изделие" }; search.Bind(TextBox.TextProperty, new Binding("AssignmentSearch", BindingMode.TwoWay)); header.Children.Add(search);
        DockPanel.SetDock(header, Dock.Top); content.Children.Add(header);
        var grid = new DataGrid { Name = "LabelProductAssignments", AutoGenerateColumns = false, IsReadOnly = true, RowHeight = 64, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal };
        grid.Bind(DataGrid.ItemsSourceProperty, new Binding("Assignments"));
        grid.Columns.Add(new DataGridTextColumn { Header = "Изделие", Binding = new Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTemplateColumn { Header = "Тип этикетки", Width = new DataGridLength(1.3, DataGridLengthUnitType.Star),
            CellTemplate = new FuncDataTemplate<LabelProductBindingRow>((row, _) => {
                var picker = new ComboBox { Name = "ProductLabelType", HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) };
                picker.Bind(ItemsControl.ItemsSourceProperty, new Binding("Choices")); picker.Bind(ComboBox.SelectedItemProperty, new Binding("Selected", BindingMode.TwoWay)); return picker;
            }) });
        content.Children.Add(grid); content.Bind(IsEnabledProperty, new Binding("CanEdit")); return content;
    }
    private static Control Field(string title, Control control, double width)
    {
        Avalonia.Automation.AutomationProperties.SetName(control, title);
        var panel = new StackPanel { Spacing = 4, Width = width, Margin = new Thickness(0, 0, 12, 4) };
        panel.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap }); panel.Children.Add(control); return panel;
    }
    private NumericUpDown Number(string property, string name, decimal min, decimal max, decimal step, string format = "0.###")
    {
        var input = new NumericUpDown { Name = name, Minimum = min, Maximum = max, Increment = step, FormatString = format, HorizontalAlignment = HorizontalAlignment.Stretch };
        input.Bind(NumericUpDown.ValueProperty, new Binding(property, BindingMode.TwoWay)); _inputs.Add(input); return input;
    }
}
