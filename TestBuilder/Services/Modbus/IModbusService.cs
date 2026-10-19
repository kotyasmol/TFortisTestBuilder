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
        Task<ushort[]> ReadAreaAsync(byte slaveId, ushort address, ushort count,
            TestBuilder.Domain.Modbus.ModbusArea area, CancellationToken cancellationToken = default)
            => area == TestBuilder.Domain.Modbus.ModbusArea.Holding ? ReadRegistersAsync(slaveId, address, count, cancellationToken)
                : throw new NotSupportedException("Эта область Modbus не поддерживается сервисом.");
        Task<bool> WriteAreaAsync(byte slaveId, ushort address, ushort value,
            TestBuilder.Domain.Modbus.ModbusArea area, CancellationToken cancellationToken = default)
            => area == TestBuilder.Domain.Modbus.ModbusArea.Holding ? WriteRegisterAsync(slaveId, address, value, false, cancellationToken)
                : throw new NotSupportedException("Запись этой области Modbus не поддерживается сервисом.");

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
