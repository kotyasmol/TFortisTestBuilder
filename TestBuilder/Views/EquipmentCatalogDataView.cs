using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using TestBuilder.Services;

namespace TestBuilder.Views;

internal sealed class EquipmentCatalogDataView : UserControl
{
    private readonly TextBox _search = new() { Name = "CatalogDataSearch", Watermark = "Модель, код, номер, MAC, шкаф или серверный тип" };
    private readonly DataGrid _models = CreateGrid("CatalogDataModels");
    private readonly DataGrid _details = CreateGrid("CatalogDataDetails");
    private readonly TextBox _json = new() { Name = "CatalogDataJson", IsReadOnly = true, AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap, FontFamily = FontFamily.Parse("monospace") };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { Name = "CatalogDataStatus", TextWrapping = TextWrapping.Wrap };
    private readonly SelectableTextBlock _paths = new() { Name = "CatalogDataPaths", FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private CatalogRow[] _rows = Array.Empty<CatalogRow>();
    private readonly Func<bool> _canEdit;
    private readonly Button _edit = new() { Content = "Редактировать изделие…", Name = "EditCatalogDataModel" };
    public event EventHandler? ReloadRequested;
    public event Action<EquipmentModel>? EditRequested;

    public EquipmentCatalogDataView(string localPath, Func<bool> canEdit)
    {
        _canEdit = canEdit;
        _models.ColumnHeaderHeight = 48;
        Column(_models, "Изделие", nameof(CatalogRow.Name), 230);
        Column(_models, "Код этикетки", nameof(CatalogRow.LabelCode), 105);
        Column(_models, "Тип на сервере", nameof(CatalogRow.ServerType), 220);
        Column(_models, "Первый номер", nameof(CatalogRow.First), 125);
        Column(_models, "Последний номер", nameof(CatalogRow.Last), 140);
        Column(_models, "База короткого номера", nameof(CatalogRow.Offset), 170);
        Column(_models, "Префикс MAC", nameof(CatalogRow.Mac), 160);
        Column(_models, "Источник", nameof(CatalogRow.Source), 220);
        Column(_details, "Поле", nameof(CatalogField.Name), 300);
        _details.Columns.Add(new DataGridTemplateColumn { Header = "Значение",
            CellTemplate = new FuncDataTemplate<CatalogField>((field, _) => new SelectableTextBlock
                { Text = field?.Value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4) }),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 300 });
        var reload = new Button { Content = "Обновить", Name = "ReloadCatalogData" };
        reload.Click += (_, _) => ReloadRequested?.Invoke(this, EventArgs.Empty);
        _edit.Click += (_, _) => { if (_canEdit() && _models.SelectedItem is CatalogRow row) EditRequested?.Invoke(row.Model); };
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,Auto,8,Auto") };
        toolbar.Children.Add(_search);
        Grid.SetColumn(reload, 2);
        toolbar.Children.Add(reload);
        Grid.SetColumn(_edit, 4); toolbar.Children.Add(_edit);
        var header = new StackPanel { Spacing = 8, Children = { toolbar, _summary, _status } };
        var detailsTabs = new TabControl { Items =
        {
            new TabItem { Header = "Данные изделия", Content = _details },
            new TabItem { Header = "JSON всего справочника", Content = _json }
        } };
        UpdatePaths(localPath);
        var root = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,*,8,*,Auto") };
        root.Children.Add(header);
        Grid.SetRow(_models, 1); root.Children.Add(_models);
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Gray, Opacity = .3 };
        Grid.SetRow(splitter, 2); root.Children.Add(splitter);
        Grid.SetRow(detailsTabs, 3); root.Children.Add(detailsTabs);
        Grid.SetRow(_paths, 4); _paths.Margin = new Thickness(0, 8, 0, 0); root.Children.Add(_paths);
        Content = root;
        _models.SelectionChanged += (_, _) =>
        {
            _details.ItemsSource = (_models.SelectedItem as CatalogRow)?.Fields;
            RefreshAvailability();
        };
        _search.TextChanged += (_, _) => Filter();
    }

    internal void Refresh(EquipmentCatalog catalog, string status, string localPath)
    {
        UpdatePaths(localPath);
        var selected = (_models.SelectedItem as CatalogRow)?.Model.Id;
        _rows = catalog.Models.OrderBy(model => EquipmentCatalog.SortKey(model.Name), StringComparer.Ordinal)
            .Select(model => new CatalogRow(model, catalog.Sources.GetValueOrDefault(model.Id, "Текущая память"))).ToArray();
        _json.Text = catalog.ToJson();
        _status.Text = string.IsNullOrWhiteSpace(status) ? "" : status + " Показаны последние прочитанные данные.";
        _status.IsVisible = _status.Text.Length > 0;
        RevisionText = $"Версия поставки: {Version(catalog.BundledRevision)} · Локальная версия: {Version(catalog.LocalRevision)}";
        Filter(selected);
    }

    private string RevisionText { get; set; } = "";
    private void UpdatePaths(string localPath) => _paths.Text = $"Поставка: {EquipmentCatalog.BundledPath}\nЛокальные изменения: {localPath}";
    internal void RefreshAvailability() => _edit.IsEnabled = _models.SelectedItem != null && _canEdit();
    private static string Version(string revision) => string.IsNullOrWhiteSpace(revision) ? "не указана" : revision;

    private void Filter(string? selected = null)
    {
        selected ??= (_models.SelectedItem as CatalogRow)?.Model.Id;
        var query = (_search.Text ?? "").Trim();
        var visible = _rows.Where(row => EquipmentCatalog.Matches(row.Model, query) ||
            row.Fields.Any(field => field.Value.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        _models.ItemsSource = visible;
        _models.SelectedItem = visible.FirstOrDefault(row => row.Model.Id == selected) ?? visible.FirstOrDefault();
        _details.ItemsSource = (_models.SelectedItem as CatalogRow)?.Fields;
        RefreshAvailability();
        _summary.Text = $"Изделий: {visible.Length} из {_rows.Length}. {RevisionText}";
    }

    private static DataGrid CreateGrid(string name) => new() { Name = name, IsReadOnly = true, AutoGenerateColumns = false,
        CanUserResizeColumns = true, SelectionMode = DataGridSelectionMode.Single, Margin = new Thickness(0, 8, 0, 0) };
    private static void Column(DataGrid grid, string caption, string property, int width) => grid.Columns.Add(
        new DataGridTextColumn { Header = new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap, MaxWidth = width - 30 },
            Binding = new Binding(property), Width = new DataGridLength(width) });

    internal sealed record CatalogField(string Name, string Value);
    internal sealed class CatalogRow
    {
        public EquipmentModel Model { get; }
        public string Name => Model.Name;
        public string LabelCode => Model.LabelCode?.ToString("D3", CultureInfo.InvariantCulture) ?? "Не задан";
        public string ServerType => Model.EffectiveServerDeviceType ?? "Не задан";
        public string First => Number(Model.SerialSeries?.FirstSerialNumber);
        public string Last => Number(Model.SerialSeries?.LastSerialNumber);
        public string Offset => Number(Model.SerialSeries?.SerialOffset);
        public string Mac => Model.SerialSeries?.MacPrefix ?? "Не задан";
        public string Source { get; }
        public IReadOnlyList<CatalogField> Fields { get; }

        public CatalogRow(EquipmentModel model, string source)
        {
            Model = model; Source = source;
            var fields = new List<CatalogField>
            {
                new("ID изделия", model.Id), new("Название", Name),
                new("Альтернативные названия", string.Join("; ", model.Aliases ?? Array.Empty<string>())),
                new("Источник", source), new("Код модели на этикетке", LabelCode),
                new("Полная локальная карточка", model.IsFullOverride ? "Да" : "Нет"),
                new("Тип на сервере", ServerType), new("Стартовый серийный номер", First),
                new("Последний серийный номер", Last), new("База короткого номера", Offset),
                new("Номер для аварийного отчёта", Number(model.SerialSeries?.FailureReportSerialNumber)),
                new("Префикс MAC", Mac), new("Имя платы при записи Pro MAC", model.SerialSeries?.BoardVersion ?? "Не задано"),
                new("Формат отчёта RTL", model.RtlReport == null ? "Не используется" : "Используется")
            };
            if (model.RtlReport is { } rtl)
            {
                fields.Add(new("Числовой тип отчёта RTL", Number(rtl.DeviceType)));
                fields.Add(new("Переменная идентификатора платы", rtl.DeviceIdentifierVariable));
                fields.Add(new("Служебный идентификатор отказов RTL", rtl.FailureDeviceIdentifier ?? "Не задан"));
                fields.Add(new("Получать номер из ответа на отчёт", rtl.AssignSerialOnReport ? "Да" : "Нет"));
                Extra(fields, "Дополнительное поле RTL", rtl.ExtensionData);
            }
            foreach (var code in (model.LabelVariants?.Keys ?? Enumerable.Empty<int>()).Union(model.LabelCabinets?.Keys ?? Enumerable.Empty<int>()).OrderBy(code => code))
            {
                fields.Add(new($"Комплектация {code}", model.LabelVariants?.GetValueOrDefault(code) ?? "Не задана"));
                fields.Add(new($"Шкаф комплектации {code}", model.LabelCabinets?.GetValueOrDefault(code) ?? "Не печатается"));
            }
            Extra(fields, "Дополнительное поле изделия", model.ExtensionData);
            Extra(fields, "Дополнительное поле серии", model.SerialSeries?.ExtensionData);
            Fields = fields;
        }

        private static string Number(int? number) => number?.ToString(CultureInfo.InvariantCulture) ?? "Не задан";
        private static void Extra(List<CatalogField> fields, string prefix, Dictionary<string, JsonElement>? extra)
        {
            if (extra == null) return;
            fields.AddRange(extra.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
                new CatalogField($"{prefix}: {pair.Key}", pair.Value.ToString())));
        }
    }
}
