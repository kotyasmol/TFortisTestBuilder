using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.ObjectModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;

namespace TestBuilder.ViewModels;

public partial class LabelPrintingViewModel : ViewModelBase, IDisposable
{
    public ObservableCollection<LabelTemplatePreviewItem> LabelOptions { get; } = new();
    [ObservableProperty] private LabelPreview? preview;
    [ObservableProperty] private LabelPreview? lastPreview;
    public bool HasLastPreview => LastPreview != null;
    [ObservableProperty] private int selectedType;
    [ObservableProperty] private string serialNumber = "";
    [ObservableProperty] private string macAddress = "";
    [ObservableProperty] private string printerName = "TSC TE310";
    [ObservableProperty] private int copies = 1;
    [ObservableProperty] private string status = "Введите данные этикетки или получите их с сервера.";
    [ObservableProperty] private string lastLabelDescription = "В этой вкладке ещё не печатали этикетки.";
    [ObservableProperty] private bool isBusy;
    private readonly ILogger _logger;
    private readonly IHttpRequestService _http;
    private readonly IRawLabelPrinter _printer;
    private readonly Func<bool> _canUse;
    private readonly Func<string?> _deviceModel;
    public string ProfileDeviceName => _deviceModel() ?? "";
    private CancellationTokenSource? _operation;
    private LabelData? _last;
    private LabelTemplateDefinition? _lastTemplate;
    private bool _disposed;
    public bool CanEdit => !IsBusy && !_disposed && _canUse();
    public bool CanRepeat => CanEdit && _last != null;
    public bool HasMac => SelectedTemplate?.RequiresMac == true;
    partial void OnSelectedTypeChanged(int value) { OnPropertyChanged(nameof(HasMac)); UpdatePreview(); }
    partial void OnSerialNumberChanged(string value) => UpdatePreview();
    partial void OnMacAddressChanged(string value) => UpdatePreview();
    partial void OnLastPreviewChanged(LabelPreview? value) => OnPropertyChanged(nameof(HasLastPreview));
    partial void OnIsBusyChanged(bool value) => RefreshAvailability();

    public LabelPrintingViewModel(ILogger logger, Func<bool> canUse, Func<string?>? deviceModel = null)
        : this(logger, canUse, LauncherServerClient.Create(App.StartupSessionId, AppSettings.Instance.ServerBaseUrl), new WindowsRawLabelPrinter(), deviceModel) { }

    internal LabelPrintingViewModel(ILogger logger, Func<bool> canUse, IHttpRequestService http, IRawLabelPrinter printer, Func<string?>? deviceModel = null)
    { _logger = logger; _canUse = canUse; _http = http; _printer = printer; _deviceModel = deviceModel ?? (() => null); RefreshTemplates(); }

    private static bool IsDeviceTemplate(LabelTemplateDefinition t) =>
        t.Id is "PswUpsBox8x2Pro" or "Psw2G6FPlus" && t.Language == "ZPL";
    private static string OptionId(LabelTemplateDefinition t) => IsDeviceTemplate(t) ? "DeviceZpl" : t.Id;
    private LabelTemplateDefinition? SelectedTemplate
    {
        get
        {
            var id = LabelOptions.ElementAtOrDefault(SelectedType)?.Id;
            var candidates = AppSettings.Instance.LabelTemplates.Where(t => OptionId(t) == id).ToArray();
            return candidates.FirstOrDefault(t => t.Name == ProfileDeviceName)
                ?? candidates.FirstOrDefault(t => int.TryParse(SerialNumber, out var serial)
                    && serial >= t.SerialOffset && (long)serial <= (long)t.SerialOffset + t.ShortSerialMax)
                ?? (candidates.Length == 1 || string.IsNullOrWhiteSpace(SerialNumber) ? candidates.FirstOrDefault() : null);
        }
    }

    internal void RefreshTemplates(string? selectedId = null)
    {
        selectedId ??= LabelOptions.ElementAtOrDefault(SelectedType)?.Id;
        var selected = AppSettings.Instance.LabelTemplates.FirstOrDefault(t => t.Id == selectedId);
        if (selected != null) selectedId = OptionId(selected);
        LabelOptions.Clear();
        foreach (var group in AppSettings.Instance.LabelTemplates.GroupBy(OptionId))
        {
            var template = group.FirstOrDefault(t => t.Name == ProfileDeviceName) ?? group.First();
            var name = group.Key == "DeviceZpl" ? "ZPL · Изделие, MAC, SN" :
                template.Id == "SerialOnlyEpl" ? "EPL · Серийный номер" : template.Name;
            LabelOptions.Add(new(group.Key, name, PreviewFor(template, "", "")));
        }
        SelectedType = Math.Max(0, LabelOptions.ToList().FindIndex(t => t.Id == selectedId));
        OnPropertyChanged(nameof(ProfileDeviceName));
        OnPropertyChanged(nameof(HasMac));
        UpdatePreview();
    }

    private LabelPreview PreviewFor(LabelTemplateDefinition template, string serial, string mac)
    {
        try { return LabelPreview.FromInput(LabelPrintContent.ForDevice(template, ProfileDeviceName), serial, mac); }
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
        EditTemplatesCommand.NotifyCanExecuteChanged();
        PrintCommand.NotifyCanExecuteChanged(); RepeatLastCommand.NotifyCanExecuteChanged();
        FindBySerialCommand.NotifyCanExecuteChanged(); FindByMacCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task EditTemplates()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            var selected = await new TestBuilder.Views.LabelTemplatesWindow().ShowDialog<LabelTemplateDefinition?>(desktop.MainWindow);
            RefreshTemplates(selected?.Id);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task FindBySerial() => Find(false);
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task FindByMac() => Find(true);
    private Task Find(bool byMac) => Run(async token =>
    {
        Status = "Получение данных с сервера…";
        var data = await new LabelLookupService(_http).FindAsync(AppSettings.Instance.ServerBaseUrl,
            byMac ? MacAddress : SerialNumber, byMac, token);
        var id = OptionId(AppSettings.Instance.LabelTemplates[data.Kind]);
        SelectedType = LabelOptions.ToList().FindIndex(t => t.Id == id);
        SerialNumber = data.Serial; MacAddress = data.Mac;
        Status = "Данные получены. Проверьте этикетку и нажмите «Печатать».";
    });

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task Print() => PrintData(new(SelectedType, SerialNumber.Trim(), MacAddress.Trim()));
    [RelayCommand(CanExecute = nameof(CanRepeat))]
    private Task RepeatLast() => _last == null ? Task.CompletedTask : PrintData(_last, _lastTemplate);

    private Task PrintData(LabelData data, LabelTemplateDefinition? savedTemplate = null) => Run(async token =>
    {
        var template = savedTemplate?.Copy() ?? LabelPrintContent.ForDevice(
            SelectedTemplate ?? throw new InvalidOperationException("Выберите этикетку. Для ZPL укажите полный серийный номер или выберите профиль изделия."), ProfileDeviceName);
        template.Validate();
        if (Copies is < 1 or > 100) throw new InvalidOperationException("Количество этикеток: от 1 до 100.");
        var printer = PrinterName.Trim(); var count = Copies;
        Status = "Печать…";
        var context = new TestContext(new RegisterState());
        var step = new PrintLabelStep(_logger, printer, "SerialNumber", "SerialShort", "Mac",
            true, data.Serial, data.Mac, count, template.UseShortSerial, true, _printer, 30000,
            DeviceLabelModel.PswUpsBox8x2Pro, template);
        var result = await step.ExecuteAsync(context, token);
        if (result == StepResult.False) throw new InvalidOperationException(context.GetVariable<string>("PrintLabel.Error"));
        _last = data; _lastTemplate = template.Copy();
        LastPreview = LabelPreview.Parse(template, context.GetVariable<string>("PrintLabel.SingleCommand") ?? "");
        LastLabelDescription = $"{template.Name} · SN {data.Serial}" + (!template.RequiresMac ? "" : $" · MAC {data.Mac}");
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
        catch (OperationCanceledException) { Status = "Операция отменена."; }
        catch (JsonException) { Status = "Сервер вернул ответ, который не удалось прочитать как историю изделия."; }
        catch (Exception ex) { Status = ex.Message; _logger.Warning("Проверка печати: " + ex.Message); }
        finally { _operation = null; IsBusy = false; }
    }
    [RelayCommand] private void Cancel() => _operation?.Cancel();
    public void Dispose() { _disposed = true; _operation?.Cancel(); (_http as IDisposable)?.Dispose(); }
}

public sealed record LabelTemplatePreviewItem(string Id, string Name, LabelPreview Preview)
{
    public override string ToString() => Name;
}
