using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Modbus.Models
{
    public abstract class SlaveModelBase : INotifyPropertyChanged
    {
        protected IModbusService Modbus { get; }

        public byte SlaveId { get; }

        public ObservableCollection<RegisterItem> RegisterItems { get; protected set; } = new();

        public abstract string DeviceType { get; }

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
                // Подставляем текущее значение в поле ввода
                if (_selectedRegister != null)
                    WriteValue = _selectedRegister.Value.ToString();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsWritePanelVisible));
            }
        }

        public bool IsWritePanelVisible => SelectedRegister != null;

        private string _writeValue = "0";
        public string WriteValue
        {
            get => _writeValue;
            set
            {
                _writeValue = value;
                OnPropertyChanged();
            }
        }

        public IRelayCommand ToggleExpandedCommand { get; }
        public IAsyncRelayCommand WriteRegisterCommand { get; }
        public IRelayCommand ClearSelectionCommand { get; }

        protected SlaveModelBase(byte slaveId, IModbusService modbus)
        {
            SlaveId = slaveId;
            Modbus = modbus ?? throw new ArgumentNullException(nameof(modbus));
            ToggleExpandedCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
            WriteRegisterCommand = new AsyncRelayCommand(WriteRegisterAsync);
            ClearSelectionCommand = new RelayCommand(() => SelectedRegister = null);
        }

        public abstract Task PollAsync();

        /// <summary>
        /// Записывает значение из поля WriteValue в выбранный регистр.
        /// </summary>
        private async Task WriteRegisterAsync()
        {
            if (SelectedRegister == null) return;
            if (!ushort.TryParse(WriteValue, out ushort val)) return;

            await Modbus.WriteRegisterAsync(SlaveId, (ushort)SelectedRegister.Address, val);
            await PollAsync();
        }

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