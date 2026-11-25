using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Logging;

namespace TestBuilder.ViewModels;

public sealed class LabelTypeItem : ObservableObject
{
    public LabelTypeDefinition Definition { get; private set; }
    public string Name => Definition.Name;
    public string Scope { get; }
    public LabelLayoutEditorViewModel? Draft { get; set; }
    public string Baseline { get; set; } = "";
    public bool IsNew { get; set; }
    public bool NeedsSave { get; set; }
    public string DraftName { get; set; }
    public LabelFieldSourceViewModel[]? Sources { get; set; }
    public string? DraftBaseTypeId { get; set; }
    public LabelTypeItem(LabelTypeDefinition definition, string scope) { Definition = definition.Copy(); DraftName = definition.Name; DraftBaseTypeId = definition.BaseTypeId; Scope = scope; }
    public void Accept(LabelTypeDefinition value) { Definition = value.Copy(); DraftName = value.Name; DraftBaseTypeId = value.BaseTypeId; IsNew = NeedsSave = false; OnPropertyChanged(nameof(Name)); }
}

public partial class LabelTypesViewModel : ObservableObject, IDisposable
{
    private readonly Func<bool> _canUse;
    private readonly ILogger _logger;
    private readonly IRawLabelPrinter _printer;
    private readonly Action _saved;
    private LabelTypeFile _stored = new();
    private readonly List<LabelTypeItem> _items = new();
    private string _folder = "", _revision = "missing";
    private CancellationTokenSource? _printing;
    private bool _disposed;
    public ObservableCollection<LabelTypeItem> Types { get; } = new();
    public ObservableCollection<LabelProductBindingRow> Assignments { get; } = new();
    private readonly List<LabelProductBindingRow> _assignments = new();
    public IReadOnlyList<LabelTypeDefinition> Prototypes { get; } = LabelTypeStore.UniversalDefaults();
    [ObservableProperty] private LabelTypeDefinition? prototype;
    [ObservableProperty] private int selectedSection;
    [ObservableProperty] private string assignmentSearch = "";
    [ObservableProperty] private LabelTypeItem? selected;
    [ObservableProperty] private string search = "";
    [ObservableProperty] private string typeName = "";
    [ObservableProperty] private string printerName = "";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private bool isBusy;
    public LabelLayoutEditorViewModel? Editor => Selected?.Draft;
    public bool HasSelection => Editor != null;
    public bool CanEdit => !_disposed && !IsBusy && _canUse();
    public bool CanSave => CanEdit && HasSelection;
    public bool IsDirty => Selected?.IsNew == true || Selected?.NeedsSave == true || Editor != null && Selected!.Baseline != Fingerprint(TypeName, Editor.Template);
    public bool HasDrafts => HasTypeDrafts || HasAssignmentDrafts;
    private bool HasTypeDrafts => _items.Any(i => i.IsNew || i.NeedsSave || i.Draft != null && i.Baseline != Fingerprint(i.DraftName, i.Draft.Template));
    public bool HasAssignmentDrafts => _assignments.Any(a => a.IsDirty);
    public bool IsTypesSection => SelectedSection == 0;
    public bool IsAssignmentsSection => SelectedSection == 1;
    public bool CanRename => Selected != null && !LabelTypeStore.IsUniversal(Selected.Definition.Id);
    public IReadOnlyList<LabelFieldSourceViewModel> Sources => Selected?.Sources ?? Array.Empty<LabelFieldSourceViewModel>();
    public string Scope => Selected?.Scope ?? "";
    public string StoragePath => LabelTypeStore.LocalPath(_folder.Length > 0 ? _folder : AppSettings.Instance.ResolvedGraphsFolder);
    public event Action? EditorChanged;
    internal Func<bool>? HasInputErrors { get; set; }

    internal LabelTypesViewModel(Func<bool> canUse, ILogger logger, IRawLabelPrinter printer, Action saved)
    { _canUse = canUse; _logger = logger; _printer = printer; _saved = saved; PrinterName = AppSettings.Instance.DefaultPrinterName ?? ""; Prototype = Prototypes[1]; }

    public void EnsureLoaded()
    {
        if (_disposed) return;
        var folder = AppSettings.Instance.ResolvedGraphsFolder;
        if (_folder == folder && _items.Count > 0) return;
        if (HasDrafts) { Status = "Изменилась папка профилей. Сначала экспортируйте или отмените черновики типов этикеток."; return; }
        Load();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Reload()
    {
        if (HasDrafts) { Status = "Есть несохранённые изменения. Сохраните, экспортируйте или отмените их перед обновлением."; return; }
        Load();
    }

    private void Load()
    {
        try
        {
            var folder = AppSettings.Instance.ResolvedGraphsFolder;
            var path = LabelTypeStore.LocalPath(folder);
            var revision = LabelTemplateStore.Revision(path);
            var stored = LabelTypeStore.LoadFile(folder);
            var catalog = EquipmentCatalog.Load(folder);
            if (revision != LabelTemplateStore.Revision(path)) throw new InvalidDataException("Каталог изменён во время чтения. Обновите список.");
            var previous = Selected?.Definition.Id;
            DetachEditor(); Selected = null; _items.Clear();
            _folder = folder; _revision = revision; _stored = stored;
            foreach (var type in LabelTypeStore.UniversalDefaults())
                Add(type, "Универсальный тип. Данные устройства подставляются при печати.");
            foreach (var type in stored.Types.Where(t => !LabelTypeStore.IsUniversal(t.Id) && t.ProductModelId == null))
                _items.Add(new(type, "Свой тип. Назначьте его изделиям на вкладке «Привязки изделий»."));
            LoadAssignments(catalog, stored.Assignments);
            Filter(); Selected = Types.FirstOrDefault(t => t.Definition.Id == previous) ?? Types.FirstOrDefault();
            Status = "Выберите тип или создайте свой. Для выбора типа по модели откройте «Привязки изделий».";
            OnPropertyChanged(nameof(StoragePath));
        }
        catch (Exception ex) when (IsCatalogError(ex)) { Status = "Не удалось открыть типы этикеток: " + ex.Message; }
        finally { RefreshAvailability(); }
    }

    private void Add(LabelTypeDefinition generated, string scope)
    {
        var stored = _stored.Types.FirstOrDefault(t => t.Id == generated.Id);
        var template = LabelTypeStore.UniversalTemplate(generated.Id, stored?.Template ?? generated.Template);
        _items.Add(new(generated with { Template = template }, scope));
    }
    partial void OnSelectedSectionChanged(int value) { OnPropertyChanged(nameof(IsTypesSection)); OnPropertyChanged(nameof(IsAssignmentsSection)); }
    private IReadOnlyList<LabelTypeChoice> TypeChoices() => new[] { new LabelTypeChoice("", "Автоматически по изделию") }
        .Concat(_items.Select(i => new LabelTypeChoice(i.Definition.Id, i.DraftName))).ToArray();
    private void LoadAssignments(EquipmentCatalog catalog, IReadOnlyList<LabelTypeAssignment> assignments)
    {
        _assignments.Clear(); var choices = TypeChoices();
        var models = catalog.Models.Select(m => (m.Id, m.Name)).Concat(assignments
            .Where(a => !catalog.Models.Any(m => m.Id == a.ProductModelId)).Select(a => (a.ProductModelId, a.ProductModelId + " (нет в справочнике)")));
        foreach (var (id, name) in models)
        {
            var row = new LabelProductBindingRow(id, name, assignments.FirstOrDefault(a => string.Equals(a.ProductModelId, id, StringComparison.OrdinalIgnoreCase))?.LabelTypeId ?? "", choices);
            row.Changed += () => OnPropertyChanged(nameof(HasAssignmentDrafts)); _assignments.Add(row);
        }
        FilterAssignments(); OnPropertyChanged(nameof(HasAssignmentDrafts));
    }
    partial void OnAssignmentSearchChanged(string value) => FilterAssignments();
    private void FilterAssignments()
    {
        Assignments.Clear();
        foreach (var row in _assignments.Where(a => a.Name.Contains(AssignmentSearch.Trim(), StringComparison.OrdinalIgnoreCase) || a.ModelId.Contains(AssignmentSearch.Trim(), StringComparison.OrdinalIgnoreCase))) Assignments.Add(row);
    }
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void NewType() => CreateType(Prototype ?? Prototypes[1], false);
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void CopyType()
    {
        try { CreateType(CurrentDefinition(), true); }
        catch (Exception ex) when (IsCatalogError(ex)) { Status = ex.Message; }
    }
    private void CreateType(LabelTypeDefinition source, bool copy)
    {
        try
        {
            CheckFolder(); var id = "custom:" + Guid.NewGuid().ToString("N");
            var template = source.Template.Copy(); template.Id = id;
            var item = new LabelTypeItem(new(id, copy ? source.Name + " — копия" : "Новый тип", null, template,
                source.BaseTypeId ?? (LabelTypeStore.IsUniversal(source.Id) ? source.Id : LabelTypeStore.TypeIdFor(template, null))), "Свой тип. Назначьте его изделиям на вкладке «Привязки изделий».") { IsNew = true };
            _items.Add(item); Search = ""; Filter(); Selected = item; SelectedSection = 0;
            Status = "Задайте название, макет и источники данных. Сохраните тип, затем назначьте его изделиям.";
        }
        catch (Exception ex) when (IsCatalogError(ex)) { Status = ex.Message; }
    }
    partial void OnSearchChanged(string value) => Filter();
    private void Filter()
    {
        var selected = Selected;
        Types.Clear();
        foreach (var item in _items.Where(i => i.Name.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase) || i.Scope.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase))) Types.Add(item);
        Selected = selected != null && Types.Contains(selected) ? selected : Types.FirstOrDefault();
    }
    partial void OnSelectedChanging(LabelTypeItem? value) => DetachEditor();
    partial void OnSelectedChanged(LabelTypeItem? value)
    {
        if (value != null)
        {
            try
            {
                value.Draft ??= CreateEditor(value.Definition.Template);
                value.Sources ??= LabelDataBindings.Fields.Select(field =>
                {
                    var row = new LabelFieldSourceViewModel(field, (value.Draft.Template.DataBindings ?? new()).Get(field));
                    row.Changed += () =>
                    {
                        value.Draft!.Template.DataBindings = (value.Draft.Template.DataBindings ?? new()).With(field, row.Binding);
                        value.Draft.Template.Name = "название устройства";
                        value.Draft.Template.DeviceCode = 0; value.Draft.Template.BarcodePrefix = "000";
                        value.Draft.Template.CabinetName = (value.Definition.BaseTypeId ?? value.Definition.Id) == LabelTypeStore.CabinetTypeId ? "Шкаф 1" : null;
                        try { value.Draft.Template.DataBindings.ApplyPresentation(value.Draft.Template); value.Draft.Cabinet = value.Draft.Template.CabinetName ?? ""; }
                        catch (Exception ex) when (IsCatalogError(ex)) { Status = ex.Message; }
                        value.Draft.Refresh(); OnPropertyChanged(nameof(IsDirty));
                    };
                    return row;
                }).ToArray();
                if (value.Baseline.Length == 0) value.Baseline = Fingerprint(value.Name, value.Draft.Template);
                TypeName = value.DraftName;
                value.Draft.Changed += OnDraftChanged;
            }
            catch (Exception ex) when (IsCatalogError(ex)) { Status = "Этот макет нельзя открыть визуально: " + ex.Message; }
        }
        foreach (var name in new[] { nameof(Editor), nameof(HasSelection), nameof(Scope), nameof(IsDirty), nameof(CanSave), nameof(CanRename), nameof(Sources) }) OnPropertyChanged(name);
        NotifyQuickFields(); SaveCommand.NotifyCanExecuteChanged(); CopyTypeCommand.NotifyCanExecuteChanged(); EditorChanged?.Invoke();
    }
    private static LabelLayoutEditorViewModel CreateEditor(LabelTemplateDefinition template)
    {
        var editor = new LabelLayoutEditorViewModel(template);
        editor.Serial = "00000"; editor.Mac = "00:00:00:00:00:00";
        return editor;
    }
    private void DetachEditor() { if (Editor != null) Editor.Changed -= OnDraftChanged; }
    partial void OnTypeNameChanged(string value) { if (Selected != null) Selected.DraftName = value; OnPropertyChanged(nameof(IsDirty)); }
    private void OnDraftChanged() { OnPropertyChanged(nameof(IsDirty)); NotifyQuickFields(); }
    private static string Fingerprint(string name, LabelTemplateDefinition template) => name + "\n" + JsonSerializer.Serialize(template, EquipmentCatalog.JsonOptions);

    // These operate on the complete layout, rather than the currently selected element.
    private decimal Mm(int dots) => Math.Round(dots * 25.4m / Editor!.Dpi, 3);
    private int Dots(decimal value) => checked((int)Math.Round(value * Editor!.Dpi / 25.4m, MidpointRounding.AwayFromZero));
    public decimal? TopMargin
    {
        get => Editor?.Elements.Count > 0 ? Mm(Editor.Elements.Min(e => e.Y)) : null;
        set { if (Editor?.Elements.Count > 0 && value.HasValue && TopMargin != value) { var delta = Dots(value.Value) - Editor.Elements.Min(e => e.Y); foreach (var e in Editor.Elements) e.Y += delta; Editor.RefreshFields(); } }
    }
    private LabelLayoutElement? Barcode => Editor?.Elements.FirstOrDefault(e => e.Kind == "Barcode" && e.Visible);
    public bool HasBarcode => Barcode != null;
    public decimal? BarcodeHeight { get => Barcode is { } bar ? Mm(bar.Height) : null; set { if (Barcode is { } bar && value.HasValue && BarcodeHeight != value) { bar.Height = Dots(value.Value); Editor!.RefreshFields(); } } }
    public decimal? BarcodeModule { get => Barcode?.Module; set { if (Barcode is { } bar && value.HasValue && value == decimal.Truncate(value.Value) && BarcodeModule != value) { bar.Module = checked((int)value.Value); Editor!.RefreshFields(); } } }
    private void NotifyQuickFields() { foreach (var name in new[] { nameof(TopMargin), nameof(BarcodeHeight), nameof(BarcodeModule), nameof(HasBarcode) }) OnPropertyChanged(name); }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        try
        {
            CheckFolder();
            var type = CurrentDefinition();
            var merged = LabelTypeStore.Merge(_stored, new() { Types = new() { type } });
            LabelTypeStore.Save(StoragePath, merged, _revision);
            _stored = merged; _revision = LabelTemplateStore.Revision(StoragePath);
            Selected!.Accept(type); Selected.Baseline = Fingerprint(TypeName, Editor!.Template);
            OnPropertyChanged(nameof(IsDirty)); _saved();
            if (!HasDrafts) Load();
            else foreach (var row in _assignments) row.UpdateChoices(TypeChoices());
            Status = LabelTypeStore.IsUniversal(type.Id) ? "Тип сохранён. Новая печать использует его при автоматическом выборе."
                : "Тип сохранён. Назначьте его нужным моделям на вкладке «Привязки изделий».";
        }
        catch (Exception ex) when (IsCatalogError(ex)) { Status = ex.Message; }
    }
    private LabelTypeDefinition CurrentDefinition()
    {
        if (HasInputErrors?.Invoke() == true) throw new InvalidOperationException("Исправьте значения в полях этикетки.");
        if (string.IsNullOrWhiteSpace(TypeName)) throw new InvalidOperationException("Введите название типа этикетки.");
        return Selected!.Definition with { Name = TypeName.Trim(), Template = Editor!.Apply(), BaseTypeId = Selected.DraftBaseTypeId };
    }
    private void CheckFolder()
    {
        if (_folder != AppSettings.Instance.ResolvedGraphsFolder) throw new InvalidOperationException("Папка профилей изменилась. Экспортируйте черновик и обновите список типов.");
    }
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void DefaultLayout()
    {
        var item = Selected!;
        var defaults = LabelTypeStore.UniversalTemplate(item.DraftBaseTypeId ?? (LabelTypeStore.IsUniversal(item.Definition.Id) ? item.Definition.Id : LabelTypeStore.TypeIdFor(item.Definition.Template, null)));
        var template = Editor!.Template.Copy();
        template.Layout = defaults.Copy().Layout;
        template.Command = defaults.Command;
        template.WidthDots = defaults.WidthDots; template.HeightDots = defaults.HeightDots;
        template.OriginXDots = defaults.OriginXDots; template.PrinterDpi = defaults.PrinterDpi;
        template.Language = defaults.Language;
        template.BarcodeModuleDots = defaults.BarcodeModuleDots;
        template.BarcodeTopDots = defaults.BarcodeTopDots; template.BarcodeHeightDots = defaults.BarcodeHeightDots;
        template.NameFontMaxWidthDots = defaults.NameFontMaxWidthDots; template.NameCharacterWidthFactor = defaults.NameCharacterWidthFactor;
        DetachEditor(); item.Draft = CreateEditor(template); Selected = null; Selected = item;
        Status = "Макет по умолчанию восстановлен в черновике. Проверьте образец и нажмите «Сохранить тип».";
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Discard()
    {
        var item = Selected!;
        DetachEditor(); item.Draft = null; item.Sources = null; item.Baseline = ""; item.DraftName = item.Name; item.DraftBaseTypeId = item.Definition.BaseTypeId; item.NeedsSave = false;
        if (item.IsNew) { _items.Remove(item); Filter(); foreach (var row in _assignments) row.UpdateChoices(TypeChoices()); }
        else { Selected = null; Selected = item; }
        Status = "Несохранённые изменения выбранного типа отменены.";
    }
    public string Export()
    {
        CheckFolder();
        if (HasInputErrors?.Invoke() == true) throw new InvalidOperationException("Исправьте значения в полях этикетки перед экспортом.");
        var types = new List<LabelTypeDefinition>();
        foreach (var item in _items)
        {
            var name = item.DraftName.Trim();
            types.Add(item.Definition with { Name = name, Template = item.Draft?.Apply() ?? item.Definition.Template.Copy(), BaseTypeId = item.DraftBaseTypeId });
        }
        var file = new LabelTypeFile { Types = types, Assignments = DraftAssignments() }; LabelTypeStore.Validate(file);
        return JsonSerializer.Serialize(file, EquipmentCatalog.JsonOptions);
    }
    public void Import(string json)
    {
        if (!CanEdit) return;
        try
        {
            CheckFolder();
            if (HasDrafts) throw new InvalidOperationException("Перед импортом сохраните или отмените свои черновики.");
            if (System.Text.Encoding.UTF8.GetByteCount(json) > 4_000_000) throw new InvalidDataException("Каталог типов этикеток больше 4 МБ.");
            var incoming = JsonSerializer.Deserialize<LabelTypeFile>(json, EquipmentCatalog.JsonOptions) ?? throw new InvalidDataException("Пустой файл.");
            LabelTypeStore.Validate(incoming);
            var incomingTypes = incoming.Types.Where(t => t.ProductModelId == null).ToArray();
            if (incomingTypes.Length == 0 && incoming.Assignments.Count == 0) throw new InvalidDataException("В файле нет общих типов этикеток и привязок изделий.");
            var drafts = incomingTypes.Select(type => (Type: type, Editor: CreateEditor(LabelTypeStore.IsUniversal(type.Id) ? LabelTypeStore.UniversalTemplate(type.Id, type.Template) : type.Template))).ToArray();
            DetachEditor(); Selected = null;
            foreach (var draft in drafts)
            {
                var type = draft.Type;
                var item = _items.FirstOrDefault(i => i.Definition.Id == type.Id || type.ProductModelId != null && i.Definition.ProductModelId == type.ProductModelId);
                if (item == null) { item = new(type, "Свой тип. Назначьте его изделиям на вкладке «Привязки изделий».") { IsNew = true }; _items.Add(item); }
                item.Draft = draft.Editor; item.Sources = null; item.DraftName = LabelTypeStore.IsUniversal(type.Id) ? item.Name : type.Name; item.DraftBaseTypeId = type.BaseTypeId; item.NeedsSave = true;
            }
            var assignments = _stored.Assignments.Where(a => incoming.Assignments.All(b => !string.Equals(a.ProductModelId, b.ProductModelId, StringComparison.OrdinalIgnoreCase))).Concat(incoming.Assignments).ToArray();
            LoadAssignments(EquipmentCatalog.Load(_folder), assignments);
            foreach (var row in _assignments) row.SavedTypeId = _stored.Assignments.FirstOrDefault(a => string.Equals(a.ProductModelId, row.ModelId, StringComparison.OrdinalIgnoreCase))?.LabelTypeId ?? "";
            OnPropertyChanged(nameof(HasAssignmentDrafts));
            Selected = null; Search = ""; Filter();
            Selected = _items.FirstOrDefault(i => incoming.Types.Any(t => t.Id == i.Definition.Id));
            Status = $"Импортировано в черновик: {incomingTypes.Length} типов; привязок в файле: {incoming.Assignments.Count}. Сначала сохраните типы, затем «Сохранить привязки».";
        }
        catch (Exception ex) when (IsCatalogError(ex)) { Status = ex.Message; }
    }

    private List<LabelTypeAssignment> DraftAssignments() => _assignments.Where(a => a.Selected.Id.Length > 0 || _stored.Assignments.Any(s => s.ProductModelId == a.ModelId))
        .Select(a => new LabelTypeAssignment(a.ModelId, a.Selected.Id)).ToList();
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void SaveAssignments()
    {
        try
        {
            CheckFolder();
            var file = new LabelTypeFile { Types = _stored.Types, Assignments = DraftAssignments() };
            LabelTypeStore.Validate(file); LabelTypeStore.Save(StoragePath, file, _revision);
            _stored = file; _revision = LabelTemplateStore.Revision(StoragePath);
            foreach (var row in _assignments) row.SavedTypeId = row.Selected.Id;
            OnPropertyChanged(nameof(HasAssignmentDrafts)); _saved(); Status = "Привязки сохранены. Они применяются к новой печати из профилей и ручному выбору изделия.";
        }
        catch (Exception ex) when (IsCatalogError(ex)) { Status = ex.Message; }
    }
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void DiscardAssignments()
    {
        foreach (var row in _assignments) row.Selected = row.Choices.Single(c => c.Id == row.SavedTypeId);
        OnPropertyChanged(nameof(HasAssignmentDrafts)); Status = "Изменения привязок отменены.";
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task PrintSample()
    {
        try
        {
            var template = CurrentDefinition().Template; template.CabinetName = Editor!.Cabinet;
            if (string.IsNullOrWhiteSpace(PrinterName)) throw new InvalidOperationException("Выберите принтер для пробной печати.");
            _printing = new(); IsBusy = true; Status = "Пробная печать…";
            var context = new TestContext(new RegisterState());
            var step = new PrintLabelStep(_logger, PrinterName, "SerialNumber", "SerialShort", "Mac", true,
                Editor.Serial, Editor.Mac, 1, template.UseShortSerial, true, _printer, 30000, template: template, preserveTemplate: true);
            var result = await step.ExecuteAsync(context, _printing.Token);
            Status = result == StepResult.False ? context.GetVariable<string>("PrintLabel.Error") ?? "Ошибка печати." : "Одна этикетка передана принтеру. Проверьте её положение и считывание сканером.";
        }
        catch (OperationCanceledException) { Status = "Пробная печать отменена."; }
        catch (Exception ex) when (IsCatalogError(ex)) { Status = ex.Message; }
        finally { _printing?.Dispose(); _printing = null; IsBusy = false; }
    }
    partial void OnIsBusyChanged(bool value) => RefreshAvailability();
    public void RefreshAvailability()
    {
        OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged(); CopyTypeCommand.NotifyCanExecuteChanged(); NewTypeCommand.NotifyCanExecuteChanged();
        SaveAssignmentsCommand.NotifyCanExecuteChanged(); DiscardAssignmentsCommand.NotifyCanExecuteChanged();
        DefaultLayoutCommand.NotifyCanExecuteChanged(); DiscardCommand.NotifyCanExecuteChanged(); PrintSampleCommand.NotifyCanExecuteChanged(); ReloadCommand.NotifyCanExecuteChanged();
    }
    internal static bool IsCatalogError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or OverflowException;
    public void Dispose() { _disposed = true; _printing?.Cancel(); DetachEditor(); }
}
