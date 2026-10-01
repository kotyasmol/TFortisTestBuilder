using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using TestBuilder.Services;
using TestBuilder.Services.Graph;

namespace TestBuilder.ViewModels;

public partial class TestViewModel
{
    private readonly bool _allowEditing;
    private string _profileLoadError = string.Empty;
    public StationDashboardViewModel Station { get; } = new();
    [ObservableProperty] private bool isOperatorView;
    [ObservableProperty] private bool isConnecting;
    [ObservableProperty] private bool isStopping;
    [ObservableProperty] private bool isPauseEffective;
    [ObservableProperty] private string stationProfileSearch = string.Empty;
    private readonly List<GraphProfile> _profileCatalog = new();
    private bool _refreshingProfiles;
    public ObservableCollection<GraphProfile> StationProfiles { get; } = new();
    public ObservableCollection<StationProfileGroupViewModel> StationProfileGroups { get; } = new();
    private readonly Dictionary<string, StationProfileGroupViewModel> _stationGroups = new(System.StringComparer.OrdinalIgnoreCase);
    public GraphProfile? SelectedStationProfile
    {
        get => SelectedProfile != null && StationProfiles.Contains(SelectedProfile) ? SelectedProfile : null;
        set { if (value != null) SelectedProfile = value; }
    }

    private void RefreshStationProfiles()
    {
        var visible = _profileCatalog.Where(p =>
            string.IsNullOrWhiteSpace(StationProfileSearch) ||
            p.DisplayName.Contains(StationProfileSearch, System.StringComparison.OrdinalIgnoreCase) ||
            p.ModelGroup.Contains(StationProfileSearch, System.StringComparison.OrdinalIgnoreCase) ||
            p.ConfigurationName.Contains(StationProfileSearch, System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.DeviceModel == null)
            .ThenBy(p => p.ModelGroup, System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.ConfigurationName != "Полная проверка")
            .ThenBy(p => p.ConfigurationName, System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.FilePath, System.StringComparer.OrdinalIgnoreCase).ToArray();
        SynchronizeStationItems(StationProfiles, visible);

        var groups = new List<StationProfileGroupViewModel>();
        foreach (var profiles in visible.GroupBy(p => p.ModelGroup, System.StringComparer.OrdinalIgnoreCase))
        {
            if (!_stationGroups.TryGetValue(profiles.Key, out var group))
                _stationGroups.Add(profiles.Key, group = new StationProfileGroupViewModel(profiles.Key));
            var options = profiles.Select(p => group.Profiles.FirstOrDefault(o => ReferenceEquals(o.Profile, p))
                ?? new StationProfileOptionViewModel(p, selected => SelectedProfile = selected)).ToArray();
            SynchronizeStationItems(group.Profiles, options);
            if (!string.IsNullOrWhiteSpace(StationProfileSearch)) group.IsExpanded = true;
            groups.Add(group);
        }
        SynchronizeStationItems(StationProfileGroups, groups);
        UpdateStationProfileSelection();
        OnPropertyChanged(nameof(SelectedStationProfile));
    }

    private static void SynchronizeStationItems<T>(ObservableCollection<T> collection, IReadOnlyList<T> items)
    {
        for (var i = collection.Count - 1; i >= 0; i--)
            if (!items.Contains(collection[i])) collection.RemoveAt(i);
        for (var i = 0; i < items.Count; i++)
        {
            var oldIndex = collection.IndexOf(items[i]);
            if (oldIndex < 0) collection.Insert(i, items[i]);
            else if (oldIndex != i) collection.Move(oldIndex, i);
        }
    }

    private void UpdateStationProfileSelection()
    {
        foreach (var group in _stationGroups.Values)
            foreach (var option in group.Profiles)
                option.IsSelected = option.Profile.FilePath == SelectedProfile?.FilePath;
    }

    public string StationProfileName => SelectedProfile?.DeviceModel ?? SelectedProfile?.StationName ?? "Выберите программу";
    public string StationConfigurationCaption => SelectedProfile?.DeviceModel != null
        ? SelectedProfile.ConfigurationName + " · этапы теста" : "Этапы теста";
    public bool HasStationProfile => SelectedProfile != null && _profileLoadError.Length == 0;
    public bool CanConnectStation => !IsConnected && !IsTestRunning && !IsConnecting;
    public bool CanDisconnectStation => IsConnected && !IsTestRunning && !IsConnecting;
    public bool CanPauseTest => IsTestRunning && !IsTestPaused && !IsStopping;
    public bool CanResumeTest => IsTestRunning && IsTestPaused && !IsStopping;
    public string StationConnectionText => IsConnecting ? "Подключение…" :
        IsConnected ? (IsMonitoringActive ? "Стенд подключён" : "Стенд подключён · нет обмена") : "Стенд отключён";
    public bool IsStationReady => IsConnected && IsMonitoringActive && !IsConnecting;
    public string PauseStatus => IsPauseEffective ? "На паузе" : "Ожидание завершения текущих операций";
    public string PauseHint => IsPauseEffective ? "Нажмите «Продолжить», чтобы продолжить тест." :
        "Текущие операции во всех ветвях завершатся. Следующие будут ждать продолжения.";

    [RelayCommand(CanExecute = nameof(CanConnectStation))]
    private async Task ConnectStationAsync()
    {
        if (CanConnectStation) await ToggleConnectionAsync();
    }

    [RelayCommand(CanExecute = nameof(CanDisconnectStation))]
    private async Task DisconnectStationAsync()
    {
        if (CanDisconnectStation) await ToggleConnectionAsync();
    }

    partial void OnStationProfileSearchChanged(string value)
    {
        _refreshingProfiles = true;
        try
        {
            RefreshStationProfiles();
            OnPropertyChanged(nameof(SelectedProfile));
        }
        finally { _refreshingProfiles = false; }
    }

    partial void OnIsTestPausedChanged(bool value)
    {
        Station.IsPaused = value;
        RefreshStationAvailability();
    }
    partial void OnIsPauseEffectiveChanged(bool value)
    {
        Station.SetPaused(value);
        OnPropertyChanged(nameof(PauseStatus));
        OnPropertyChanged(nameof(PauseHint));
    }
    public bool CanEditGraph => _allowEditing && !IsOperatorView && !IsTestRunning;
    public bool CanChooseProfile => !IsTestRunning && !IsConnecting;
    public bool CanStartRun => !IsTestRunning && !IsConnecting && App.StartupOptions.IsValid &&
        _profileLoadError.Length == 0 &&
        (!IsOperatorView || (_selectedProfile != null && _currentProfilePath != null &&
            (!GraphConnectionRequirements.RequiresStandConnection(RootGraph) || (IsConnected && IsMonitoringActive))));
    public string StationReadiness => !App.StartupOptions.IsValid ? App.StartupOptions.Error :
        _profileLoadError.Length > 0 ? "Программа не загружена. Выберите другой профиль или обратитесь к инженеру." :
        IsConnecting ? "Ищем стенд. Дождитесь окончания подключения." :
        IsTestRunning ? (IsStopping ? "Выполняем завершающие действия" : "Стенд занят проверкой") :
        _currentProfilePath == null || _selectedProfile == null ? "Выберите программу проверки" :
        !GraphConnectionRequirements.RequiresStandConnection(RootGraph) ? "Готово · подключение к стенду не требуется" :
        IsConnected && IsMonitoringActive ? "Стенд готов к проверке" : "Подключите стенд перед запуском";

    partial void OnIsOperatorViewChanged(bool value) => RefreshStationAvailability();
    partial void OnIsConnectingChanged(bool value) => RefreshStationAvailability();
    partial void OnIsStoppingChanged(bool value) => RefreshStationAvailability();
    partial void OnIsTestRunningChanged(bool value) => RefreshStationAvailability();
    partial void OnIsConnectedChanged(bool value) => RefreshStationAvailability();
    partial void OnIsMonitoringActiveChanged(bool value) => RefreshStationAvailability();

    private void RefreshStationAvailability()
    {
        OnPropertyChanged(nameof(CanEditGraph));
        OnPropertyChanged(nameof(CanChooseProfile));
        OnPropertyChanged(nameof(CanStartRun));
        OnPropertyChanged(nameof(StationReadiness));
        OnPropertyChanged(nameof(StationProfileName));
        OnPropertyChanged(nameof(StationConfigurationCaption));
        OnPropertyChanged(nameof(HasStationProfile));
        OnPropertyChanged(nameof(StationConnectionText));
        OnPropertyChanged(nameof(IsStationReady));
        OnPropertyChanged(nameof(CanConnectStation));
        OnPropertyChanged(nameof(CanDisconnectStation));
        OnPropertyChanged(nameof(CanPauseTest));
        OnPropertyChanged(nameof(CanResumeTest));
        ConnectStationCommand.NotifyCanExecuteChanged();
        DisconnectStationCommand.NotifyCanExecuteChanged();
        RunGraphCommand?.NotifyCanExecuteChanged();
        ToggleConnectionCommand?.NotifyCanExecuteChanged();
        foreach (var command in new object?[] { AddNodeCommand, DisconnectConnectorCommand,
            DeleteSelectedNodesCommand, ClearGraphCommand, NewProfileCommand, SaveGraphCommand,
            ImportProfilesCommand, UndoCommand, RedoCommand, StopTestCommand, PauseTestCommand, ResumeTestCommand })
            if (command is IRelayCommand relay) relay.NotifyCanExecuteChanged();
    }
}
