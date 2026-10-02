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
    /// Может проверить запись повторным чтением регистра.
    /// </summary>
    public class ModbusWriteStep : ITestStep
    {
        private const int VerifyAttempts = 3;
        private static readonly TimeSpan VerifyRetryDelay = TimeSpan.FromMilliseconds(300);

        private readonly IModbusService _modbusService;
        private readonly byte _slaveId;
        private readonly ushort _address;
        private readonly ushort _value;
        private readonly bool _useCurrentSlaveId;
        private readonly bool _verifyWrite;
        private readonly ILogger _logger;

        public ModbusWriteStep(
            IModbusService modbusService,
            ILogger logger,
            byte slaveId,
            ushort address,
            ushort value,
            bool useCurrentSlaveId = false,
            bool verifyWrite = false)
        {
            _modbusService = modbusService;
            _logger = logger;
            _slaveId = slaveId;
            _address = address;
            _value = value;
            _useCurrentSlaveId = useCurrentSlaveId;
            _verifyWrite = verifyWrite;
        }

        public async Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            var actualSlaveId = ResolveSlaveId(context);
            var registerAddress = context.ModbusRegisters.FormatAddress(actualSlaveId, _address);

            if (actualSlaveId == null)
            {
                _logger.Warning(
                    $"[ШАГ] Запись регистра → устройство не задано, адрес {registerAddress}, значение {_value}.");

                return StepResult.False;
            }

            _logger.Info(
                $"[ШАГ] Запись регистра → устройство {actualSlaveId}, адрес {registerAddress}, значение {_value}.");

            var writeOk = await _modbusService.WriteRegisterAsync(
                actualSlaveId.Value,
                _address,
                _value,
                false,
                cancellationToken);

            if (!writeOk)
            {
                _logger.Warning(
                    $"[ОШИБКА] Запись не выполнена. Устройство {actualSlaveId}, адрес {registerAddress}, значение {_value}.");

                return StepResult.False;
            }

            if (!_verifyWrite)
            {
                _logger.Info($"[OK] Запись выполнена. Устройство {actualSlaveId}, адрес {registerAddress}.");
                return StepResult.True;
            }

            _logger.Info($"[OK] Запись выполнена. Устройство {actualSlaveId}, адрес {registerAddress}. Проверка значения...");

            var lastVerificationError = string.Empty;
            for (var attempt = 1; attempt <= VerifyAttempts; attempt++)
            {
                if (attempt > 1)
                {
                    await Task.Delay(VerifyRetryDelay, cancellationToken);
                }

                ushort[] readValues;

                try
                {
                    readValues = await _modbusService.ReadRegistersAsync(
                        actualSlaveId.Value,
                        _address,
                        1,
                        cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastVerificationError = $"Ошибка чтения: {ex.Message}";
                    _logger.Warning(
                        $"[ОШИБКА] Ошибка чтения регистра для проверки. Попытка {attempt}/{VerifyAttempts}. Устройство {actualSlaveId}, адрес {registerAddress}: {ex.Message}");

                    continue;
                }

                if (readValues == null || readValues.Length == 0)
                {
                    lastVerificationError = "Регистр не прочитан: получен пустой ответ.";
                    _logger.Warning(
                        $"[ОШИБКА] Не удалось прочитать регистр для проверки. Попытка {attempt}/{VerifyAttempts}. Устройство {actualSlaveId}, адрес {registerAddress}.");

                    continue;
                }

                var actualValue = readValues[0];

                if (actualValue == _value)
                {
                    context.RegisterState.Update(actualSlaveId.Value, _address, actualValue);

                    _logger.Info(
                        $"[OK] Значение подтверждено: {actualValue}. Устройство {actualSlaveId}, адрес {registerAddress}.");

                    return StepResult.True;
                }

                lastVerificationError = $"Ожидалось {_value}, прочитано {actualValue}.";
                _logger.Warning(
                    $"[ОШИБКА] Значение не совпадает. Попытка {attempt}/{VerifyAttempts}. Ожидалось {_value}, прочитано {actualValue}. Устройство {actualSlaveId}, адрес {registerAddress}.");
            }

            _logger.Warning(
                $"[ОШИБКА] Проверка записи не пройдена после {VerifyAttempts} попыток. Устройство {actualSlaveId}, адрес {registerAddress}, значение {_value}. {lastVerificationError}");

            return StepResult.False;
        }

        private byte? ResolveSlaveId(TestContext context)
        {
            return _useCurrentSlaveId ? context.CurrentSlaveId : _slaveId;
        }
    }
}
