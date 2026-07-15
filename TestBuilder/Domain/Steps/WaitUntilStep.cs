using System;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Steps
{
    public class WaitUntilStep : ITestStep
    {
        private readonly byte _slaveId;
        private readonly int _address;
        private readonly int _expectedValue;
        private readonly int _timeoutMs;
        private readonly bool _useCurrentSlaveId;
        private readonly IModbusService? _modbusService;
        private readonly bool _liveRead;
        private readonly ILogger _logger;

        private const int PollIntervalMs = 200;

        public WaitUntilStep(
            byte slaveId,
            int address,
            int expectedValue,
            int timeoutMs,
            ILogger logger,
            bool useCurrentSlaveId = false,
            IModbusService? modbusService = null,
            bool liveRead = false)
        {
            _slaveId = slaveId;
            _address = address;
            _expectedValue = expectedValue;
            _timeoutMs = timeoutMs;
            _logger = logger;
            _useCurrentSlaveId = useCurrentSlaveId;
            _modbusService = modbusService;
            _liveRead = liveRead;
        }

        public async Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            var actualSlaveId = _useCurrentSlaveId ? context.CurrentSlaveId : _slaveId;

            if (actualSlaveId == null)
            {
                _logger.Warning(
                    $"[ШАГ] Ожидание значения -> устройство не задано, адрес {_address}, ожидалось {_expectedValue}.");

                return StepResult.False;
            }

            _logger.Info(
                $"[ШАГ] Ожидание значения -> устройство {actualSlaveId}, адрес {_address}, ожидаемое {_expectedValue}, таймаут {_timeoutMs}мс, источник {(_liveRead ? "live Modbus" : "RegisterState")}.");

            var deadline = DateTime.UtcNow.AddMilliseconds(_timeoutMs);
            string lastError = string.Empty;

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var read = await ModbusRegisterReadHelper.ReadAsync(
                    context,
                    _modbusService,
                    actualSlaveId.Value,
                    _address,
                    _liveRead,
                    cancellationToken);

                if (read.Success && read.Value == _expectedValue)
                {
                    _logger.Info(
                        $"[OK] Устройство {actualSlaveId}, адрес {_address}: получено значение {read.Value}.");

                    return StepResult.True;
                }

                lastError = read.Success
                    ? $"последнее значение {read.Value}"
                    : read.Error;

                await Task.Delay(PollIntervalMs, cancellationToken);
            }

            _logger.Warning(
                $"[ОШИБКА] Таймаут {_timeoutMs}мс истёк. Устройство {actualSlaveId}, адрес {_address}, ожидалось {_expectedValue}; {lastError}.");

            return StepResult.False;
        }
    }
}
