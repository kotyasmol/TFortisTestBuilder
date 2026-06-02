using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;

namespace TestBuilder.ViewModels
{
    public class ModbusMonitoringViewModel : ViewModelBase, IDisposable
    {
        private readonly SlaveManager _slaveManager;
        private readonly ModbusService _modbusService;
        private CancellationTokenSource? _cts;
        private Task? _monitorTask;

        public bool IsConnected => _modbusService.IsConnected;

        public ObservableCollection<SlaveModelBase> Slaves => _slaveManager.Slaves;

        private bool _isMonitoring;
        public bool IsMonitoring
        {
            get => _isMonitoring;
            private set => SetProperty(ref _isMonitoring, value);
        }

        private bool _isScanning;
        public bool IsScanning
        {
            get => _isScanning;
            private set => SetProperty(ref _isScanning, value);
        }

        public AsyncRelayCommand ScanCommand { get; }
        public ILogger TestingLogger { get; }

        public ModbusMonitoringViewModel(SlaveManager slaveManager, ModbusService modbusService, ILogger testingLogger)
        {
            _slaveManager = slaveManager ?? throw new ArgumentNullException(nameof(slaveManager));
            _modbusService = modbusService ?? throw new ArgumentNullException(nameof(modbusService));
            TestingLogger = testingLogger ?? throw new ArgumentNullException(nameof(testingLogger));
            ScanCommand = new AsyncRelayCommand(ScanAndStartAsync);

            // Подписываемся на изменение состояния подключения —
            // уведомляем UI что IsConnected изменилось
            _modbusService.IsConnectedChanged += OnModbusConnectionChanged;
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

        public Task StartAsync()
        {
            if (_monitorTask is { IsCompleted: false })
                return Task.CompletedTask;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            IsMonitoring = true;
            _monitorTask = Task.Run(() => MonitorLoop(token), token);
            return Task.CompletedTask;
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            IsMonitoring = false;
        }

        private async Task MonitorLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                foreach (var slave in _slaveManager.GetSlavesSnapshot())
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

        private void OnModbusConnectionChanged(object? sender, EventArgs e)
            => OnPropertyChanged(nameof(IsConnected));

        public void Dispose()
        {
            Stop();
            _modbusService.IsConnectedChanged -= OnModbusConnectionChanged;
        }
    }
}
