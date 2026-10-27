using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
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
    public string[] LabelTypes { get; } = ["PSW+UPS-Box 8x2Pro · 25×13 мм", "PSW-2G6F+ · 25×13 мм", "Только серийный номер · 30×10 мм"];
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
    private bool _disposed;
    public bool CanEdit => !IsBusy && !_disposed && _canUse();
    public bool CanRepeat => CanEdit && _last != null;
    public bool HasMac => SelectedType != 2;
    partial void OnSelectedTypeChanged(int value) => OnPropertyChanged(nameof(HasMac));
    partial void OnIsBusyChanged(bool value) => RefreshAvailability();

    public LabelPrintingViewModel(ILogger logger, Func<bool> canUse)
        : this(logger, canUse, LauncherServerClient.Create(App.StartupSessionId, AppSettings.Instance.ServerBaseUrl), new WindowsRawLabelPrinter()) { }

    internal LabelPrintingViewModel(ILogger logger, Func<bool> canUse, IHttpRequestService http, IRawLabelPrinter printer)
    { _logger = logger; _canUse = canUse; _http = http; _printer = printer; }

    public void RefreshAvailability()
    {
        OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanRepeat));
        PrintCommand.NotifyCanExecuteChanged(); RepeatLastCommand.NotifyCanExecuteChanged();
        FindBySerialCommand.NotifyCanExecuteChanged(); FindByMacCommand.NotifyCanExecuteChanged();
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
    private Task RepeatLast() => _last == null ? Task.CompletedTask : PrintData(_last);

    private Task PrintData(LabelData data) => Run(async token =>
    {
        if (data.Kind is < 0 or > 2) throw new InvalidOperationException("Выберите тип этикетки.");
        if (Copies is < 1 or > 100) throw new InvalidOperationException("Количество этикеток: от 1 до 100.");
        var printer = PrinterName.Trim(); var count = Copies;
        Status = "Печать…";
        var context = new TestContext(new RegisterState());
        var step = new PrintLabelStep(_logger, printer, "SerialNumber", "SerialShort", "Mac",
            true, data.Serial, data.Mac, count, data.Kind != 2, true, _printer, 30000,
            data.Kind == 1 ? DeviceLabelModel.Psw2G6FPlus : DeviceLabelModel.PswUpsBox8x2Pro);
        var result = await step.ExecuteAsync(context, token);
        if (result == StepResult.False) throw new InvalidOperationException(context.GetVariable<string>("PrintLabel.Error"));
        _last = data;
        LastLabelDescription = $"{LabelTypes[data.Kind]} · SN {data.Serial}" + (data.Kind == 2 ? "" : $" · MAC {data.Mac}");
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
