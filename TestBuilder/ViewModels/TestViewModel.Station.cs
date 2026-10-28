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
        var visible = _profileCatalog.Where(MatchesProductFilter).Where(p =>
            string.IsNullOrWhiteSpace(StationProfileSearch) ||
            EquipmentCatalog.Matches(p.DisplayName, StationProfileSearch) ||
            EquipmentCatalog.Matches(p.ModelGroup, StationProfileSearch) ||
            EquipmentCatalog.Matches(p.ConfigurationName, StationProfileSearch))
            .OrderBy(p => p.DeviceModel == null)
            .ThenBy(p => EquipmentCatalog.SortKey(p.ModelGroup), System.StringComparer.Ordinal)
            .ThenBy(p => p.ConfigurationName != "Полная проверка")
            .ThenBy(p => p.ConfigurationName, System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.FilePath, System.StringComparer.OrdinalIgnoreCase).ToArray();
        SynchronizeStationItems(StationProfiles, visible);

        var groups = new List<StationProfileGroupViewModel>();
        foreach (var profiles in visible.GroupBy(p => p.ModelGroupKey, System.StringComparer.OrdinalIgnoreCase))
        {
            if (!_stationGroups.TryGetValue(profiles.Key, out var group))
                _stationGroups.Add(profiles.Key, group = new StationProfileGroupViewModel(profiles.First().ModelGroup));
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

    public string StationProfileName => SelectedProfile?.DisplayName ?? "Выберите программу";
    public string StationConfigurationCaption => SelectedProfile?.DeviceModel != null
        ? SelectedProfile.ConfigurationName + " · этапы теста" : "Этапы теста";
    public bool HasStationProfile => SelectedProfile != null && _profileLoadError.Length == 0;
    public bool HasConnectionProfile => _currentProfilePath != null && _profileLoadError.Length == 0 && RootGraph.Hardware != null;
    public string ConnectionRequirements => _currentProfilePath == null ? "Подключитесь к стенду, затем выберите профиль." :
        RootGraph.Hardware == null ? "Оборудование профиля не задано." :
        "Проверяем на подключённом COM-порту: " + string.Join("; ", RootGraph.Hardware.Devices.Select(d =>
            d.ExpectedIdentificationValues.Count > 0
                ? $"slave {d.SlaveId} — {d.Name}, регистр {d.IdentificationAddress} = {string.Join(" или ", d.ExpectedIdentificationValues)}"
                : $"slave {d.SlaveId} — {d.Name}, чтение {d.Registers.Count} регистров"));
    public bool CanConnectStation => _profileLoadError.Length == 0 && !IsTraining && !IsConnected && !IsTestRunning && !IsConnecting && !IsNetworkSetupBusy;
    public bool CanDisconnectStation => IsConnected && !IsTestRunning && !IsConnecting;
    public bool CanPauseTest => IsTestRunning && !IsTestPaused && !IsStopping;
    public bool CanResumeTest => IsTestRunning && IsTestPaused && !IsStopping;
    public string StationConnectionText => IsConnecting ? "Подключение…" :
        IsConnected ? (IsMonitoringActive ? "Стенд подключён" : "COM-порт открыт · оборудование не проверено") : "Стенд отключён";
    public bool IsStationReady => !NeedsStandInspection && !HasPendingPreparation && IsConnected && IsMonitoringActive && !IsConnecting;
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
    public bool CanEditGraph => _allowEditing && !IsOperatorView && !IsTestRunning && !IsConnecting;
    public bool CanChooseProfile => !IsTestRunning && !IsConnecting;
    [ObservableProperty] private bool needsStandInspection;
    public string StandInspectionMessage => "Отключение стенда не подтверждено. Выключите питание стенда и обратитесь к инженеру перед следующим запуском.";
    partial void OnNeedsStandInspectionChanged(bool value) => RefreshStationAvailability();
    [RelayCommand]
    private void ConfirmStandInspection()
    {
        if (!NeedsStandInspection || IsOperatorView || IsTestRunning || IsConnecting || IsConnected) return;
        NeedsStandInspection = false;
        TestingLogger.Warning("Инженер подтвердил проверку и ручное обесточивание стенда. Перед запуском подключитесь заново.");
    }
    [ObservableProperty] private bool isLabelToolBusy;
    partial void OnIsLabelToolBusyChanged(bool value) => RefreshStationAvailability();
    [ObservableProperty] private bool isNetworkSetupBusy;
    partial void OnIsNetworkSetupBusyChanged(bool value) => RefreshStationAvailability();
    public bool CanStartRun => !IsNetworkSetupBusy && !IsLabelToolBusy && !NeedsStandInspection && !IsTestRunning && !IsConnecting && App.StartupOptions.IsValid &&
        _profileLoadError.Length == 0 && (!HasPendingPreparation || HasPartialRun) &&
        (!ProfileRequiresHardware || (IsConnected && IsMonitoringActive)) &&
        (!IsOperatorView || (_selectedProfile != null && _currentProfilePath != null &&
            (!GraphConnectionRequirements.RequiresStandConnection(RootGraph) || (IsConnected && IsMonitoringActive))));
    public string StationReadiness => !App.StartupOptions.IsValid ? App.StartupOptions.Error :
        IsNetworkSetupBusy ? "Дождитесь завершения настройки сети." :
        IsLabelToolBusy ? "Дождитесь завершения проверки печати этикетки." :
        NeedsStandInspection ? StandInspectionMessage :
        _profileLoadError.Length > 0 ? "Программа не загружена. Выберите другой профиль или обратитесь к инженеру." :
        IsConnecting ? "Подключение и проверка оборудования." :
        IsTestRunning ? (IsStopping ? "Выполняем завершающие действия" : _isPreparingProfile ? "Настройка стенда" : "Стенд занят проверкой") :
        _currentProfilePath == null || _selectedProfile == null ? (IsConnected ? "Выберите программу проверки" : "Подключитесь к стенду, затем выберите профиль") :
        RootGraph.Hardware == null && GraphConnectionRequirements.RequiresStandConnection(RootGraph) ? ConnectionRequirements :
        ProfileRequiresHardware && IsConnected && !IsMonitoringActive ? "Оборудование профиля не проверено. Откройте профиль заново." :
        HasPendingPreparation ? (!IsConnected && GraphConnectionRequirements.RequiresStandConnection(RootGraph)
            ? "Подключите стенд для подготовки профиля" : "Подготовка профиля не выполнена. Откройте профиль заново.") :
        !GraphConnectionRequirements.RequiresStandConnection(RootGraph) ? "Готово · подключение к стенду не требуется" :
        IsConnected && IsMonitoringActive ? "Стенд готов к проверке" : "Подключите стенд перед запуском";

    partial void OnIsOperatorViewChanged(bool value)
    {
        if (value && !IsTestRunning) ResetPartialRun();
        if (value) { Station.RefreshStageNames(); BoardChanged?.Invoke(); }
        RefreshStationAvailability();
    }
    partial void OnIsConnectingChanged(bool value) => RefreshStationAvailability();
    partial void OnIsStoppingChanged(bool value) => RefreshStationAvailability();
    partial void OnIsTestRunningChanged(bool value) => RefreshStationAvailability();
    partial void OnIsConnectedChanged(bool value)
    {
        RefreshStationAvailability();
        RefreshStandIdentity();
    }
    partial void OnIsMonitoringActiveChanged(bool value) => RefreshStationAvailability();

    private void RefreshStationAvailability()
    {
        OnPropertyChanged(nameof(CanEditGraph));
        OnPropertyChanged(nameof(CanPaste));
        OnPropertyChanged(nameof(CanEditHardware));
        OnPropertyChanged(nameof(CanChooseProfile));
        OnPropertyChanged(nameof(CanStartRun));
        OnPropertyChanged(nameof(StationReadiness));
        OnPropertyChanged(nameof(StationProfileName));
        OnPropertyChanged(nameof(StationConfigurationCaption));
        OnPropertyChanged(nameof(HasStationProfile));
        OnPropertyChanged(nameof(StationConnectionText));
        OnPropertyChanged(nameof(IsStationReady));
        OnPropertyChanged(nameof(HasConnectionProfile));
        OnPropertyChanged(nameof(ConnectionRequirements));
        OnPropertyChanged(nameof(CanConnectStation));
        OnPropertyChanged(nameof(CanDisconnectStation));
        OnPropertyChanged(nameof(CanPauseTest));
        OnPropertyChanged(nameof(CanResumeTest));
        ConnectStationCommand.NotifyCanExecuteChanged();
        DisconnectStationCommand.NotifyCanExecuteChanged();
        RunGraphCommand?.NotifyCanExecuteChanged();
        ToggleConnectionCommand?.NotifyCanExecuteChanged();
        foreach (var command in new object?[] { AddNodeCommand, DisconnectConnectorCommand,
            DeleteSelectedNodesCommand, ClearGraphCommand, ClearPartialRunCommand, NewProfileCommand, SaveGraphCommand,
            ImportProfilesCommand, UndoCommand, RedoCommand, StopTestCommand, PauseTestCommand, ResumeTestCommand })
            if (command is IRelayCommand relay) relay.NotifyCanExecuteChanged();
    }
}
