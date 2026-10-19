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
        private readonly TestBuilder.Domain.Modbus.ModbusArea _area;
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
            bool verifyWrite = false, TestBuilder.Domain.Modbus.ModbusArea area = TestBuilder.Domain.Modbus.ModbusArea.Holding)
        {
            _modbusService = modbusService;
            _logger = logger;
            _area = area;
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
            if (!context.IsCleanup) return await ExecuteCoreAsync(context, cancellationToken);
            // Bound each attempt, including recovery and readback, so one missing output
            // cannot consume the entire shutdown budget. Await transport completion;
            // never abandon a write that could continue after the next action starts.
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attempt.CancelAfter(TimeSpan.FromSeconds(5));
            try { return await ExecuteCoreAsync(context, attempt.Token); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                DiagnosticLog.WriteException("Аварийная запись регистра", ex);
                _logger.Warning($"Не подтверждено отключение: устройство {ResolveSlaveId(context)}, регистр {_address}. " +
                    (attempt.IsCancellationRequested ? "Истекло время попытки." : "Ошибка обмена со стендом."));
                return StepResult.False;
            }
        }

        private async Task<StepResult> ExecuteCoreAsync(TestContext context, CancellationToken cancellationToken)
        {
            var actualSlaveId = ResolveSlaveId(context);
            var registerAddress = context.ModbusRegisters.FormatAddress(actualSlaveId, _address, _area);

            if (actualSlaveId == null)
            {
                _logger.Warning(
                    $"[ШАГ] Запись регистра → устройство не задано, адрес {registerAddress}, значение {_value}.");

                return StepResult.False;
            }

            _logger.Info(
                $"[ШАГ] Запись регистра → устройство {actualSlaveId}, адрес {registerAddress}, значение {_value}.");

            var writeOk = await _modbusService.WriteAreaAsync(
                actualSlaveId.Value,
                _address,
                _value,
                _area,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (!writeOk)
            {
                _logger.Warning(
                    $"[ОШИБКА] Запись не выполнена. Устройство {actualSlaveId}, адрес {registerAddress}, значение {_value}.");

                return StepResult.False;
            }

            if (context.RequireFreshMonitoring) context.RequiredPollAfter[actualSlaveId.Value] = System.Diagnostics.Stopwatch.GetTimestamp();

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
                    readValues = await _modbusService.ReadAreaAsync(
                        actualSlaveId.Value,
                        _address,
                        1, _area,
                        cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
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
                    context.RegisterState.Update(actualSlaveId.Value, _address, actualValue, _area);

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
