using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;

namespace TestBuilder.ViewModels;

public partial class LabelPrintingViewModel : ViewModelBase, IDisposable
{
    public string[] LabelTypes => AppSettings.Instance.LabelTemplates.Select(t => t.Name).ToArray();
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
    private CancellationTokenSource? _operation;
    private LabelData? _last;
    private LabelTemplateDefinition? _lastTemplate;
    private bool _disposed;
    public bool CanEdit => !IsBusy && !_disposed && _canUse();
    public bool CanRepeat => CanEdit && _last != null;
    public bool HasMac => SelectedType >= 0 && SelectedType < AppSettings.Instance.LabelTemplates.Count && AppSettings.Instance.LabelTemplates[SelectedType].RequiresMac;
    partial void OnSelectedTypeChanged(int value) => OnPropertyChanged(nameof(HasMac));
    partial void OnIsBusyChanged(bool value) => RefreshAvailability();

    public LabelPrintingViewModel(ILogger logger, Func<bool> canUse)
        : this(logger, canUse, LauncherServerClient.Create(App.StartupSessionId, AppSettings.Instance.ServerBaseUrl), new WindowsRawLabelPrinter()) { }

    internal LabelPrintingViewModel(ILogger logger, Func<bool> canUse, IHttpRequestService http, IRawLabelPrinter printer)
    { _logger = logger; _canUse = canUse; _http = http; _printer = printer; }

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
            OnPropertyChanged(nameof(LabelTypes));
            if (selected != null) SelectedType = AppSettings.Instance.LabelTemplates.FindIndex(t => t.Id == selected.Id);
            OnPropertyChanged(nameof(HasMac));
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
        SelectedType = data.Kind; SerialNumber = data.Serial; MacAddress = data.Mac;
        Status = "Данные получены. Проверьте этикетку и нажмите «Печатать».";
    });

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task Print() => PrintData(new(SelectedType, SerialNumber.Trim(), MacAddress.Trim()));
    [RelayCommand(CanExecute = nameof(CanRepeat))]
    private Task RepeatLast() => _last == null ? Task.CompletedTask : PrintData(_last, _lastTemplate);

    private Task PrintData(LabelData data, LabelTemplateDefinition? savedTemplate = null) => Run(async token =>
    {
        if (savedTemplate == null && (data.Kind < 0 || data.Kind >= AppSettings.Instance.LabelTemplates.Count)) throw new InvalidOperationException("Выберите тип этикетки.");
        var template = (savedTemplate ?? AppSettings.Instance.LabelTemplates[data.Kind]).Copy();
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
