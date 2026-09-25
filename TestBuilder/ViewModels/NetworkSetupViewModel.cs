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
    public string Psw2G6FPort => int.Parse(Ip.Split('.')[3]) switch
    {
        >= 2 and <= 7 => $"Медный {int.Parse(Ip.Split('.')[3]) - 1}",
        8 => "SFP 1",
        9 => "SFP 2",
        _ => "Резерв"
    };
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
    public IRelayCommand SuggestPsw2G6FCommand { get; }
    public IAsyncRelayCommand ApplyCommand { get; }

    public NetworkSetupViewModel()
    {
        Rows = new(DataTestNetworkConfigurator.BenchIps.Select(
            ip => new NetworkSetupRowViewModel(ip, Adapters)));
        RefreshCommand = new RelayCommand(Refresh);
        SuggestPsw2G6FCommand = new RelayCommand(SuggestPsw2G6F);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync);
        Refresh();
    }

    private void SuggestPsw2G6F()
    {
        if (IsBusy) return;

        var savedIds = (AppSettings.Instance.DataTestAdapterIds ?? new()).Values.ToArray();
        var (assignments, error) = DataTestNetworkConfigurator.SuggestPsw2G6FAssignments(
            Adapters.ToArray(), savedIds);
        if (error != null)
        {
            Status = error;
            return;
        }

        foreach (var row in Rows)
        {
            var id = assignments!.Single(item => item.Ip == row.Ip).AdapterId;
            row.SelectedAdapter = Adapters.Single(item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        Status = "Подбор PSW-2G6F+ готов: 6 карт 100 Мбит/с → .2–.7; " +
            "2 карты 1 Гбит/с → .8–.9; 2 отключённые → .10–.11. " +
            "Проверьте, какие кабели подключены к каждой паре портов, затем нажмите «Применить адреса».";
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
            IsBusy = false;
            Refresh();
            Status = error ?? "Адреса настроены. Проверьте физические пары и повторите DataTest.";
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
