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
    public string Port { get; }
    public string Role { get; }
    public string TestAssignment { get; }
    public IAsyncRelayCommand RenameCommand { get; }
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

    public NetworkSetupRowViewModel(string ip, ObservableCollection<BenchAdapter> adapters, int index = 0, BenchNetworkAddress? definition = null, Func<NetworkSetupRowViewModel, Task>? rename = null)
    {
        Ip = ip;
        Port = string.IsNullOrWhiteSpace(definition?.Name) ? $"Карта {index + 1}" : definition.Name;
        Role = definition?.Role ?? "";
        TestAssignment = definition?.IncludeInDataTest == true ? $"{definition.Pair} · тест {definition.TestMbps} Мбит/с" : "Не включена в план DataTest";
        RenameCommand = new AsyncRelayCommand(() => rename?.Invoke(this) ?? Task.CompletedTask);
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
        ResetCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
        EditAddressPlanCommand.NotifyCanExecuteChanged();
        SaveBindingsCommand.NotifyCanExecuteChanged();
        RememberLinksCommand.NotifyCanExecuteChanged();
        CompareLinksCommand.NotifyCanExecuteChanged();
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
                row.CurrentAddress == row.Ip ? "Адрес совпадает" : $"Будет назначен {row.Ip}";
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
    public IAsyncRelayCommand ApplyCommand { get; }

    public NetworkSetupViewModel() : this(DataTestNetworkConfigurator.GetAdapters, () => true) { }
    internal NetworkSetupViewModel(Func<IReadOnlyList<BenchAdapter>> getAdapters, Func<bool> canUse)
    {
        _getAdapters = getAdapters;
        _canUse = canUse;
        Rows = new(DataTestNetworkConfigurator.BenchIps.Select(
            (ip, index) => CreateRow(ip, index)));
        foreach (var row in Rows) row.SelectionChanged += SelectionChanged;
        ResetCommand = new RelayCommand(Reset, () => CanEdit);
        RefreshCommand = new RelayCommand(Refresh, () => CanEdit);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => CanApply);
        Refresh();
    }

    private NetworkSetupRowViewModel CreateRow(string ip, int index) =>
        new(ip, Adapters, index, AppSettings.Instance.NetworkPlan.Addresses.Single(a => a.Ip == ip), RenameAdapter);

    private Dictionary<string, BenchAdapter>? _linkBaseline;
    [ObservableProperty] private string linkChanges = "";
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void RememberLinks()
    {
        Refresh();
        if (Validation.StartsWith("Не удалось")) return;
        _linkBaseline = Adapters.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        LinkChanges = "Состояние запомнено. Отключите один кабель и нажмите «Найти изменение линка».";
    }
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void CompareLinks()
    {
        if (_linkBaseline == null) { LinkChanges = "Сначала запомните состояние линков."; return; }
        Refresh();
        if (Validation.StartsWith("Не удалось")) { LinkChanges = Validation; return; }
        var changes = Adapters.Where(a => !_linkBaseline.TryGetValue(a.Id, out var old) || old.Status != a.Status || old.SpeedMbps != a.SpeedMbps)
            .Select(a => $"{a.Name} · MAC {a.Mac} · {a.Ipv4}: {(_linkBaseline.TryGetValue(a.Id, out var old) ? old.LinkDisplay : "не было")} → {a.LinkDisplay}")
            .Concat(_linkBaseline.Values.Where(a => !Adapters.Any(now => now.Id.Equals(a.Id, StringComparison.OrdinalIgnoreCase)))
                .Select(a => $"{a.Name} · MAC {a.Mac}: карта исчезла")).ToArray();
        LinkChanges = changes.Length == 0 ? "Изменений нет. Проверьте кабель и обновите ещё раз."
            : string.Join(Environment.NewLine, changes) + (changes.Length == 1 ? "" : Environment.NewLine + "Изменилось несколько карт. Повторите с одним кабелем.");
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void SaveBindings()
    {
        if (!CanApply) return;
        var previous = AppSettings.Instance.DataTestAdapterIds;
        try
        {
            AppSettings.Instance.DataTestAdapterIds = Rows.ToDictionary(r => r.Ip, r => r.SelectedAdapter!.Id);
            AppSettings.Instance.SaveChecked();
            Status = "Привязки сохранены. Для изменения IP используйте «Применить адреса».";
        }
        catch (Exception ex) { AppSettings.Instance.DataTestAdapterIds = previous; Status = ex.Message; }
    }

    private async Task RenameAdapter(NetworkSetupRowViewModel row)
    {
        if (!CanEdit || row.SelectedAdapter == null) return;
        if (Avalonia.Application.Current?.ApplicationLifetime is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;
        var adapterId = row.SelectedAdapter.Id;
        var name = await new TestBuilder.Views.NetworkAdapterNameWindow(row.SelectedAdapter.Name).ShowDialog<string?>(desktop.MainWindow);
        if (name == null || !CanEdit) return;
        IsBusy = true;
        try { Status = await DataTestNetworkConfigurator.RenameAsync(adapterId, name) ?? "Карта переименована."; }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; }
        var result = Status; Refresh(); Status = result;
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task EditAddressPlan()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            var saved = await new TestBuilder.Views.NetworkPlanWindow().ShowDialog<bool>(desktop.MainWindow);
            if (!saved) return;
            foreach (var row in Rows) row.SelectionChanged -= SelectionChanged;
            Rows.Clear();
            foreach (var (ip, index) in DataTestNetworkConfigurator.BenchIps.Select((ip, index) => (ip, index)))
            {
                var row = CreateRow(ip, index);
                row.SelectionChanged += SelectionChanged; Rows.Add(row);
            }
            _draft = null; Refresh();
        }
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
            AppSettings.Instance.SaveChecked();

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
