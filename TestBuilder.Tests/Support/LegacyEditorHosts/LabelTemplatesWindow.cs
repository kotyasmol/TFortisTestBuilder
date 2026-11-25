using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using System.IO;
using System.Text.Json;
using TestBuilder.Services;

namespace TestBuilder.Views;
public sealed class LabelTemplatesWindow : Window
{
    public LabelTemplatesWindow(LabelTemplateDefinition? profileTemplate = null, bool settingsOnly = false, bool profileOnly = false)
    {
        Title = profileOnly ? "Этикетка ноды профиля" : settingsOnly ? "Каталог этикеток" : "Шаблоны для ручной печати";
        Width = 1040; Height = 800; MinWidth = 800; MinHeight = 600;
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var inputs = new List<TextBox>();
        var catalogPath = LabelTemplateStore.LocalPath(AppSettings.Instance.ResolvedGraphsFolder);
        var imported = false;
        string? catalogRevision = null;
        var templates = new ObservableCollection<LabelTemplateDefinition>();
        try
        {
            catalogRevision = LabelTemplateStore.Revision(catalogPath);
            var loaded = LabelTemplateStore.Load(AppSettings.Instance.ResolvedGraphsFolder, AppSettings.Instance.LabelTemplates);
            if (LabelTemplateStore.Revision(catalogPath) != catalogRevision) throw new InvalidDataException("Каталог изменён во время чтения. Откройте окно заново.");
            foreach (var t in loaded) templates.Add(t);
        }
        catch (Exception ex) { foreach (var t in AppSettings.Instance.LabelTemplates) templates.Add(t.Copy()); status.Text = "Каталог этикеток не загружен: " + ex.Message; }
        if (profileTemplate != null)
        {
            var existing = templates.FirstOrDefault(t => t.Id == profileTemplate.Id);
            if (existing != null) templates.Remove(existing);
            templates.Add(profileTemplate.CopyUpgraded());
        }
        var root = new DockPanel { Margin = new Thickness(20) };
        var scope = new TextBlock { Text = profileOnly ? "Изменения применяются только к ноде текущего профиля. После применения сохраните профиль." :
            "Общий каталог макетов. Сохранённые ноды используют свои копии шаблонов.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(scope, Dock.Top); root.Children.Add(scope);
        var footer = new StackPanel { Spacing = 8 };
        footer.Children.Add(status);
        footer.Children.Add(new SelectableTextBlock { Name = "LabelCatalogPath", Text = "Каталог: " + catalogPath, IsVisible = !profileOnly, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var save = new Button { Content = profileOnly ? "Применить к ноде" : settingsOnly ? "Сохранить каталог" : "Сохранить каталог и выбрать шаблон" }; footer.Children.Add(save);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("260,16,*") };
        var left = new DockPanel(); var actions = new StackPanel { Spacing = 8 };
        var list = new ListBox { ItemsSource = templates, ItemTemplate = new FuncDataTemplate<LabelTemplateDefinition>((_, _) =>
        { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding("Name")); return text; }) };
        var add = new Button { Content = "Новый шаблон" };
        add.Click += (_, _) => { var t = new LabelTemplateDefinition(); templates.Add(t); list.SelectedItem = t; };
        var copy = new Button { Content = "Создать копию" };
        copy.Click += (_, _) => { if (list.SelectedItem is LabelTemplateDefinition t) { var c = t.Copy(); c.Id = Guid.NewGuid().ToString(); c.Name += " (копия)"; c.MatchServerSerial = false; templates.Add(c); list.SelectedItem = c; } };
        var remove = new Button { Content = "Удалить шаблон", IsVisible = !profileOnly };
        remove.Click += (_, _) => { if (list.SelectedItem is LabelTemplateDefinition t) { templates.Remove(t); list.SelectedItem = templates.FirstOrDefault(); } };
        actions.Children.Add(add); actions.Children.Add(copy); actions.Children.Add(remove); DockPanel.SetDock(actions, Dock.Bottom); left.Children.Add(actions); left.Children.Add(list);
        var export = new Button { Content = "Экспорт каталога…", IsVisible = !profileOnly };
        export.Click += async (_, _) =>
        {
            try
            {
                if (inputs.Any(DataValidationErrors.GetHasErrors)) throw new InvalidOperationException("Исправьте значения шаблона перед экспортом.");
                LabelTemplateCatalog.Validate(templates);
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Экспорт каталога этикеток", SuggestedFileName = "label-templates.json" });
                if (file == null) return;
                await using var stream = await file.OpenWriteAsync(); stream.SetLength(0);
                await JsonSerializer.SerializeAsync(stream, new LabelTemplateFile { IsComplete = true, Templates = templates.Select(t => t.Copy()).ToList() }, EquipmentCatalog.JsonOptions);
                status.Text = "Каталог экспортирован. Он содержит макеты, элементы и правила формирования данных.";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        var import = new Button { Content = "Импорт каталога…", IsVisible = !profileOnly };
        import.Click += async (_, _) =>
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Импорт каталога этикеток", AllowMultiple = false });
                if (files.Count == 0) return;
                await using var stream = await files[0].OpenReadAsync();
                if (stream.Length > 4_000_000) throw new InvalidDataException("Каталог больше 4 МБ.");
                var file = await JsonSerializer.DeserializeAsync<LabelTemplateFile>(stream, EquipmentCatalog.JsonOptions) ?? throw new InvalidDataException("Пустой каталог.");
                var merged = LabelTemplateStore.Merge(templates, file);
                templates.Clear(); foreach (var t in merged) templates.Add(t); list.SelectedItem = templates.FirstOrDefault(); imported = true;
                status.Text = "Каталог загружен в черновик. Совпадающие ID заменены. Для записи нажмите «Сохранить каталог»; прежний файл будет сохранён в .bak.";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        actions.Children.Add(import); actions.Children.Add(export);
        grid.Children.Add(left);
        var fields = new StackPanel { Spacing = 6 };
        var commandOnlyInputs = new List<TextBox>();
        var previewPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 12) };
        var right = new ContentControl();
        previewPanel.Children.Add(new TextBlock { Text = "Пример этикетки", FontWeight = Avalonia.Media.FontWeight.SemiBold });
        var editLayout = new Button { Content = "Редактор элементов…", Name = "EditLabelLayout" };
        editLayout.Click += (_, _) =>
        {
            try
            {
                if (inputs.Any(DataValidationErrors.GetHasErrors)) throw new InvalidOperationException("Исправьте значения шаблона перед открытием редактора.");
                if (list.SelectedItem is not LabelTemplateDefinition selected) return;
                var original = right.Content;
                save.IsEnabled = list.IsEnabled = actions.IsEnabled = false;
                void ReturnToTemplates() { right.Content = original; save.IsEnabled = list.IsEnabled = actions.IsEnabled = true; }
                var editor = new LabelTemplateEditorView(new TestBuilder.ViewModels.LabelLayoutEditorViewModel(selected));
                editor.PrintControls.Content = new LabelEditorPrintView(editor.Editor, () => IsEnabled);
                var pane = new DockPanel(); var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
                var applyLayout = new Button { Content = "Применить макет", Name = "ApplyInlineLabelLayout", Margin = new Thickness(0, 0, 8, 0) };
                applyLayout.Click += (_, _) =>
                {
                    try
                    {
                        if (editor.HasInputErrors()) throw new InvalidOperationException("Исправьте поля этикетки.");
                        var edited = editor.Editor.Apply(); var index = templates.IndexOf(selected);
                        templates[index] = edited; list.SelectedItem = edited; ReturnToTemplates();
                    }
                    catch (Exception ex) { status.Text = ex.Message; }
                };
                var back = new Button { Content = "Назад к шаблонам", Name = "BackToLabelTemplates" };
                back.Click += (_, _) => ReturnToTemplates();
                buttons.Children.Add(applyLayout); buttons.Children.Add(back); DockPanel.SetDock(buttons, Dock.Top); pane.Children.Add(buttons); pane.Children.Add(editor); right.Content = pane;
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        previewPanel.Children.Add(editLayout);
        var preview = new LabelPreviewControl { Height = 150, MaxWidth = 500 };
        var paperSize = new TextBlock();
        var previewMessage = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var cabinetSample = new TextBox { Watermark = "Шкаф для предпросмотра", Name = "CabinetPreviewSample" };
        previewPanel.Children.Add(preview); previewPanel.Children.Add(paperSize); previewPanel.Children.Add(cabinetSample); previewPanel.Children.Add(previewMessage);
        void UpdatePreview()
        {
            var template = list.SelectedItem as LabelTemplateDefinition;
            var sample = template?.Copy();
            if (sample != null && !string.IsNullOrWhiteSpace(cabinetSample.Text)) sample.CabinetName = cabinetSample.Text.Trim();
            preview.Preview = sample == null ? null : LabelPreview.FromInput(sample, "", "");
            paperSize.Text = template?.HeightDots is int height && template.PrinterDpi > 0
                ? $"{template.WidthDots * 25.4 / template.PrinterDpi:0.#} × {height * 25.4 / template.PrinterDpi:0.#} мм · {template.PrinterDpi} dpi"
                : "";
            previewMessage.Text = preview.Preview?.Message;
        }
        var previewTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        previewTimer.Tick += (_, _) => { previewTimer.Stop(); UpdatePreview(); };
        void SchedulePreview() { previewTimer.Stop(); previewTimer.Start(); }
        cabinetSample.TextChanged += (_, _) => SchedulePreview();
        Closed += (_, _) => previewTimer.Stop();
        var dataSettings = new LabelDataSettingsView();
        dataSettings.Changed += SchedulePreview;
        fields.Children.Add(new Expander { Header = "Формирование SN и штрихкода", Content = dataSettings, HorizontalAlignment = HorizontalAlignment.Stretch, IsExpanded = true });
        foreach (var (label, property) in new[] { ("Название изделия / шаблона", "Name"), ("Язык (ZPL или EPL)", "Language"),
            ("Разрешение принтера, dpi (203 или 300)", "PrinterDpi"), ("Ширина этикетки, точки", "WidthDots"), ("Высота этикетки, точки", "HeightDots"),
            ("Левый край этикетки, точки", "OriginXDots"), ("Ширина узкой полосы ZPL, точки", "BarcodeModuleDots"),
            ("Отступ штрихкода ZPL сверху, точки", "BarcodeTopDots"), ("Высота штрихкода ZPL, точки", "BarcodeHeightDots"),
            ("Ширина поля шкафа ZPL, точки", "CabinetWidthDots"), ("Высота текста шкафа ZPL, точки", "CabinetFontHeightDots"),
            ("Смещение полного серийного номера", "SerialOffset"), ("Максимум короткого номера", "ShortSerialMax"),
            ("Цифр короткого номера по умолчанию", "ShortSerialDigits"), ("Код типа устройства", "DeviceCode"), ("Префикс старого формата (если новый формат не задан)", "BarcodePrefix") })
        {
            fields.Children.Add(new TextBlock { Text = label });
            var box = new TextBox(); box.Bind(TextBox.TextProperty, new Binding(property) { Mode = BindingMode.TwoWay }); fields.Children.Add(box); inputs.Add(box);
            if (property is "BarcodeModuleDots" or "BarcodeTopDots" or "BarcodeHeightDots" or "CabinetWidthDots" or "CabinetFontHeightDots")
            { commandOnlyInputs.Add(box); ToolTip.SetTip(box, "Для визуального макета меняйте параметр у соответствующего элемента в редакторе."); }
            box.TextChanged += (_, _) => SchedulePreview();
        }
        foreach (var (label, property) in new[] { ("Использовать полный и короткий серийный номер", "UseShortSerial"),
            ("Требовать MAC", "RequiresMac"), ("Определять шаблон по диапазону серийного номера с сервера", "MatchServerSerial") })
        {
            var box = new CheckBox { Content = label }; box.Bind(CheckBox.IsCheckedProperty, new Binding(property) { Mode = BindingMode.TwoWay }); fields.Children.Add(box);
            box.PropertyChanged += (_, e) => { if (e.Property == CheckBox.IsCheckedProperty) SchedulePreview(); };
        }
        fields.Children.Add(new TextBlock { Text = "Команда принтера. {{SerialText}} — SN по настройкам выше; {{Barcode}} — данные штрихкода; {{Name}}, {{Serial}}, {{ShortSerial}}, {{Mac}}, {{Variant}}, {{Cabinet}} — исходные поля. ZPL: {{ZplBarcode}} — компактный Code 128; {{CabinetField}} — шкаф рядом с SN с шириной {{SerialTextWidth}}.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var command = new TextBox { AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 180 };
        command.Bind(TextBox.TextProperty, new Binding("Command") { Mode = BindingMode.TwoWay }); fields.Children.Add(command);
        command.TextChanged += (_, _) => SchedulePreview();
        var useCommand = new Button { Content = "Вернуть исходную команду" };
        useCommand.Click += (_, _) => { if (list.SelectedItem is LabelTemplateDefinition t) { t.Command = t.Layout?.OriginalCommand ?? t.Command; t.Layout = null; fields.DataContext = null; fields.DataContext = t; command.IsReadOnly = false; foreach (var box in commandOnlyInputs) box.IsEnabled = true; UpdatePreview(); status.Text = "Визуальные элементы убраны из черновика. Используется команда, сохранённая до визуального редактирования."; } };
        fields.Children.Add(useCommand);
        list.SelectionChanged += (_, _) => { fields.DataContext = list.SelectedItem; command.IsReadOnly = (list.SelectedItem as LabelTemplateDefinition)?.Layout != null;
            foreach (var box in commandOnlyInputs) box.IsEnabled = !command.IsReadOnly; UpdatePreview(); };
        var form = new DockPanel(); DockPanel.SetDock(previewPanel, Dock.Top); form.Children.Add(previewPanel);
        form.Children.Add(new ScrollViewer { Content = fields }); right.Content = form; Grid.SetColumn(right, 2); grid.Children.Add(right);
        root.Children.Add(grid); Content = root;
        list.SelectedItem = templates.FirstOrDefault(t => t.Id == profileTemplate?.Id) ?? templates.FirstOrDefault();
        save.Click += (_, _) =>
        {
            try
            {
                if (inputs.Any(DataValidationErrors.GetHasErrors)) throw new InvalidOperationException("Исправьте значения шаблона.");
                var selected = list.SelectedItem as LabelTemplateDefinition ?? throw new InvalidOperationException("Выберите шаблон.");
                selected.CommitDataFormats();
                if (profileOnly) LabelTemplateCatalog.Validate(new[] { selected });
                else
                {
                    LabelTemplateCatalog.Validate(templates);
                    LabelTemplateStore.Save(catalogPath, templates, preserveExisting: !imported, expectedRevision: catalogRevision);
                }
                Close(selected.Copy());
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
    }
}
