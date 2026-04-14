using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Modbus.Models;
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

        public AsyncRelayCommand ScanCommand { get; }

        public ModbusMonitoringViewModel(SlaveManager slaveManager, ModbusService modbusService)
        {
            _slaveManager = slaveManager;
            _modbusService = modbusService;
            ScanCommand = new AsyncRelayCommand(ScanAndStartAsync);
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