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
    [ObservableProperty] private string message = "";
    public string CurrentAddress => SelectedAdapter?.Ipv4 is { Length: > 0 } ip ? ip : "Нет IPv4";
    public string Link => SelectedAdapter == null ? "Карта не выбрана" :
        SelectedAdapter.LinkDisplay;
    public event Action<NetworkSetupRowViewModel, BenchAdapter?>? SelectionChanged;
    partial void OnSelectedAdapterChanged(BenchAdapter? oldValue, BenchAdapter? newValue)
    {
        OnPropertyChanged(nameof(CurrentAddress));
        OnPropertyChanged(nameof(Link));
        SelectionChanged?.Invoke(this, oldValue);
    }
    [RelayCommand] private void Clear() => SelectedAdapter = null;

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

    private readonly Func<IReadOnlyList<BenchAdapter>> _getAdapters;
    private readonly Func<bool> _canUse;
    private bool _updating;
    private Dictionary<string, string?>? _draft;
    private void CaptureDraft() => _draft = Rows.ToDictionary(r => r.Ip, r => r.SelectedAdapter?.Id);
    public ObservableCollection<string> Journal { get; } = new();
    [ObservableProperty] private string validation = "";
    public bool CanEdit => !IsBusy && _canUse();
    public bool CanApply => CanEdit && string.IsNullOrEmpty(Validation);
    public string Summary => $"Выбрано карт: {Rows.Count(r => r.SelectedAdapter != null)} из {Rows.Count}";
    public IRelayCommand ResetCommand { get; }
    public void RefreshAvailability()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanApply));
        RefreshCommand.NotifyCanExecuteChanged();
        SuggestPsw2G6FCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsBusyChanged(bool value) => RefreshAvailability();
    private void SelectionChanged(NetworkSetupRowViewModel row, BenchAdapter? previous)
    {
        if (_updating) return;
        _updating = true;
        // Selecting an occupied card exchanges the two assignments.
        if (row.SelectedAdapter != null)
            foreach (var other in Rows.Where(r => r != row && string.Equals(r.SelectedAdapter?.Id,
                row.SelectedAdapter.Id, StringComparison.OrdinalIgnoreCase))) other.SelectedAdapter = previous;
        _updating = false;
        CaptureDraft();
        Validate();
    }
    private void Validate()
    {
        Validation = DataTestNetworkConfigurator.ValidateAssignments(Rows.Select(r =>
            new AdapterAssignment(r.Ip, r.SelectedAdapter?.Id ?? "")).ToArray(), Adapters.ToArray()) ?? "";
        foreach (var row in Rows)
            row.Message = row.SelectedAdapter == null ? "Выберите карту" :
                row.CurrentAddress == row.Ip ? "Адрес совпадает" : $"Будет назначен {row.Ip}/24";
        OnPropertyChanged(nameof(Summary));
        RefreshAvailability();
    }
    private void Reset()
    {
        if (!CanEdit) return;
        _updating = true;
        foreach (var row in Rows) row.SelectedAdapter = null;
        _updating = false;
        _draft = null;
        Refresh();
    }

    [ObservableProperty] private string status = string.Empty;
    [ObservableProperty] private bool isBusy;

    public IRelayCommand RefreshCommand { get; }
    public IRelayCommand SuggestPsw2G6FCommand { get; }
    public IAsyncRelayCommand ApplyCommand { get; }

    public NetworkSetupViewModel() : this(DataTestNetworkConfigurator.GetAdapters, () => true) { }
    internal NetworkSetupViewModel(Func<IReadOnlyList<BenchAdapter>> getAdapters, Func<bool> canUse)
    {
        _getAdapters = getAdapters;
        _canUse = canUse;
        Rows = new(DataTestNetworkConfigurator.BenchIps.Select(
            ip => new NetworkSetupRowViewModel(ip, Adapters)));
        foreach (var row in Rows) row.SelectionChanged += SelectionChanged;
        ResetCommand = new RelayCommand(Reset, () => CanEdit);
        RefreshCommand = new RelayCommand(Refresh, () => CanEdit);
        SuggestPsw2G6FCommand = new RelayCommand(SuggestPsw2G6F, () => CanEdit);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => CanApply);
        Refresh();
    }

    private void SuggestPsw2G6F()
    {
        if (!CanEdit) return;

        var savedIds = (AppSettings.Instance.DataTestAdapterIds ?? new()).Values.ToArray();
        var (assignments, error) = DataTestNetworkConfigurator.SuggestPsw2G6FAssignments(
            Adapters.ToArray(), savedIds);
        if (error != null)
        {
            Status = error;
            return;
        }

        _updating = true;
        foreach (var row in Rows)
        {
            var id = assignments!.Single(item => item.Ip == row.Ip).AdapterId;
            row.SelectedAdapter = Adapters.Single(item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        _updating = false;
        CaptureDraft();
        Validate();
        Status = "Карты подобраны. Проверьте подключение кабелей и примените адреса.";
    }

    private void Refresh()
    {
        if (IsBusy) return;

        try
        {
            var previous = _draft ?? Rows.ToDictionary(row => row.Ip,
                row => (AppSettings.Instance.DataTestAdapterIds ?? new()).GetValueOrDefault(row.Ip));
            var current = _getAdapters();
            _updating = true;
            Adapters.Clear();
            foreach (var adapter in current) Adapters.Add(adapter);

            foreach (var row in Rows)
            {
                var savedId = previous[row.Ip];
                row.SelectedAdapter = savedId != null
                    ? current.FirstOrDefault(adapter => string.Equals(adapter.Id, savedId, StringComparison.OrdinalIgnoreCase))
                    : _draft == null ? current.FirstOrDefault(adapter => adapter.Ipv4.Split(", ").Contains(row.Ip)) : null;
            }

            _updating = false;
            _draft ??= Rows.ToDictionary(r => r.Ip, r => previous[r.Ip] ?? r.SelectedAdapter?.Id);
            Validate();
            Status = $"Найдено адаптеров: {Adapters.Count}.";
        }
        catch (Exception ex)
        {
            _updating = false;
            Status = $"Не удалось получить список адаптеров: {ex.Message}";
            Validation = Status;
            RefreshAvailability();
        }
    }

    private async Task ApplyAsync()
    {
        if (!CanEdit) return;

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

        Journal.Clear();
        IsBusy = true;
        try
        {
            // Save the physical adapter mapping before changing IPs, so it survives a partial failure.
            AppSettings.Instance.DataTestAdapterIds = assignments.ToDictionary(
                assignment => assignment.Ip, assignment => assignment.AdapterId);
            AppSettings.Instance.Save();

            var progress = new Progress<string>(message => { Status = message; Journal.Add(message); });
            error = await DataTestNetworkConfigurator.ApplyAsync(assignments, progress);
            IsBusy = false;
            Refresh();
            Status = error ?? "Адреса настроены.";
            Journal.Add(Status);
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
