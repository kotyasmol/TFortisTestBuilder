using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using TestBuilder.Services;

namespace TestBuilder.ViewModels;

public partial class NetworkSetupRowViewModel : ObservableObject
{
    public string Ip { get; }
    public string Port => (int.Parse(Ip.Split('.')[3]) - 2).ToString();
    public ObservableCollection<BenchAdapter> Adapters { get; }

    [ObservableProperty] private BenchAdapter? selectedAdapter;

    public NetworkSetupRowViewModel(string ip, ObservableCollection<BenchAdapter> adapters)
    {
        Ip = ip;
        Adapters = adapters;
    }
}

public partial class NetworkSetupViewModel : ViewModelBase
{
    public ObservableCollection<BenchAdapter> Adapters { get; } = new();
    public ObservableCollection<NetworkSetupRowViewModel> Rows { get; }

    [ObservableProperty] private string status = string.Empty;
    [ObservableProperty] private bool isBusy;

    public IRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand ApplyCommand { get; }

    public NetworkSetupViewModel()
    {
        Rows = new(DataTestNetworkConfigurator.BenchIps.Select(
            ip => new NetworkSetupRowViewModel(ip, Adapters)));
        RefreshCommand = new RelayCommand(Refresh);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync);
        Refresh();
    }

    private void Refresh()
    {
        if (IsBusy) return;

        try
        {
            var previous = Rows.ToDictionary(row => row.Ip,
                row => row.SelectedAdapter?.Id ??
                    (AppSettings.Instance.DataTestAdapterIds ?? new()).GetValueOrDefault(row.Ip));
            var current = DataTestNetworkConfigurator.GetAdapters();
            Adapters.Clear();
            foreach (var adapter in current) Adapters.Add(adapter);

            foreach (var row in Rows)
            {
                var savedId = previous[row.Ip];
                row.SelectedAdapter = current.FirstOrDefault(adapter =>
                    string.Equals(adapter.Id, savedId, StringComparison.OrdinalIgnoreCase)) ??
                    current.FirstOrDefault(adapter =>
                        adapter.Ipv4.Split(", ").Contains(row.Ip));
            }

            Status = $"Найдено сетевых адаптеров: {Adapters.Count}. Проверьте соответствие портов и карт.";
        }
        catch (Exception ex)
        {
            Status = $"Не удалось получить список адаптеров: {ex.Message}";
        }
    }

    private async Task ApplyAsync()
    {
        if (IsBusy) return;

        var assignments = Rows.Select(row => new AdapterAssignment(
            row.Ip, row.SelectedAdapter?.Id ?? string.Empty)).ToArray();
        var error = DataTestNetworkConfigurator.ValidateAssignments(assignments, Adapters.ToArray());
        if (error != null)
        {
            Status = error;
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            Status = "Настройка адаптеров доступна только в Windows.";
            return;
        }

        IsBusy = true;
        try
        {
            // Save the physical adapter mapping before changing IPs, so it survives a partial failure.
            AppSettings.Instance.DataTestAdapterIds = assignments.ToDictionary(
                assignment => assignment.Ip, assignment => assignment.AdapterId);
            AppSettings.Instance.Save();

            var progress = new Progress<string>(message => Status = message);
            error = await DataTestNetworkConfigurator.ApplyAsync(assignments, progress);
            Status = error ?? "Адреса настроены. Проверьте состояние линков и повторите DataTest.";
        }
        catch (Exception ex)
        {
            Status = $"Ошибка настройки: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
