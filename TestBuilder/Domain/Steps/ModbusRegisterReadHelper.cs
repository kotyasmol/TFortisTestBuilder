using System;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Steps
{
    internal readonly record struct RegisterReadResult(
        bool Success,
        int Value,
        string Error);

    internal static class ModbusRegisterReadHelper
    {
        public static async Task<RegisterReadResult> ReadAsync(
            TestContext context,
            IModbusService? modbusService,
            byte slaveId,
            int address,
            bool liveRead,
            CancellationToken cancellationToken)
        {
            if (!liveRead)
            {
                return context.RegisterState.TryGet(slaveId, address, out var cachedValue)
                    ? new RegisterReadResult(true, cachedValue, string.Empty)
                    : new RegisterReadResult(false, 0, "Регистр не найден в состоянии мониторинга.");
            }

            if (modbusService == null)
            {
                return new RegisterReadResult(false, 0, "Live-чтение Modbus недоступно для этой ноды.");
            }

            if (address < ushort.MinValue || address > ushort.MaxValue)
            {
                return new RegisterReadResult(false, 0, $"Некорректный адрес регистра: {address}.");
            }

            try
            {
                var values = await modbusService.ReadRegistersAsync(
                    slaveId,
                    (ushort)address,
                    1,
                    cancellationToken);

                if (values.Length == 0)
                {
                    return new RegisterReadResult(false, 0, "Modbus вернул пустой ответ.");
                }

                var value = values[0];
                context.RegisterState.Update(slaveId, address, value);
                return new RegisterReadResult(true, value, string.Empty);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new RegisterReadResult(false, 0, ex.Message);
            }
        }
    }
}
