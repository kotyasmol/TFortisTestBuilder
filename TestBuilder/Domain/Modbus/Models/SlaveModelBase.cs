using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Threading;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Modbus.Models
{
    public abstract class SlaveModelBase : INotifyPropertyChanged
    {
        protected IModbusService Modbus { get; }

        public byte SlaveId { get; }

        public ObservableCollection<RegisterItem> RegisterItems { get; protected set; } = new();

        public abstract string DeviceType { get; }

        protected SlaveModelBase(byte slaveId, IModbusService modbus)
        {
            SlaveId = slaveId;
            Modbus = modbus ?? throw new ArgumentNullException(nameof(modbus));
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