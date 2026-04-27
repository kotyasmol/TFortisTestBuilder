using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Modbus.Models
{
    public abstract class SlaveModelBase : INotifyPropertyChanged
    {
        public IModbusService Modbus { get; }

        public byte SlaveId { get; }

        public ObservableCollection<RegisterItem> RegisterItems { get; protected set; } = new();

        public abstract string DeviceType { get; }

        public override string ToString() => $"{SlaveId} — {DeviceType}";

        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ToggleIcon));
            }
        }

        public string ToggleIcon => IsExpanded ? "▲" : "▼";

        // <summary>
        /// Высота DataGrid — 32px на каждый регистр + 36px заголовок
        /// </summary>
        public double GridHeight => RegisterItems.Count * 33 + 36;

        private RegisterItem? _selectedRegister;
        public RegisterItem? SelectedRegister
        {
            get => _selectedRegister;
            set
            {
                // Если кликнули на RO регистр — игнорируем
                if (value?.IsReadOnly == true)
                    value = null;
                _selectedRegister = value;
                OnPropertyChanged();
            }
        }

        public IRelayCommand ToggleExpandedCommand { get; }

        protected SlaveModelBase(byte slaveId, IModbusService modbus)
        {
            SlaveId = slaveId;
            Modbus = modbus ?? throw new ArgumentNullException(nameof(modbus));
            ToggleExpandedCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        }

        public abstract Task PollAsync();

        /// <summary>
        /// Обновляет RegisterItems из массива значений в UI-потоке.
        /// Вызывай это в конце каждого PollAsync вместо ручного цикла.
        /// </summary>
        protected Task UpdateRegisterItemsAsync(ushort[] regs)
        {
            return Dispatcher.UIThread.InvokeAsync(() =>
            {
                for (int i = 0; i < regs.Length && i < RegisterItems.Count; i++)
                    RegisterItems[i].Value = regs[i];
            }).GetTask();
        }

        protected void OnPropertyChanged([CallerMemberName] string prop = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}