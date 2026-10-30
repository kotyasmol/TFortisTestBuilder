using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Services;

namespace TestBuilder.ViewModels;

public partial class PrinterPickerViewModel : ViewModelBase
{
    private readonly IPrinterCatalog _catalog;
    private CancellationTokenSource? _refresh;
    private bool _loaded;
    [ObservableProperty] private IReadOnlyList<string> printers = Array.Empty<string>();
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string message = "";
    public bool CanRefresh => !IsRefreshing;
    public bool CanChoose => !IsRefreshing && Printers.Count > 0;
    public bool HasMessage => Message.Length > 0;
    internal PrinterPickerViewModel(IPrinterCatalog catalog) => _catalog = catalog;
    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));
    partial void OnPrintersChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(CanChoose));
    partial void OnIsRefreshingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRefresh)); OnPropertyChanged(nameof(CanChoose));
        RefreshCommand.NotifyCanExecuteChanged();
    }
    public Task EnsureLoadedAsync() => _loaded || IsRefreshing ? Task.CompletedTask : RefreshCommand.ExecuteAsync(null);
    public void CancelRefresh() => _refresh?.Cancel();

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task Refresh()
    {
        if (IsRefreshing) return;
        using var cancellation = new CancellationTokenSource();
        _refresh = cancellation; IsRefreshing = true; Message = "Загрузка принтеров…";
        try
        {
            var names = await _catalog.GetPrintersAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            Printers = names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToArray();
            Message = Printers.Count == 0 ? "Принтеры не найдены. Добавьте принтер в Windows и нажмите «Обновить»." : "";
            _loaded = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Message = ""; }
        catch (PlatformNotSupportedException ex) { Message = ex.Message; }
        catch (TimeoutException) { Message = "Windows не ответила за 10 секунд. Можно ввести имя принтера вручную или повторить обновление."; }
        catch (Exception ex) { Message = "Не удалось получить список принтеров: " + ex.Message; }
        finally { _refresh = null; IsRefreshing = false; }
    }
}
