using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Steps
{
    /// <summary>
    /// Шаг теста, который записывает указанное значение в регистр тестового контекста.
    /// Используется для подготовки состояния регистров перед проверками или другими шагами.
    /// </summary>
    public class ModbusWriteStep : ITestStep
    {
        private readonly IModbusService _modbusService;
        private readonly byte _slaveId;
        private readonly ushort _address;
        private readonly ushort _value;
        private readonly ILogger _logger;

        public ModbusWriteStep(
    IModbusService modbusService,
    ILogger logger,
    byte slaveId,
    ushort address,
    ushort value)
        {
            _modbusService = modbusService;
            _logger = logger;
            _slaveId = slaveId;
            _address = address;
            _value = value;
        }

        public async Task<StepResult> ExecuteAsync(
     TestContext context,
     CancellationToken cancellationToken)
        {
            _logger.Info($"ШАГ ЗАПИСИ: устройство={_slaveId}, регистр={_address}, значение={_value}");

            var writeOk = await _modbusService.WriteRegisterAsync(
                _slaveId,
                _address,
                _value,
                false,
                cancellationToken);

            if (!writeOk)
            {
                _logger.Info($"ШАГ ЗАПИСИ: ошибка записи. Устройство={_slaveId}, регистр={_address}, значение={_value}");
                return StepResult.False;
            }

            _logger.Info($"ШАГ ЗАПИСИ: запись выполнена. Ожидание 1 секунда перед проверкой.");

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            var readValues = await _modbusService.ReadRegistersAsync(
                _slaveId,
                _address,
                1,
                cancellationToken);

            if (readValues == null || readValues.Length == 0)
            {
                _logger.Info($"ШАГ ЗАПИСИ: ошибка проверки. Не удалось прочитать регистр. Устройство={_slaveId}, регистр={_address}");
                return StepResult.False;
            }

            var actualValue = readValues[0];

            if (actualValue != _value)
            {
                _logger.Info(
                    $"ШАГ ЗАПИСИ: проверка не пройдена. Устройство={_slaveId}, регистр={_address}, ожидалось={_value}, прочитано={actualValue}");

                return StepResult.False;
            }

            _logger.Info(
                $"ШАГ ЗАПИСИ: проверка пройдена. Устройство={_slaveId}, регистр={_address}, значение={actualValue}");

            return StepResult.True;
        }
    }
}
