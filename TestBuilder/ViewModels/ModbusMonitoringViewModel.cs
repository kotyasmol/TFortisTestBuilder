using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Modbus.Models;

namespace TestBuilder.ViewModels
{
    public class ModbusMonitoringViewModel : INotifyPropertyChanged
    {
        private readonly SlaveManager _slaveManager;
        private CancellationTokenSource? _cts;

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

        public ModbusMonitoringViewModel(SlaveManager slaveManager)
        {
            _slaveManager = slaveManager;
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
                    catch
                    {
                        // можно логировать ошибки опроса
                    }
                }

                await Task.Delay(1000, token);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
