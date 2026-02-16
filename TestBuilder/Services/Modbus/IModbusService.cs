using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Services.Modbus
{
    public interface IModbusService
    {
        /// <summary>
        /// Читает последовательный диапазон регистров Holding Registers.
        /// </summary>
        Task<ushort[]> ReadRegistersAsync(
            byte slaveId,
            ushort address,
            ushort count,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Записывает одиночный регистр и опционально проверяет запись.
        /// </summary>
        Task<bool> WriteRegisterAsync(
            byte slaveId,
            ushort address,
            ushort value,
            bool verify = true,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Проверяет, что текущее соединение с портом рабочее
        /// (обычно пробным чтением регистра).
        /// </summary>
        Task<bool> CheckPortAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Подписка на изменения конкретного регистра. Реализация сама
        /// организует периодический опрос и вызывает callback при изменениях.
        /// Используется, например, в DiagnosticViewModel.
        /// </summary>
        void SubscribeRegister(byte slaveId, ushort address, Action<ushort[]> callback);
    }
}
