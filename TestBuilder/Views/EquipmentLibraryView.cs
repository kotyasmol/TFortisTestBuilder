using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

// Shared catalog; this view never holds or changes an open profile.
public sealed class EquipmentLibraryView : UserControl
{
    private readonly EquipmentLibrary _library;
    private readonly EquipmentTemplateBrowser _browser;
    private readonly ContentControl _host = new() { Name = "EquipmentLibraryPageHost" };
    private readonly TextBlock _status = new() { Name = "EquipmentLibraryStatus", TextWrapping = TextWrapping.Wrap };
    private readonly Control _main;
    private Action? _cancel;
    public event Action? Finished;

    public EquipmentLibraryView() : this(new EquipmentLibrary()) { }
    internal EquipmentLibraryView(EquipmentLibrary library)
    {
        _library = library;
        var root = new DockPanel { Margin = new Thickness(20) };
        var top = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(new TextBlock { Text = "Каталог оборудования", FontSize = 22, FontWeight = FontWeight.SemiBold });
        top.Children.Add(new TextBlock { Text = "Общие шаблоны устройств и регистров. Состав и Slave-адреса конкретной проверки задаются через «Оборудование профиля…» рядом с сохранением графа.", TextWrapping = TextWrapping.Wrap });
        _browser = new EquipmentTemplateBrowser(library);
        var actions = new WrapPanel();
        actions.Children.Add(Button("Новое оборудование…", () => Edit(null, new DeviceDefinition { Name = "", Registers = new() { new RegisterDefinition() } })));
        var edit = Button("Редактировать своё…", () =>
        { if (_browser.Selected is EquipmentTemplate { IsBuiltIn: false } entry) Edit(entry.Id, entry.Device); });
        actions.Children.Add(edit);
        var copy = Button("Создать копию…", () =>
        {
            if (_browser.Selected is not EquipmentTemplate entry) return;
            var device = TestViewModel.CopyDefinition(entry.Device); device.Name += " (копия)"; Edit(null, device);
        });
        actions.Children.Add(copy);
        actions.Children.Add(Button("Импорт каталога…", async () => await ImportAsync()));
        actions.Children.Add(Button("Экспорт каталога…", async () => await ExportAsync()));
        top.Children.Add(actions);
        top.Children.Add(new TextBlock { Text = "Перенос включает свои шаблоны. Встроенные карты входят в поставку программы; сохранённые профили содержат независимые копии.", TextWrapping = TextWrapping.Wrap, Classes = { "hint" } });
        var path = new TextBox { Name = "EquipmentLibraryPath", Text = library.StoragePath, IsReadOnly = true };
        top.Children.Add(new Expander { Header = "Место хранения каталога", Content = path, HorizontalAlignment = HorizontalAlignment.Stretch });
        top.Children.Add(_status);
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top); root.Children.Add(_browser);
        void SelectionChanged() { edit.IsEnabled = _browser.Selected is EquipmentTemplate { IsBuiltIn: false }; copy.IsEnabled = _browser.Selected != null; }
        _browser.SelectionChanged += SelectionChanged; SelectionChanged();
        _main = root; _host.Content = root; Content = _host;
    }

    public void Finish() { _cancel?.Invoke(); Finished?.Invoke(); }

    private void ShowForm(Control form, Action cancel)
    {
        _cancel = cancel;
        var page = new DockPanel { Margin = new Thickness(20) };
        var back = Button("Назад в каталог", cancel); back.Name = "BackToEquipmentLibrary";
        back.Margin = new Thickness(0, 0, 0, 12); back.HorizontalAlignment = HorizontalAlignment.Left;
        DockPanel.SetDock(back, Dock.Top); page.Children.Add(back); page.Children.Add(form); _host.Content = page;
    }
    private void Back() { _cancel = null; _host.Content = _main; }
    private void Edit(string? id, DeviceDefinition source)
    {
        if (_cancel != null) return;
        var editor = new EquipmentTemplateEditor(_library, source, id);
        editor.Completed += result =>
        {
            Back();
            if (result == null) return;
            _browser.Reload(result.Id); _status.Text = "Шаблон сохранён в общем каталоге.";
        };
        ShowForm(editor, editor.Cancel);
    }
    internal void PreviewImport(string json)
    {
        if (_cancel != null) return;
        var preview = _library.PreviewImport(json);
        var form = new StackPanel { Spacing = 14, MaxWidth = 780, HorizontalAlignment = HorizontalAlignment.Left };
        form.Children.Add(new TextBlock { Text = "Импорт каталога оборудования", FontSize = 22, FontWeight = FontWeight.SemiBold });
        form.Children.Add(new TextBlock { Name = "EquipmentImportSummary", Text = $"Добавить: {preview.Added}. Обновить: {preview.Updated}. Без изменений: {preview.Unchanged}.", TextWrapping = TextWrapping.Wrap });
        form.Children.Add(new ListBox { ItemsSource = preview.Changes, MaxHeight = 280, MinWidth = 300 });
        form.Children.Add(new TextBlock { Text = "Обновляются записи с теми же идентификаторами. Остальные свои записи сохраняются. Состав оборудования открытого и сохранённых профилей не меняется.", TextWrapping = TextWrapping.Wrap });
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; form.Children.Add(error);
        var apply = Button("Импортировать в каталог", () =>
        {
            try { _library.ApplyImport(preview); Back(); _browser.Reload(); _status.Text = $"Каталог импортирован. Добавлено: {preview.Added}, обновлено: {preview.Updated}."; }
            catch (Exception ex) { error.Text = ex.Message; }
        });
        apply.Name = "ConfirmEquipmentLibraryImport"; form.Children.Add(apply);
        form.Children.Add(Button("Отмена", Back)); ShowForm(form, Back);
    }
    private async Task ImportAsync()
    {
        try
        {
            var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Импорт каталога оборудования", AllowMultiple = false });
            if (files.Count == 0) return;
            await using var stream = await files[0].OpenReadAsync();
            if (stream.Length > 4_000_000) throw new InvalidOperationException("Файл каталога превышает 4 МБ.");
            using var reader = new StreamReader(stream); PreviewImport(await reader.ReadToEndAsync());
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private async Task ExportAsync()
    {
        try
        {
            var json = _library.ExportJson();
            var file = await TopLevel.GetTopLevel(this)!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Экспорт каталога оборудования", SuggestedFileName = "equipment-catalog.json", DefaultExtension = "json" });
            if (file == null) return;
            await using var stream = await file.OpenWriteAsync(); stream.SetLength(0);
            await using var writer = new StreamWriter(stream); await writer.WriteAsync(json);
            _status.Text = "Свои шаблоны экспортированы в каталог JSON.";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 4) };
        button.Click += (_, _) => action(); return button;
    }
}
