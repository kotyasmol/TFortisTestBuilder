using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;

namespace TestBuilder.ViewModels;

public partial class LabelPrintingViewModel : ViewModelBase, IDisposable
{
    public LabelTypesViewModel Types { get; }
    [ObservableProperty] private int selectedSection;
    partial void OnSelectedSectionChanged(int value) { if (value == 1) Types.EnsureLoaded(); }
    public ObservableCollection<LabelTemplatePreviewItem> LabelOptions { get; } = new();
    public ObservableCollection<string> ProductNames { get; } = new();
    [ObservableProperty] private IReadOnlyList<TestLabelOption> testLabelOptions = Array.Empty<TestLabelOption>();
    [ObservableProperty] private TestLabelOption? selectedTestLabel;
    [ObservableProperty] private string? selectedProductName;
    private readonly Func<IReadOnlyList<GraphProfile>>? _profiles;
    private List<LabelTemplateDefinition> _workstationTemplates = new();
    private LabelTypeDefinition[] _universalTypes = LabelTypeStore.UniversalDefaults();
    private LabelTypeFile _typeFile = new();
    private IReadOnlyList<LabelTypeDefinition> _savedUniversalTypes = Array.Empty<LabelTypeDefinition>();
    private readonly Action? _refreshProfiles;
    private bool _refreshingProducts;
    private string? _lookupProductName;
    public bool HasProducts => ProductNames.Count > 0;
    public bool ShowProductPicker => _profiles != null;
    private LastTestLabelSnapshot? _lastTest;
    [ObservableProperty] private LabelPreview? lastTestPreview;
    public bool HasLastTest => _lastTest != null;
    public bool HasLastTestLabel => _lastTest?.Label != null;
    public bool CanPrintLastTest => CanEdit && HasLastTestLabel;
    public string LastTestDescription => _lastTest == null ? "Завершённых тестов пока нет." :
        (_lastTest.Label is { } label ? $"{label.Template.Name} · SN {label.Serial}" +
            (label.Mac.Length == 0 ? "" : $" · MAC {label.Mac}") : _lastTest.DeviceModel);
    public string LastTestResult => _lastTest == null ? "" :
        $"{_lastTest.FinishedAt.ToLocalTime():dd.MM.yyyy HH:mm} · {_lastTest.ProfileName} · " +
        (_lastTest.Result switch { ExecutionStatus.Completed => "Тест завершён", ExecutionStatus.Cancelled => "Тест остановлен", _ => "Тест завершён с ошибкой" });
    public string LastTestMessage => _lastTest == null ? "" :
        _lastTest.Error + (HasLastTestLabel ? "" : " Данные этикетки в этом тесте не были подготовлены.");
    [ObservableProperty] private LabelPreview? preview;
    public string BarcodeData => Preview == null ? "" : string.Join("\n", Preview.Elements.Where(e => e.Bars.Length > 0).Select(e => "Данные штрихкода: " + e.EncodedData));
    partial void OnPreviewChanged(LabelPreview? value) => OnPropertyChanged(nameof(BarcodeData));
    [ObservableProperty] private LabelPreview? lastPreview;
    public bool HasLastPreview => LastPreview != null;
    [ObservableProperty] private int selectedType;
    [ObservableProperty] private string serialNumber = "";
    [ObservableProperty] private string macAddress = "";
    [ObservableProperty] private string printerName = "";
    [ObservableProperty] private int copies = 1;
    [ObservableProperty] private string status = "Введите данные этикетки или получите их с сервера.";
    [ObservableProperty] private string lastLabelDescription = "В этой вкладке ещё не печатали этикетки.";
    [ObservableProperty] private bool isBusy;
    private readonly ILogger _logger;
    private readonly IHttpRequestService _http;
    private readonly IRawLabelPrinter _printer;
    private readonly Func<bool> _canUse;
    private readonly Func<string?> _deviceModel;
    public string ProfileDeviceName => SelectedProductName ?? (_profiles == null ? _deviceModel() : null) ?? "";
    private CancellationTokenSource? _operation;
    private LabelData? _last;
    private LabelTemplateDefinition? _lastTemplate;
    private bool _disposed;
    public bool CanEdit => !IsBusy && Types?.IsBusy != true && !_disposed && _canUse();
    public bool CanRepeat => CanEdit && _last != null;
    public bool HasMac => SelectedTemplate?.RequiresMac == true;
    partial void OnSelectedTypeChanged(int value) { OnPropertyChanged(nameof(HasMac)); UpdatePreview(); }
    partial void OnSerialNumberChanged(string value) => UpdatePreview();
    partial void OnMacAddressChanged(string value) => UpdatePreview();
    partial void OnLastPreviewChanged(LabelPreview? value) => OnPropertyChanged(nameof(HasLastPreview));
    partial void OnIsBusyChanged(bool value) => RefreshAvailability();
    partial void OnSelectedProductNameChanged(string? value)
    {
        if (!_refreshingProducts) RefreshTemplates();
    }

    public LabelPrintingViewModel(ILogger logger, Func<bool> canUse, Func<string?>? deviceModel = null,
        Func<IReadOnlyList<GraphProfile>>? profiles = null, Action? refreshProfiles = null)
        : this(logger, canUse, LauncherServerClient.Create(App.StartupSessionId, () => AppSettings.Instance.ServerBaseUrl),
            WindowsRawLabelPrinter.Shared, deviceModel, profiles, refreshProfiles) { }

    internal LabelPrintingViewModel(ILogger logger, Func<bool> canUse, IHttpRequestService http, IRawLabelPrinter printer,
        Func<string?>? deviceModel = null, Func<IReadOnlyList<GraphProfile>>? profiles = null, Action? refreshProfiles = null)
    {
        _logger = logger; _canUse = canUse; _http = http; _printer = printer;
        _deviceModel = deviceModel ?? (() => null); _profiles = profiles; _refreshProfiles = refreshProfiles;
        Types = new(() => CanEdit, logger, printer, () => RefreshTemplates());
        Types.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LabelTypesViewModel.IsBusy)) { OnPropertyChanged(nameof(IsTypePrinting)); RefreshAvailability(); } };
        RefreshProductNames();
    }

    public bool IsTypePrinting => Types.IsBusy;

    internal void RefreshProductNames(string? preferred = null)
    {
        var names = (_profiles?.Invoke() ?? Array.Empty<GraphProfile>()).Select(p => p.DeviceModel?.Trim())
            .Append(_lookupProductName)
            .Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => EquipmentCatalog.SortKey(n), StringComparer.Ordinal).ToArray();
        var selected = preferred ?? SelectedProductName ?? _deviceModel();
        _refreshingProducts = true;
        try
        {
            ProductNames.Clear();
            foreach (var name in names) ProductNames.Add(name);
            SelectedProductName = names.FirstOrDefault(n => string.Equals(n, selected, StringComparison.OrdinalIgnoreCase))
                ?? (names.Length == 1 ? names[0] : null);
        }
        finally { _refreshingProducts = false; }
        OnPropertyChanged(nameof(HasProducts));
        RefreshTemplates();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void RefreshProducts() { _refreshProfiles?.Invoke(); RefreshProductNames(); }

    public void SetLastTest(LastTestLabelSnapshot? snapshot)
    {
        _lastTest = snapshot?.Copy();
        LastTestPreview = _lastTest?.Label is { } label
            ? LabelPreview.FromInput(label.Template, label.Serial, label.Mac) : null;
        foreach (var property in new[] { nameof(HasLastTest), nameof(HasLastTestLabel), nameof(LastTestDescription), nameof(LastTestResult), nameof(LastTestMessage) })
            OnPropertyChanged(property);
        RefreshAvailability();
    }

    private static bool IsDeviceTemplate(LabelTemplateDefinition t) =>
        t.Language == "ZPL" && (t.Id is "PswUpsBox8x2Pro" or "Psw2G6FPlus" ||
            t.Id.StartsWith("catalog:", StringComparison.Ordinal));
    private static string CardId(string typeId) => typeId == LabelTypeStore.DeviceTypeId ? "DeviceZpl" : typeId == LabelTypeStore.BoardTypeId ? "SerialOnlyEpl" : typeId;
    private string OptionId(LabelTemplateDefinition t)
    {
        var assigned = LabelTypeStore.AssignedTypeId(_typeFile, _modelCatalog?.Resolve(null, t.Name)?.Id);
        return assigned != null ? CardId(assigned) : t.UseShortSerial
            ? string.IsNullOrWhiteSpace(t.CabinetName) ? "DeviceZpl" : LabelTypeStore.CabinetTypeId : "SerialOnlyEpl";
    }
    private LabelTemplateDefinition? SelectedTemplate => ResolveSelectedTemplate(SerialNumber);
    private EquipmentCatalog? _modelCatalog;
    private LabelTemplateDefinition ForProduct(LabelTemplateDefinition template, string serial)
    {
        var model = _modelCatalog?.Resolve(null, ProfileDeviceName);
        if (model != null && !string.IsNullOrWhiteSpace(serial) && LabelPrintContent.CheckBoardSerial(model, serial) is { } error)
            throw new InvalidOperationException(error);
        if (!template.UseShortSerial && model?.SerialSeries is { } series && !string.IsNullOrWhiteSpace(serial))
        {
            if (!long.TryParse(serial, out var full)) throw new InvalidOperationException("Введите полный серийный номер изделия.");
            if (series.Check(full) is { } seriesError) throw new InvalidOperationException(seriesError);
        }
        var resolved = model?.LabelCode != null && !template.UseShortSerial
            ? LabelPrintContent.ForModel(template, model, 0)
            : LabelPrintContent.ForDevice(template, ProfileDeviceName);
        resolved.DataBindings?.ApplyPresentation(resolved);
        return resolved;
    }

    private LabelTemplateDefinition? ResolveSelectedTemplate(string serialNumber)
    {
        var id = LabelOptions.ElementAtOrDefault(SelectedType)?.Id;
        var customType = _typeFile.Types.FirstOrDefault(t => t.Id == id && !LabelTypeStore.IsUniversal(t.Id) && t.ProductModelId == null);
        var board = id == "SerialOnlyEpl" || customType?.Template.UseShortSerial == false;
        var candidates = _workstationTemplates.Where(t => board ? !t.UseShortSerial : t.UseShortSerial).ToArray();
        var source = candidates.FirstOrDefault(t => string.Equals(t.Name, ProfileDeviceName, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(t => int.TryParse(serialNumber, out var serial)
                && t.MatchesServerSerial(serial))
            ?? (candidates.Length == 1 || string.IsNullOrWhiteSpace(serialNumber) ? candidates.FirstOrDefault() : null);
        if (source == null && !board && string.IsNullOrWhiteSpace(ProfileDeviceName) && int.TryParse(serialNumber, out var shortNumber))
        {
            var custom = candidates.Where(t => !IsDeviceTemplate(t) && shortNumber <= t.ShortSerialMax).ToArray();
            if (custom.Length == 1) source = custom[0];
        }
        if (source == null) return null;
        var model = _modelCatalog?.Resolve(null, ProfileDeviceName);
        if (model == null && string.IsNullOrWhiteSpace(ProfileDeviceName)) model = _modelCatalog?.Resolve(null, source.Name);
        // The serial-only fallback remains usable when no model or type code is known.
        if (board && model == null && source.DeviceCode == 0 && customType == null) return source;
        if (customType != null) return LabelTypeStore.ApplyPresentation(customType.Template, source, model, ProfileDeviceName);
        var typeId = board ? LabelTypeStore.BoardTypeId : id == LabelTypeStore.CabinetTypeId ? LabelTypeStore.CabinetTypeId : LabelTypeStore.DeviceTypeId;
        if (typeId != LabelTypeStore.CabinetTypeId && !_savedUniversalTypes.Any(t => t.Id == typeId) &&
            LabelTypeStore.AssignedTypeId(_typeFile, model?.Id) != typeId) return source;
        var template = _universalTypes.Single(t => t.Id == typeId).Template;
        return LabelTypeStore.ApplyPresentation(template, source, model, ProfileDeviceName);
    }

    internal void RefreshTemplates(string? selectedId = null)
    {
        selectedId ??= LabelOptions.ElementAtOrDefault(SelectedType)?.Id;
        EquipmentCatalog? catalog;
        try
        {
            _workstationTemplates = LabelTemplateCatalog.WorkstationTemplates(out catalog);
            _typeFile = LabelTypeStore.LoadFile(AppSettings.Instance.ResolvedGraphsFolder);
            var stored = _typeFile.Types;
            _savedUniversalTypes = stored.Where(t => LabelTypeStore.IsUniversal(t.Id)).ToArray();
            _universalTypes = LabelTypeStore.UniversalDefaults().Select(type => type with {
                Template = LabelTypeStore.UniversalTemplate(type.Id, stored.FirstOrDefault(t => t.Id == type.Id)?.Template ?? type.Template)
            }).ToArray();
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or System.UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            _workstationTemplates.Clear(); LabelOptions.Clear(); TestLabelOptions = Array.Empty<TestLabelOption>(); SelectedTestLabel = null; Preview = null;
            Status = "Каталог этикеток не загружен. Печать по каталогу недоступна: " + ex.Message;
            OnPropertyChanged(nameof(HasMac)); return;
        }
        var testId = SelectedTestLabel?.Id;
        _modelCatalog = catalog;
        TestLabelOptions = GetTestLabelOptions(catalog);
        SelectedTestLabel = TestLabelOptions.FirstOrDefault(option => option.Id == testId) ?? TestLabelOptions[0];
        var selected = _workstationTemplates.FirstOrDefault(t => t.Id == selectedId);
        if (selected != null) selectedId = OptionId(selected);
        var assigned = LabelTypeStore.AssignedTypeId(_typeFile, catalog?.Resolve(null, ProfileDeviceName)?.Id);
        if (assigned != null) selectedId = CardId(assigned);
        LabelOptions.Clear();
        foreach (var id in new[] { LabelTypeStore.DeviceTypeId, LabelTypeStore.BoardTypeId, LabelTypeStore.CabinetTypeId })
        {
            var type = _universalTypes.Single(t => t.Id == id);
            LabelOptions.Add(new(id == LabelTypeStore.DeviceTypeId ? "DeviceZpl" : id == LabelTypeStore.BoardTypeId ? "SerialOnlyEpl" : id,
                type.Name, LabelPreview.FromInput(type.Template, "00000", "00:00:00:00:00:00")));
        }
        foreach (var type in _typeFile.Types.Where(t => t.ProductModelId == null && !LabelTypeStore.IsUniversal(t.Id)))
            LabelOptions.Add(new(type.Id, type.Name, LabelPreview.FromInput(type.Template, "00000", "00:00:00:00:00:00")));
        SelectedType = Math.Max(0, LabelOptions.ToList().FindIndex(t => t.Id == selectedId));
        OnPropertyChanged(nameof(ProfileDeviceName));
        OnPropertyChanged(nameof(HasMac));
        UpdatePreview();
    }

    private IReadOnlyList<TestLabelOption> GetTestLabelOptions(EquipmentCatalog? catalog)
    {
        var options = _workstationTemplates.Select(template => new TestLabelOption(
            "template:" + template.Id, $"{template.Name} · {template.Language}", template)).ToList();
        var modelTemplates = new Dictionary<string, LabelTemplateDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in _workstationTemplates)
            if (catalog?.Resolve(null, template.Name) is { } model)
                modelTemplates.TryAdd(model.Id, template);
        var switchLayout = LabelTemplateCatalog.Legacy("PswUpsBox8x2Pro").CopyUpgraded();
        var boardLayout = LabelTemplateCatalog.Legacy("SerialOnlyEpl").CopyUpgraded();
        foreach (var model in catalog?.Models.Where(model => model.LabelCode.HasValue) ?? Enumerable.Empty<EquipmentModel>())
        {
            if (!modelTemplates.TryGetValue(model.Id, out var template))
            {
                template = model.SerialSeries != null || model.RtlReport != null
                    ? LabelPrintContent.ForModel(model.RtlReport == null ? switchLayout : boardLayout, model, 0)
                    : LabelPrintContent.ForDevice(model.RtlReport == null ? switchLayout : boardLayout, model.Name);
                template.DeviceCode = model.LabelCode!.Value;
                template.CabinetName = model.LabelCabinets?.GetValueOrDefault(0);
                if (template.UseShortSerial)
                    template.BarcodePrefix = "0" + model.LabelCode!.Value.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
                options.Add(new("model:" + model.Id, $"{model.Name} · {template.Language}", template));
            }
            if (model.LabelVariants == null) continue;
            foreach (var (code, name) in model.LabelVariants)
            {
                var variant = template.Copy();
                variant.VariantCode = code;
                variant.VariantName = name;
                variant.CabinetName = model.LabelCabinets?.GetValueOrDefault(code);
                if (variant.UseShortSerial)
                    variant.BarcodePrefix = code.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        model.LabelCode!.Value.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
                options.Add(new($"model:{model.Id}:{code}", $"{model.Name} · {name} · {variant.Language}", variant));
            }
        }
        options.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name));
        options.Insert(0, new("selected", "По выбранной карточке", null));
        return options;
    }

    private LabelPreview PreviewFor(LabelTemplateDefinition template, string serial, string mac)
    {
        try { return LabelPreview.FromInput(ForProduct(template, serial), serial, mac); }
        catch (InvalidOperationException ex) { return new LabelPreview(Array.Empty<LabelPreviewElement>(), 0, 0, 354, 150, ex.Message); }
    }

    private void UpdatePreview()
    {
        var template = SelectedTemplate;
        Preview = template == null ? null : PreviewFor(template, SerialNumber, MacAddress);
    }

    public void RefreshAvailability()
    {
        OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanRepeat));
        OnPropertyChanged(nameof(CanPrintLastTest));
        PrintLastTestCommand.NotifyCanExecuteChanged(); RefreshProductsCommand.NotifyCanExecuteChanged();
        EditTemplatesCommand.NotifyCanExecuteChanged();
        PrintCommand.NotifyCanExecuteChanged(); RepeatLastCommand.NotifyCanExecuteChanged();
        PrintTestLabelCommand.NotifyCanExecuteChanged();
        FindBySerialCommand.NotifyCanExecuteChanged(); FindByMacCommand.NotifyCanExecuteChanged();
        Types.RefreshAvailability();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void EditTemplates() => SelectedSection = 1;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task FindBySerial() => Find(false);
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task FindByMac() => Find(true);
    private Task Find(bool byMac) => Run(async token =>
    {
        var query = byMac ? MacAddress : ResolveSerialForLookup();
        Status = "Получение данных с сервера…";
        var templates = _workstationTemplates;
        var data = await new LabelLookupService(_http, templates).FindAsync(AppSettings.Instance.ServerBaseUrl, query, byMac, token);
        var foundTemplate = templates[data.Kind];
        if (_profiles == null) SelectedProductName = foundTemplate.Name;
        else
        {
            _lookupProductName = foundTemplate.Name;
            RefreshProductNames(foundTemplate.Name);
        }
        var id = OptionId(foundTemplate);
        SelectedType = LabelOptions.ToList().FindIndex(t => t.Id == id);
        SerialNumber = data.Serial; MacAddress = data.Mac;
        Status = $"Найдены данные для «{foundTemplate.Name}». Проверьте этикетку и нажмите «Печатать».";
    });

    private string ResolveSerialForLookup()
    {
        var query = SerialNumber.Trim();
        if (!int.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) return query;
        var templates = _workstationTemplates;
        if (templates.Any(template => template.MatchesServerSerial(number))) return query;
        var selected = SelectedTemplate;
        if (selected is { MatchServerSerial: true } && number <= selected.ShortSerialMax)
            return (selected.SerialOffset + number).ToString(CultureInfo.InvariantCulture);
        if (selected == null && templates.Any(template => template.MatchServerSerial && number <= template.ShortSerialMax))
            throw new InvalidOperationException("Для поиска по короткому номеру выберите изделие. Можно также ввести полный номер или MAC.");
        return query;
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task Print() => PrintData(new(SelectedType, SerialNumber.Trim(), MacAddress.Trim()));
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task PrintTestLabel() => PrintData(new(0, "00000000", "00:00:00:00:00:00"), SelectedTestLabel?.Template, testPrint: true);
    [RelayCommand(CanExecute = nameof(CanRepeat))]
    private Task RepeatLast() => _last == null ? Task.CompletedTask : PrintData(_last, _lastTemplate);
    [RelayCommand(CanExecute = nameof(CanPrintLastTest))]
    private Task PrintLastTest() => _lastTest?.Label is not { } label ? Task.CompletedTask :
        PrintData(new LabelData(0, label.Serial, label.Mac), label.Template);

    private Task PrintData(LabelData data, LabelTemplateDefinition? savedTemplate = null, bool testPrint = false) => Run(async token =>
    {
        var selected = savedTemplate == null ? ResolveSelectedTemplate(testPrint ? "" : SerialNumber) : null;
        if (!testPrint && _profiles != null && selected?.Language == "ZPL" &&
            (SelectedProductName == null || !ProductNames.Contains(SelectedProductName)))
            throw new InvalidOperationException("Выберите изделие из профилей. Название изделия задаётся в редакторе профиля.");
        var template = savedTemplate?.Copy() ?? ForProduct(
            selected ?? throw new InvalidOperationException("Выберите этикетку. Для ZPL укажите полный серийный номер или выберите изделие."), data.Serial);
        if (testPrint && template.UseShortSerial)
        {
            template.SerialOffset = 0;
            template.FirstSerialNumber = null;
            template.BarcodePrefix = new string('0', template.BarcodePrefix.Length);
        }
        if (testPrint) { template.DeviceCode = 0; template.VariantCode = 0; }
        template.Validate();
        var count = testPrint ? 1 : Copies;
        if (count is < 1 or > 100) throw new InvalidOperationException("Количество этикеток должно быть от 1 до 100.");
        var printer = string.IsNullOrWhiteSpace(PrinterName) ? AppSettings.Instance.DefaultPrinterName?.Trim() : PrinterName.Trim();
        if (string.IsNullOrEmpty(printer)) throw new InvalidOperationException("Выберите принтер в разделе «Этикетки» или в настройках программы.");
        Status = "Печать…";
        var context = new TestContext(new RegisterState());
        var step = new PrintLabelStep(_logger, printer, "SerialNumber", "SerialShort", "Mac",
            true, data.Serial, data.Mac, count, template.UseShortSerial, true, _printer, 30000,
            DeviceLabelModel.PswUpsBox8x2Pro, template, preserveTemplate: true);
        var result = await step.ExecuteAsync(context, token);
        if (result == StepResult.False) throw new InvalidOperationException(context.GetVariable<string>("PrintLabel.Error"));
        if (!testPrint)
        {
            _last = data; _lastTemplate = template.Copy();
            LastPreview = LabelPreview.Parse(template, context.GetVariable<string>("PrintLabel.SingleCommand") ?? "");
            LastLabelDescription = $"{template.Name} · SN {data.Serial}" + (!template.RequiresMac ? "" : $" · MAC {data.Mac}");
        }
        Status = context.GetVariable<string>("PrintLabel.CompletionStatus") == "SentToPrinter"
            ? $"Передано принтеру: {count} шт. Драйвер не подтверждает физический выход этикетки."
            : $"Печать завершена: {count} шт.";
    });

    private async Task Run(Func<CancellationToken, Task> action)
    {
        if (!CanEdit) return;
        using var operation = new CancellationTokenSource();
        _operation = operation; IsBusy = true;
        try { await action(operation.Token); }
        catch (PrintCancellationException ex) { Status = ex.Message; }
        catch (OperationCanceledException) { Status = "Операция отменена."; }
        catch (JsonException) { Status = "Сервер вернул ответ, который не удалось прочитать как историю изделия."; }
        catch (Exception ex) { Status = ex.Message; _logger.Warning("Проверка печати: " + ex.Message); }
        finally { _operation = null; IsBusy = false; }
    }
    [RelayCommand] private void Cancel() => _operation?.Cancel();
    public void Dispose() { _disposed = true; _operation?.Cancel(); Types.Dispose(); (_http as IDisposable)?.Dispose(); }
}

public sealed record LabelTemplatePreviewItem(string Id, string Name, LabelPreview Preview)
{
    public override string ToString() => Name;
}

public sealed record TestLabelOption(string Id, string Name, LabelTemplateDefinition? Template)
{
    public override string ToString() => Name;
}
