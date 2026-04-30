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
                    $"[ШАГ] Запись регистра → устройство не задано, адрес {_address}, значение {_value}.");

                return StepResult.False;
            }

            _logger.Info(
                $"[ШАГ] Запись регистра → устройство {actualSlaveId}, адрес {_address}, значение {_value}.");

            var writeOk = await _modbusService.WriteRegisterAsync(
                actualSlaveId.Value,
                _address,
                _value,
                false,
                cancellationToken);

            if (!writeOk)
            {
                _logger.Warning(
                    $"[ОШИБКА] Запись не выполнена. Устройство {actualSlaveId}, адрес {_address}, значение {_value}.");

                return StepResult.False;
            }

            _logger.Info("[OK] Запись выполнена. Ожидание подтверждения...");

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            var readValues = await _modbusService.ReadRegistersAsync(
                actualSlaveId.Value,
                _address,
                1,
                cancellationToken);

            if (readValues == null || readValues.Length == 0)
            {
                _logger.Warning(
                    $"[ОШИБКА] Не удалось прочитать регистр для проверки. Устройство {actualSlaveId}, адрес {_address}.");

                return StepResult.False;
            }

            var actualValue = readValues[0];

            if (actualValue != _value)
            {
                _logger.Warning(
                    $"[ОШИБКА] Значение не совпадает. Ожидалось {_value}, прочитано {actualValue}. Устройство {actualSlaveId}, адрес {_address}.");

                return StepResult.False;
            }

            _logger.Info(
                $"[OK] Значение подтверждено: {actualValue}. Устройство {actualSlaveId}, адрес {_address}.");

            return StepResult.True;
        }

        private byte? ResolveSlaveId(TestContext context)
        {
            return _useCurrentSlaveId ? context.CurrentSlaveId : _slaveId;
        }
    }
}