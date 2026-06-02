using CommunityToolkit.Mvvm.ComponentModel;
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
        public SettingsViewModel SettingsVM { get; }

        public MainWindowViewModel()
        {
            ModbusService = new ModbusService();
            SlaveManager = new SlaveManager(ModbusService);

            TestVM = new TestViewModel(ModbusService, SlaveManager);
            ModbusVM = new ModbusMonitoringViewModel(SlaveManager, ModbusService, TestVM.TestingLogger);
            SettingsVM = new SettingsViewModel();

            TestVM.PropertyChanged += OnTestVmPropertyChanged;
        }

        private void OnTestVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TestVM.IsMonitoringActive))
                IsSlavesFound = TestVM.IsMonitoringActive;
        }

        public void Dispose()
        {
            TestVM.PropertyChanged -= OnTestVmPropertyChanged;
            TestVM.Dispose();
            ModbusVM.Dispose();
        }
    }
}
