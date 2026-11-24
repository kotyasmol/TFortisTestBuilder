using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using TestBuilder.Domain.Modbus;
using TestBuilder.Presentation;
using TestBuilder.Services;

namespace TestBuilder.Views;

internal sealed class EquipmentTemplateBrowser : UserControl
{
    private readonly EquipmentLibrary library;
    private readonly ListBox _catalogList = new() { Name = "EquipmentCatalogList" };
    private readonly TextBox _catalogSearch = new() { Name = "EquipmentCatalogSearch", Watermark = "Найти оборудование по названию" };
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private EquipmentTemplate[] catalog = Array.Empty<EquipmentTemplate>();
    public EquipmentTemplate? Selected => _catalogList.SelectedItem as EquipmentTemplate;
    public event Action? SelectionChanged;

    public EquipmentTemplateBrowser(EquipmentLibrary library)
    {
        this.library = library;
        var root = new DockPanel();
        var top = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(_catalogSearch); top.Children.Add(error);
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        _catalogList.ItemTemplate = new FuncDataTemplate<EquipmentTemplate>((entry, _) =>
        {
            var row = new StackPanel { Spacing = 4, Margin = new Thickness(8) };
            row.Children.Add(new TextBlock { Text = entry?.Display, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(new TextBlock { Text = entry?.Details, TextWrapping = TextWrapping.Wrap }); return row;
        });
        _catalogList.SelectionChanged += (_, _) => { ShowCatalogPreview(); SelectionChanged?.Invoke(); };
        _catalogSearch.TextChanged += (_, _) => FilterCatalog();
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("230,16,*") };
        body.Children.Add(_catalogList);
        var preview = CreateCatalogPreview(); Grid.SetColumn(preview, 2); body.Children.Add(preview);
        root.Children.Add(body); Content = root; Reload();
    }

    public void Reload(string? selected = null)
    {
        try
        {
            catalog = library.Load().ToArray(); _catalogSearch.Text = ""; FilterCatalog();
            if (selected != null) _catalogList.SelectedItem = catalog.FirstOrDefault(e => e.Id == selected);
            error.Text = ""; error.IsVisible = false; ShowCatalogPreview();
        }
        catch (Exception ex) { error.Text = "Не удалось прочитать каталог: " + ex.Message; error.IsVisible = true; }
    }
    private void FilterCatalog()
    {
        var id = Selected?.Id;
        var entries = catalog.Where(e => e.Device.Name.Contains(_catalogSearch.Text?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
        _catalogList.ItemsSource = entries;
        _catalogList.SelectedItem = entries.FirstOrDefault(e => e.Id == id) ?? entries.FirstOrDefault();
    }

    private readonly TextBlock _catalogPreviewTitle = new() { Name = "CatalogPreviewTitle", FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _catalogPreviewDetails = new() { Name = "CatalogPreviewDetails", TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _catalogRegisterSearch = new() { Name = "CatalogRegisterSearch", Watermark = "Найти регистр по адресу или названию" };
    private readonly TextBlock _catalogRegisterCount = new() { Name = "CatalogRegisterCount", Classes = { "hint" } };
    private readonly DataGrid _catalogRegisters = new() { Name = "CatalogPreviewRegisters", AutoGenerateColumns = false, IsReadOnly = true };
    private CatalogRegisterPreview[] _catalogRegisterRows = Array.Empty<CatalogRegisterPreview>();
    private string? _catalogPreviewId;

    private Control CreateCatalogPreview()
    {
        var preview = new DockPanel();
        var top = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(_catalogPreviewTitle); top.Children.Add(_catalogPreviewDetails);
        top.Children.Add(_catalogRegisterSearch); top.Children.Add(_catalogRegisterCount);
        var allFields = new CheckBox { Name = "ShowAllCatalogRegisterFields", Content = "Все поля" };
        top.Children.Add(allFields);
        DockPanel.SetDock(top, Dock.Top); preview.Children.Add(top);
        foreach (var (label, property, width) in new[] {
            ("Адрес", "Address", 100), ("Название", "Name", 200), ("Доступ", "Access", 90),
            ("Единица", "Unit", 110), ("Тип регистра", "Area", 155), ("Категория", "Category", 120), ("Повтор", "Repeat", 110) })
        {
            DataGridColumn column = property == "Name" ? new TranslatedNameColumn { WrapText = true, Binding = new Binding(property) { Mode = BindingMode.OneWay } } :
                new DataGridTemplateColumn { CellTemplate = new FuncDataTemplate<CatalogRegisterPreview>((_, _) =>
                {
                    var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 6), VerticalAlignment = VerticalAlignment.Center };
                    text.Bind(TextBlock.TextProperty, new Binding(property) { Mode = BindingMode.OneWay }); return text;
                }) };
            column.Header = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, MaxWidth = width - 24 };
            column.IsReadOnly = true; column.MinWidth = width;
            if (property is "Area" or "Category" or "Repeat") column.IsVisible = false;
            column.Width = property == "Name" ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width);
            if (property == "Repeat")
            {
                var header = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, MaxWidth = width - 24 };
                ToolTip.SetTip(header, "Повтор записи состояния при несовпадении контрольного чтения."); column.Header = header;
            }
            _catalogRegisters.Columns.Add(column);
        }
        allFields.IsCheckedChanged += (_, _) =>
        {
            foreach (var index in new[] { 4, 5, 6 }) _catalogRegisters.Columns[index].IsVisible = allFields.IsChecked == true;
        };
        _catalogRegisterSearch.TextChanged += (_, _) => FilterCatalogRegisters();
        preview.Children.Add(_catalogRegisters); return preview;
    }

    private void ShowCatalogPreview()
    {
        var entry = _catalogList.SelectedItem as EquipmentTemplate;
        var changed = entry?.Id != _catalogPreviewId; _catalogPreviewId = entry?.Id;
        // Project onto immutable display rows: browsing can never edit a template or profile.
        _catalogRegisterRows = entry?.Device.Registers.Select(register => new CatalogRegisterPreview(register)).ToArray() ?? Array.Empty<CatalogRegisterPreview>();
        _catalogPreviewTitle.Text = entry?.Device.Name ?? "Оборудование не выбрано";
        _catalogPreviewDetails.Text = entry == null ? "Выберите оборудование в списке слева." :
            (entry.IsBuiltIn ? "Встроенный шаблон" : "Свой шаблон") + " · " +
            (entry.Device.ExpectedIdentificationValues.Count == 0 ? "Определение типа: допустимые значения не заданы." :
                $"Регистр типа (хранения): {entry.Device.IdentificationAddress}; значения: {string.Join(", ", entry.Device.ExpectedIdentificationValues)}.") +
            (entry.Device.StandSerialNumberAddress is { } serial ? $"{Environment.NewLine}Регистр серийного номера стенда (хранения): {serial}." : "");
        _catalogRegisterSearch.IsEnabled = entry != null;
        if (changed) _catalogRegisterSearch.Text = "";
        FilterCatalogRegisters();
    }

    private void FilterCatalogRegisters()
    {
        var query = _catalogRegisterSearch.Text?.Trim() ?? "";
        var rows = _catalogRegisterRows.Where(r => query.Length == 0 ||
            string.Join(" ", r.Address, r.Name, RussianUiText.Display(r.Name), r.Area, r.Access, r.Unit, r.Category)
                .Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        _catalogRegisters.ItemsSource = rows;
        _catalogRegisterCount.Text = query.Length > 0 && rows.Length == 0 ? $"Регистры не найдены · всего: {_catalogRegisterRows.Length}" :
            $"Показано регистров: {rows.Length} из {_catalogRegisterRows.Length} · только просмотр";
    }

    internal sealed record CatalogRegisterPreview(int Address, string Name, string Area, string Access, string Unit, string Category, string Repeat)
    {
        public CatalogRegisterPreview(RegisterDefinition register) : this(register.Address, register.Name, RussianUiText.Display(register.Area),
            register.Writable ? "RW · запись" : "RO · чтение", register.Unit, RussianUiText.Display(register.Category), register.RetryWriteOnMismatch ? "Да" : "Нет") { }
    }
}
