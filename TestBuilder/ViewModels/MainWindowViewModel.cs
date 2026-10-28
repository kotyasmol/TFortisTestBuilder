using TestBuilder.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.ComponentModel;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services.Modbus;

namespace TestBuilder.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase, IDisposable
    {
        // Сервис Modbus и менеджер слейвов — единые для всех VM
        public ModbusService ModbusService { get; }
        public SlaveManager SlaveManager { get; }

        [ObservableProperty]
        private bool _isSlavesFound = false;

        // Вложенные VM для вкладок
        public TestViewModel TestVM { get; }
        public ModbusMonitoringViewModel ModbusVM { get; }
        public SelfTestPageViewModel SelfTestPageVM { get; }
        public SettingsViewModel SettingsVM { get; }
        public NetworkSetupViewModel NetworkSetupVM { get; }
        public LabelPrintingViewModel LabelPrintingVM { get; }
        public string OperatorName => string.IsNullOrWhiteSpace(App.StartupUserName) ? "Локальный оператор" : App.StartupUserName;
        public string SessionStatus => !App.StartupOptions.IsValid ? "Ошибка параметров запуска" :
            string.IsNullOrEmpty(App.StartupSessionId) ? "Локальный запуск" : "Сессия лаунчера получена";
        public bool IsEngineerWorkspace => SettingsVM.IsEngineerMode;
        public bool ShowBoardEditor => IsEngineerWorkspace && TestVM.IsBoardProfile;

        [ObservableProperty] private string selectedPage = "Editor";
        public bool IsEditorPage => SelectedPage == "Editor";
        public bool IsRegistersPage => SelectedPage == "Registers";
        public bool IsSelfTestPage => SelectedPage == "SelfTest" && ShowSwitchTools;
        public bool IsNetworkPage => SelectedPage == "Network" && ShowSwitchTools;
        public bool IsLabelsPage => SelectedPage == "Labels";
        public bool ShowSwitchTools => !TestVM.IsAutonomousStand;
        partial void OnSelectedPageChanged(string value)
        {
            OnPropertyChanged(nameof(IsEditorPage));
            OnPropertyChanged(nameof(IsRegistersPage));
            OnPropertyChanged(nameof(IsSelfTestPage));
            OnPropertyChanged(nameof(IsNetworkPage));
            OnPropertyChanged(nameof(IsLabelsPage));
        }
        [RelayCommand]
        private void SelectPage(string page)
        {
            if (page is not ("Editor" or "Registers" or "SelfTest" or "Network" or "Labels")) return;
            if (!ShowSwitchTools && page is "SelfTest" or "Network") return;
            SelectedPage = page;
        }

        public string WorkspaceTitle => IsSettingsOpen ? "Настройки" :
            IsEngineerWorkspace ? "Инженерный режим" : "Пульт стенда";
        public string SwitchWorkspaceTitle => IsEngineerWorkspace ? "Перейти в пульт" : "Открыть редактор";
        [RelayCommand]
        private void SwitchWorkspace()
        {
            SettingsVM.IsEngineerMode = !SettingsVM.IsEngineerMode;
        }

        [ObservableProperty] private bool isSettingsOpen;
        partial void OnIsSettingsOpenChanged(bool value) => OnPropertyChanged(nameof(WorkspaceTitle));

        [RelayCommand]
        private void OpenSettings() => IsSettingsOpen = true;

        [RelayCommand]
        private void CloseSettings() => IsSettingsOpen = false;

        public MainWindowViewModel()
        {
            ModbusService = new ModbusService();
            SlaveManager = new SlaveManager(ModbusService);

            TestVM = new TestViewModel(ModbusService, SlaveManager);
            ModbusVM = new ModbusMonitoringViewModel(SlaveManager, ModbusService, TestVM.TestingLogger);
            ModbusVM.ManagedMonitorActive = () => TestVM.IsMonitoringActive || TestVM.IsConnecting || TestVM.RootGraph.Hardware != null;
            ModbusVM.CanWrite = () => !TestVM.IsTestRunning && !TestVM.HasRunningQuickActions && !TestVM.IsConnecting;
            SelfTestPageVM = new SelfTestPageViewModel(TestVM.SelfTestPageState);
            SettingsVM = new SettingsViewModel(() => !TestVM.IsTestRunning && !TestVM.HasRunningQuickActions && !TestVM.IsConnecting);
            NetworkSetupVM = new NetworkSetupViewModel(DataTestNetworkConfigurator.GetAdapters,
                () => IsEngineerWorkspace && !TestVM.IsTestRunning && !TestVM.HasRunningQuickActions && !TestVM.IsConnecting);
            NetworkSetupVM.PropertyChanged += OnNetworkSetupChanged;
            LabelPrintingVM = new LabelPrintingViewModel(TestVM.TestingLogger,
                () => IsEngineerWorkspace && !TestVM.IsTestRunning && !TestVM.HasRunningQuickActions && !TestVM.IsConnecting);
            LabelPrintingVM.PropertyChanged += OnLabelPrintingChanged;

            TestVM.PropertyChanged += OnTestVmPropertyChanged;
            SettingsVM.PropertyChanged += OnSettingsPropertyChanged;
            TestVM.IsOperatorView = !IsEngineerWorkspace;
            RefreshProfileSettings();
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SettingsVM.IsEngineerMode)) return;
            TestVM.IsOperatorView = !IsEngineerWorkspace;
            if (!IsEngineerWorkspace && !TestVM.IsTestRunning) TestVM.Station.Prepare(TestVM.RootGraph);
            OnPropertyChanged(nameof(IsEngineerWorkspace));
            OnPropertyChanged(nameof(ShowBoardEditor));
            OnPropertyChanged(nameof(WorkspaceTitle));
            OnPropertyChanged(nameof(SwitchWorkspaceTitle));
            LabelPrintingVM.RefreshAvailability();
            NetworkSetupVM.RefreshAvailability();
        }

        private void OnTestVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(TestVM.IsBoardProfile) or nameof(TestVM.HasConsoleSettings))
            {
                OnPropertyChanged(nameof(ShowBoardEditor));
                RefreshProfileSettings();
            }
            if (e.PropertyName == nameof(TestVM.IsAutonomousStand))
            {
                OnPropertyChanged(nameof(ShowSwitchTools));
                if (!ShowSwitchTools && SelectedPage is "SelfTest" or "Network") SelectedPage = "Editor";
                OnSelectedPageChanged(SelectedPage);
            }
            if (e.PropertyName == nameof(TestVM.IsMonitoringActive))
                IsSlavesFound = TestVM.IsMonitoringActive;
            if (e.PropertyName is nameof(TestVM.IsTestRunning) or nameof(TestVM.IsConnecting) or nameof(TestVM.HasRunningQuickActions))
            {
                SettingsVM.RefreshTutorialAvailability();
                LabelPrintingVM.RefreshAvailability();
                NetworkSetupVM.RefreshAvailability();
            }
        }

        private void OnNetworkSetupChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NetworkSetupVM.IsBusy)) TestVM.IsNetworkSetupBusy = NetworkSetupVM.IsBusy;
        }

        private void OnLabelPrintingChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LabelPrintingVM.IsBusy)) TestVM.IsLabelToolBusy = LabelPrintingVM.IsBusy;
        }

        private void RefreshProfileSettings() =>
            SettingsVM.SetProfileContext(TestVM.ProfileProductKind, TestVM.HasConsoleSettings);

        public void Dispose()
        {
            TestVM.PropertyChanged -= OnTestVmPropertyChanged;
            SettingsVM.PropertyChanged -= OnSettingsPropertyChanged;
            SelfTestPageVM.Dispose();
            LabelPrintingVM.Dispose();
            LabelPrintingVM.PropertyChanged -= OnLabelPrintingChanged;
            NetworkSetupVM.PropertyChanged -= OnNetworkSetupChanged;
            TestVM.Dispose();
            ModbusVM.Dispose();
        }
    }
}
