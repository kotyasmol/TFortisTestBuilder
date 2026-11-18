using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Services;

namespace TestBuilder.ViewModels;

public partial class NetworkSetupViewModel
{
    private Func<NetworkProbePair, CancellationToken, Task<NetworkProbeResult>> _probePair = NetworkConnectivityProbe.CheckAsync;
    private CancellationTokenSource? _probeCancellation;
    public ObservableCollection<NetworkProbeResult> ProbeResults { get; } = new();
    [ObservableProperty] private string probeStatus = "Проверка связи ещё не выполнялась.";
    public bool CanProbe => !IsBusy && !IsCheckingRuntime && _canInspectRuntime();
    public bool CanCancelProbe => _probeCancellation != null;

    private string ProbeMapping() => string.Join("\n", Rows.Select(r =>
        $"{r.Ip}|{r.SelectedAdapter?.Id}|{r.SelectedAdapter?.Ipv4}|{r.SelectedAdapter?.Status}|{r.SelectedAdapter?.SpeedMbps}|{r.SelectedAdapter?.HasIpv4Gateway}"));

    private void InvalidateProbeResults()
    {
        if (ProbeResults.Count == 0) return;
        ProbeResults.Clear();
        ProbeStatus = "Подключение изменилось. Повторите проверку связи.";
    }

    [RelayCommand(CanExecute = nameof(CanProbe))]
    private async Task ProbeConnections()
    {
        if (!CanProbe) return;
        Refresh();
        ProbeResults.Clear();
        NetworkProbePair[] pairs;
        try
        {
            pairs = NetworkConnectivityProbe.CreatePairs(AppSettings.Instance.NetworkPlan,
                Rows.Where(r => r.SelectedAdapter != null).ToDictionary(r => r.Ip, r => r.SelectedAdapter!.Id)).ToArray();
        }
        catch (Exception ex) { ProbeStatus = ex.Message; return; }
        using var cancellation = new CancellationTokenSource();
        _probeCancellation = cancellation;
        IsBusy = true;
        RefreshAvailability();
        ProbeResults.Clear();
        try
        {
            foreach (var pair in pairs)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                ProbeStatus = $"Проверяется {pair.Name}: {pair.FirstIp} ↔ {pair.SecondIp}";
                ProbeResults.Add(await _probePair(pair, cancellation.Token));
            }
            ProbeStatus = $"Проверено {DateTime.Now:HH:mm:ss}. Пар: {ProbeResults.Count}. Связь в обе стороны: {ProbeResults.Count(r => r.Passed)}.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { ProbeStatus = "Проверка остановлена."; }
        catch (Exception ex) { ProbeStatus = "Ошибка проверки сети: " + ex.Message; }
        finally
        {
            _probeCancellation = null;
            IsBusy = false;
            RefreshAvailability();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelProbe))]
    private void CancelProbe() => _probeCancellation?.Cancel();
}
