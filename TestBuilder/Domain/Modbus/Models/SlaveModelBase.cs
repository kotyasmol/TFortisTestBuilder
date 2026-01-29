using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Modbus.Models
{
    /// <summary>
    /// Базовая модель устройства/слейва Modbus.
    /// Все устройства наследуются от этого класса.
    /// </summary>
    public abstract class SlaveModelBase : INotifyPropertyChanged
    {
        protected IModbusService Modbus { get; }

        public byte SlaveId { get; }

        /// <summary>
        /// Список регистров устройства, которые опрашиваются.
        /// </summary>
        public List<RegisterItem> RegisterItems { get; protected set; } = new();

        protected SlaveModelBase(byte slaveId, IModbusService modbus)
        {
            SlaveId = slaveId;
            Modbus = modbus ?? throw new ArgumentNullException(nameof(modbus));
        }

        /// <summary>
        /// Метод опроса устройства. Должен быть реализован в наследниках.
        /// </summary>
        public abstract Task PollAsync();

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Вызывает событие изменения свойства для UI и других подписчиков.
        /// </summary>
        protected void OnPropertyChanged([CallerMemberName] string prop = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        #endregion
    }
}
