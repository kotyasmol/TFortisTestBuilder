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
        private readonly TestBuilder.Domain.Modbus.ModbusArea _area;
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
            bool liveRead = false, TestBuilder.Domain.Modbus.ModbusArea area = TestBuilder.Domain.Modbus.ModbusArea.Holding)
        {
            _area = area;
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
            var registerAddress = context.ModbusRegisters.FormatAddress(actualSlaveId, _address, _area);

            if (actualSlaveId == null)
            {
                _logger.Warning(
                    $"[ШАГ] Ожидание значения -> устройство не задано, адрес {registerAddress}, ожидалось {_expectedValue}.");

                return StepResult.False;
            }

            _logger.Info(
                $"[ШАГ] Ожидание значения -> устройство {actualSlaveId}, адрес {registerAddress}, ожидаемое {_expectedValue}, таймаут {_timeoutMs}мс, источник {(_liveRead ? "live Modbus" : "RegisterState")}.");

            var timer = System.Diagnostics.Stopwatch.StartNew();
            string lastError = string.Empty;

            while (timer.ElapsedMilliseconds < _timeoutMs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var read = await ModbusRegisterReadHelper.ReadAsync(
                    context,
                    _modbusService,
                    actualSlaveId.Value,
                    _address,
                    _liveRead,
                    cancellationToken, _area, Math.Max(0, _timeoutMs - (int)timer.ElapsedMilliseconds));

                if (read.Success && read.Value == _expectedValue)
                {
                    _logger.Info(
                        $"[OK] Устройство {actualSlaveId}, адрес {registerAddress}: получено значение {read.Value}.");

                    return StepResult.True;
                }

                lastError = read.Success
                    ? $"последнее значение {read.Value}"
                    : read.Error;

                var remaining = _timeoutMs - (int)timer.ElapsedMilliseconds;
                if (remaining > 0) await Task.Delay(Math.Min(PollIntervalMs, remaining), cancellationToken);
            }

            _logger.Warning(
                $"[ОШИБКА] Таймаут {_timeoutMs}мс истёк. Устройство {actualSlaveId}, адрес {registerAddress}, ожидалось {_expectedValue}; {lastError}.");

            return StepResult.False;
        }
    }
}
