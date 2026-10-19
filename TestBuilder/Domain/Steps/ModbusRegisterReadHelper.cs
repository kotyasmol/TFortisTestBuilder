using System;
using System.Collections.Generic;
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
            CancellationToken cancellationToken, TestBuilder.Domain.Modbus.ModbusArea area = TestBuilder.Domain.Modbus.ModbusArea.Holding, int freshnessTimeoutMs = 10000)
        {
            if (!liveRead)
            {
                var barrier = context.RequiredPollAfter.GetValueOrDefault(slaveId);
                var deadline = System.Diagnostics.Stopwatch.StartNew();
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var oldestAllowed = context.RequireFreshMonitoring
                        ? System.Diagnostics.Stopwatch.GetTimestamp() - (long)(System.Diagnostics.Stopwatch.Frequency * (Math.Max(1, context.MaxRegisterAgeMs) / 1000.0)) : 0;
                    if (context.RegisterState.TryGet(slaveId, address, out var value, area, Math.Max(barrier, oldestAllowed)))
                        return new RegisterReadResult(true, value, string.Empty);
                    if (barrier == 0 && !context.RequireFreshMonitoring) break;
                    var remaining = freshnessTimeoutMs - (int)deadline.ElapsedMilliseconds;
                    if (remaining <= 0) break;
                    await Task.Delay(Math.Min(50, remaining), cancellationToken);
                } while (deadline.ElapsedMilliseconds < freshnessTimeoutMs);
                return new RegisterReadResult(false, 0, "Нет свежего значения мониторинга после команды; проверьте таблицу регистров и связь.");
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
                var values = await modbusService.ReadAreaAsync(
                    slaveId,
                    (ushort)address,
                    1, area,
                    cancellationToken);

                if (values.Length == 0)
                {
                    return new RegisterReadResult(false, 0, "Modbus вернул пустой ответ.");
                }

                var value = values[0];
                context.RegisterState.Update(slaveId, address, value, area);
                return new RegisterReadResult(true, value, string.Empty);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new RegisterReadResult(false, 0, ex.Message);
            }
        }
    }
}
