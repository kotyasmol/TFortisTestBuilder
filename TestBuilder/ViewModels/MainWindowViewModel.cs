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
        public string OperatorName => string.IsNullOrWhiteSpace(App.StartupUserName) ? "Локальный оператор" : App.StartupUserName;
        public string SessionStatus => !App.StartupOptions.IsValid ? "Ошибка параметров запуска" :
            string.IsNullOrEmpty(App.StartupSessionId) ? "Локальный запуск" : "Сессия лаунчера получена";
        public bool IsEngineerWorkspace => SettingsVM.IsEngineerMode;

        [ObservableProperty] private bool isSettingsOpen;

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
            SelfTestPageVM = new SelfTestPageViewModel(TestVM.SelfTestPageState);
            SettingsVM = new SettingsViewModel(() => !TestVM.IsTestRunning && !TestVM.IsConnecting);
            NetworkSetupVM = new NetworkSetupViewModel();

            TestVM.PropertyChanged += OnTestVmPropertyChanged;
            SettingsVM.PropertyChanged += OnSettingsPropertyChanged;
            TestVM.IsOperatorView = !IsEngineerWorkspace;
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SettingsVM.IsEngineerMode)) return;
            TestVM.IsOperatorView = !IsEngineerWorkspace;
            if (!IsEngineerWorkspace) TestVM.Station.Prepare(TestVM.RootGraph);
            OnPropertyChanged(nameof(IsEngineerWorkspace));
        }

        private void OnTestVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TestVM.IsMonitoringActive))
                IsSlavesFound = TestVM.IsMonitoringActive;
            if (e.PropertyName is nameof(TestVM.IsTestRunning) or nameof(TestVM.IsConnecting))
                SettingsVM.RefreshModeAvailability();
        }

        public void Dispose()
        {
            TestVM.PropertyChanged -= OnTestVmPropertyChanged;
            SettingsVM.PropertyChanged -= OnSettingsPropertyChanged;
            SelfTestPageVM.Dispose();
            TestVM.Dispose();
            ModbusVM.Dispose();
        }
    }
}
