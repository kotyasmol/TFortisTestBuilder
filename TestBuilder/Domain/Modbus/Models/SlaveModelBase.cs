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

        protected void OnPropertyChanged([CallerMemberName] string prop = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
