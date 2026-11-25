using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

/// <summary>A template picker and its editor in the graph's existing workspace.</summary>
public sealed class ProfileLabelEditorView : UserControl
{
    private sealed class Option(string name, LabelTemplateDefinition template)
    {
        public string Name { get; } = name;
        public LabelTemplateDefinition Template { get; } = template;
        public LabelLayoutEditorViewModel? Draft { get; set; }
        public IReadOnlyList<LabelFieldSourceViewModel>? Sources { get; set; }
        public LabelRawTemplateEditorView? RawDraft { get; set; }
    }
    private readonly ContentControl _editorHost = new();
    private readonly TextBlock _status = new() { Name = "ProfileLabelStatus", TextWrapping = TextWrapping.Wrap };
    private readonly Func<bool> _canApply;
    private LabelTemplateEditorView? _editor;
    private LabelRawTemplateEditorView? _rawEditor;
    public event Action<LabelTemplateDefinition?>? Finished;
    public ProfileLabelEditorView(LabelTemplateDefinition source, Func<bool> canApply, EquipmentModel? model = null,
        string? deviceName = null, int variant = 0, Action? openShared = null)
    {
        _canApply = canApply; Name = "ProfileLabelWorkspace";
        var root = new DockPanel { Margin = new Thickness(16) };
        var top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 10) };
        var heading = new DockPanel();
        if (openShared != null)
        {
            var shared = Button("Общие типы этикеток", () => { Finished?.Invoke(null); openShared(); });
            DockPanel.SetDock(shared, Dock.Right); heading.Children.Add(shared);
        }
        heading.Children.Add(new TextBlock { Text = "Этикетка ноды", FontSize = 20, FontWeight = FontWeight.SemiBold }); top.Children.Add(heading);
        top.Children.Add(new TextBlock { Text = "Копия для этой ноды. Общий тип изделия имеет приоритет при печати. После применения сохраните профиль.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var footer = new StackPanel { Spacing = 6, Margin = new Thickness(0, 10, 0, 0) }; footer.Children.Add(_status);
        var actions = new WrapPanel();
        var apply = Button("Применить к ноде", Apply); apply.Name = "ApplyProfileLabel"; apply.Classes.Add("primary"); actions.Children.Add(apply);
        var cancel = Button("Отмена", () => Finished?.Invoke(null)); cancel.Name = "CancelProfileLabel"; actions.Children.Add(cancel); footer.Children.Add(actions);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var options = new List<Option> { new("Копия этой ноды", source.CopyUpgraded()) };
        try
        {
            foreach (var type in LabelTypeStore.Available(LabelTypeStore.LoadFile(AppSettings.Instance.ResolvedGraphsFolder)))
                options.Add(new(type.Name, LabelTypeStore.ApplyPresentation(type.Template, source, model, deviceName, variant)));
            var defaults = LabelTemplateCatalog.Defaults().Select(t => t.Id).ToHashSet();
            foreach (var legacy in LabelTemplateCatalog.ConfiguredTemplates().Where(t => !defaults.Contains(t.Id)))
                options.Add(new("Прежний шаблон: " + legacy.Name, legacy.CopyUpgraded()));
        }
        catch (Exception ex) when (LabelTypesViewModel.IsCatalogError(ex)) { _status.Text = "Общие шаблоны не загружены: " + ex.Message; }
        var body = new DockPanel();
        var selection = new Grid { ColumnDefinitions = new("*,12,*"), Margin = new Thickness(0, 0, 0, 10) };
        var picker = new ComboBox { Name = "ProfileLabelTemplates", ItemsSource = options, HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = new FuncDataTemplate<Option>((item, _) => new TextBlock { Text = item?.Name, TextWrapping = TextWrapping.Wrap }) };
        selection.Children.Add(LabelTemplateEditorView.Field("Шаблон", picker));
        var name = new TextBox { Name = "ProfileLabelDeviceName", IsReadOnly = true };
        name.Bind(TextBox.TextProperty, new Binding("Name", BindingMode.TwoWay));
        name.TextChanged += (_, _) => _editor?.Editor.Refresh();
        var nameField = LabelTemplateEditorView.Field("Название на образце", name); Grid.SetColumn(nameField, 2); selection.Children.Add(nameField);
        DockPanel.SetDock(selection, Dock.Top); body.Children.Add(selection); body.Children.Add(_editorHost); root.Children.Add(body);
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is not Option option) return;
            try
            {
                _rawEditor = null;
                option.Draft ??= new LabelLayoutEditorViewModel(option.Template);
                option.Sources ??= LabelTemplateEditorView.CreateSources(option.Draft,
                    model != null ? LabelPrintContent.ForModel(option.Template, model, variant) : LabelPrintContent.ForDevice(option.Template, deviceName));
                _editor = new LabelTemplateEditorView(option.Draft, option.Sources);
                _editor.PrintControls.Content = new LabelEditorPrintView(option.Draft, canApply);
                name.DataContext = option.Draft.Template; _editorHost.Content = _editor;
            }
            catch (Exception ex) when (LabelTypesViewModel.IsCatalogError(ex))
            {
                _editor = null; option.RawDraft ??= new LabelRawTemplateEditorView(option.Template);
                _rawEditor = option.RawDraft; name.DataContext = _rawEditor.Template; _editorHost.Content = _rawEditor;
                _status.Text = "Визуальное редактирование недоступно: " + ex.Message;
            }
        };
        Content = root; picker.SelectedIndex = 0;
    }
    private void Apply()
    {
        try
        {
            if (!_canApply()) throw new InvalidOperationException("Редактирование недоступно во время выполнения операции.");
            if (_rawEditor != null) { Finished?.Invoke(_rawEditor.Apply()); return; }
            if (_editor == null) throw new InvalidOperationException("Выберите шаблон для редактирования.");
            if (_editor.HasInputErrors()) throw new InvalidOperationException("Исправьте значения в полях этикетки.");
            Finished?.Invoke(_editor.Editor.Apply());
        }
        catch (Exception ex) when (LabelTypesViewModel.IsCatalogError(ex)) { _status.Text = ex.Message; }
    }
    private static Button Button(string title, Action action)
    {
        var button = new Button { Content = title, Margin = new Thickness(0, 0, 8, 0) };
        button.Click += (_, _) => action(); return button;
    }
}
