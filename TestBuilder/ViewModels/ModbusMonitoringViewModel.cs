using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;

namespace TestBuilder.ViewModels
{
    public class ModbusMonitoringViewModel : INotifyPropertyChanged
    {
        private readonly SlaveManager _slaveManager;
        private readonly ModbusService _modbusService;
        private CancellationTokenSource? _cts;

        public bool IsConnected => _modbusService.IsConnected;

        public ObservableCollection<SlaveModelBase> Slaves => _slaveManager.Slaves;

        private bool _isMonitoring;
        public bool IsMonitoring
        {
            get => _isMonitoring;
            private set
            {
                if (_isMonitoring == value) return;
                _isMonitoring = value;
                OnPropertyChanged();
            }
        }

        private bool _isScanning;
        public bool IsScanning
        {
            get => _isScanning;
            private set
            {
                if (_isScanning == value) return;
                _isScanning = value;
                OnPropertyChanged();
            }
        }

        private bool _verboseLogging;
        public bool VerboseLogging
        {
            get => _verboseLogging;
            set
            {
                if (_verboseLogging == value) return;
                _verboseLogging = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VerboseButtonText));
                TestingLogger.Info(value ? "Verbose логи включены" : "Verbose логи выключены");
            }
        }

        public string VerboseButtonText => VerboseLogging ? "Verbose: ON" : "Verbose: OFF";

        public AsyncRelayCommand ScanCommand { get; }
        public RelayCommand ToggleVerboseCommand { get; }
        public ILogger TestingLogger { get; }

        public ModbusMonitoringViewModel(SlaveManager slaveManager, ModbusService modbusService, ILogger testingLogger)
        {
            _slaveManager = slaveManager;
            _modbusService = modbusService;
            TestingLogger = testingLogger;
            ScanCommand = new AsyncRelayCommand(ScanAndStartAsync);
            ToggleVerboseCommand = new RelayCommand(() => VerboseLogging = !VerboseLogging);
        }

        public async Task ScanAndStartAsync()
        {
            if (!_modbusService.IsConnected)
                return;
            Stop();

            IsScanning = true;
            try
            {
                await _slaveManager.ScanAsync();
            }
            finally
            {
                IsScanning = false;
            }

            await StartAsync();
        }

        public async Task StartAsync()
        {
            if (IsMonitoring) return;

            _cts = new CancellationTokenSource();
            IsMonitoring = true;
            _ = MonitorLoop(_cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            IsMonitoring = false;
        }

        private async Task MonitorLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                foreach (var slave in Slaves)
                {
                    try
                    {
                        await slave.PollAsync();

                        if (VerboseLogging)
                        {
                            foreach (var reg in slave.RegisterItems)
                                TestingLogger.Debug($"Slave {slave.SlaveId} | {reg.Name} ({reg.Address}) = {reg.Value}");
                        }
                    }
                    catch { }
                }

                await Task.Delay(1000, token);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}