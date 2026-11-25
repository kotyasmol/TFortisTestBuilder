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
    private LabelTemplateEditorView? _activeEditor;
    private readonly Grid _body = new() { ColumnDefinitions = new("200,12,*") };
    private readonly Grid _list = new() { RowDefinitions = new("Auto,8,*") };
    public LabelTypesView()
    {
        Name = "LabelTypesWorkspace";
        var root = new DockPanel { Margin = new Thickness(16) };
        var header = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
        header.Children.Add(new TextBlock { Text = "Типы этикеток", FontSize = 20, FontWeight = FontWeight.SemiBold });
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
        var sections = new TabControl { Name = "LabelTypesSections", Padding = new Thickness(0), ItemsSource = new[] {
            new TabItem { Header = "Шаблоны", Content = _body },
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
            _body.ColumnDefinitions = new(compact ? "*" : "200,12,*"); _body.RowDefinitions = new(compact ? "90,8,*" : "*");
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
    private bool HasInputErrors() => _activeEditor?.HasInputErrors() == true;
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
        _activeEditor = null;
        if (_model?.Editor is not { } editor) { _editor.Content = new TextBlock { Text = "Выберите тип этикетки в списке.", TextWrapping = TextWrapping.Wrap }; return; }
        var content = new DockPanel();
        var heading = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 8) };
        var name = new TextBox { Name = "LabelTypeName", FontSize = 16, Watermark = "Название типа", IsReadOnly = !_model.CanRename };
        name.Bind(TextBox.TextProperty, new Binding("TypeName", BindingMode.TwoWay));
        Avalonia.Automation.AutomationProperties.SetName(name, "Название типа"); heading.Children.Add(name);
        DockPanel.SetDock(heading, Dock.Top); content.Children.Add(heading);
        _activeEditor = new LabelTemplateEditorView(editor, _model.Sources);
        var print = new StackPanel { Spacing = 6, DataContext = _model };
        var printer = new PrinterPicker { HorizontalAlignment = HorizontalAlignment.Stretch }; printer.Bind(PrinterPicker.PrinterNameProperty, new Binding("PrinterName", BindingMode.TwoWay)); print.Children.Add(printer);
        print.Children.Add(CommandButton("Пробная печать: 1 шт.", "PrintSampleCommand", "PrintLabelTypeSample"));
        _activeEditor.PrintControls.Content = print;
        content.Children.Add(_activeEditor); _editor.Content = content;
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
}
