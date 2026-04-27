using System;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Steps
{
    /// <summary>
    /// Шаг теста, который записывает указанное значение в Modbus-регистр.
    /// Может использовать фиксированный slaveId или текущий slaveId из цикла.
    /// После записи ждет 1 секунду и проверяет, что значение действительно записалось.
    /// </summary>
    public class ModbusWriteStep : ITestStep
    {
        private readonly IModbusService _modbusService;
        private readonly byte _slaveId;
        private readonly ushort _address;
        private readonly ushort _value;
        private readonly bool _useCurrentSlaveId;
        private readonly ILogger _logger;

        public ModbusWriteStep(
            IModbusService modbusService,
            ILogger logger,
            byte slaveId,
            ushort address,
            ushort value,
            bool useCurrentSlaveId = false)
        {
            _modbusService = modbusService;
            _logger = logger;
            _slaveId = slaveId;
            _address = address;
            _value = value;
            _useCurrentSlaveId = useCurrentSlaveId;
        }

        public async Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            var actualSlaveId = ResolveSlaveId(context);

            if (actualSlaveId == null)
            {
                _logger.Warning(
                    $"ШАГ ЗАПИСИ: slaveId не задан. Регистр={_address}, значение={_value}.");

                return StepResult.False;
            }

            _logger.Info(
                $"ШАГ ЗАПИСИ: устройство={actualSlaveId}, регистр={_address}, значение={_value}.");

            var writeOk = await _modbusService.WriteRegisterAsync(
                actualSlaveId.Value,
                _address,
                _value,
                false,
                cancellationToken);

            if (!writeOk)
            {
                _logger.Warning(
                    $"ШАГ ЗАПИСИ: ошибка записи. Устройство={actualSlaveId}, регистр={_address}, значение={_value}.");

                return StepResult.False;
            }

            _logger.Info("ШАГ ЗАПИСИ: запись выполнена. Ожидание 1 секунда перед проверкой.");

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            var readValues = await _modbusService.ReadRegistersAsync(
                actualSlaveId.Value,
                _address,
                1,
                cancellationToken);

            if (readValues == null || readValues.Length == 0)
            {
                _logger.Warning(
                    $"ШАГ ЗАПИСИ: ошибка проверки. Не удалось прочитать регистр. Устройство={actualSlaveId}, регистр={_address}.");

                return StepResult.False;
            }

            var actualValue = readValues[0];

            if (actualValue != _value)
            {
                _logger.Warning(
                    $"ШАГ ЗАПИСИ: проверка не пройдена. Устройство={actualSlaveId}, регистр={_address}, ожидалось={_value}, прочитано={actualValue}.");

                return StepResult.False;
            }

            _logger.Info(
                $"ШАГ ЗАПИСИ: проверка пройдена. Устройство={actualSlaveId}, регистр={_address}, значение={actualValue}.");

            return StepResult.True;
        }

        private byte? ResolveSlaveId(TestContext context)
        {
            return _useCurrentSlaveId ? context.CurrentSlaveId : _slaveId;
        }
    }
}
