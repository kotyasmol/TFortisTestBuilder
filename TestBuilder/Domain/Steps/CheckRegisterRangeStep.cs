using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Steps
{
    /// <summary>
    /// Проверяет, что последнее известное значение регистра находится в заданном диапазоне.
    /// Может использовать фиксированный slaveId или текущий slaveId из цикла.
    /// </summary>
    public class CheckRegisterRangeStep : ITestStep
    {
        private readonly byte _slaveId;
        private readonly int _address;
        private readonly int _min;
        private readonly int _max;
        private readonly bool _useCurrentSlaveId;
        private readonly IModbusService? _modbusService;
        private readonly bool _liveRead;
        private readonly int _readAttempts;
        private readonly int _readIntervalMs;
        private readonly ILogger _logger;

        public CheckRegisterRangeStep(
            byte slaveId,
            int address,
            int min,
            int max,
            ILogger logger,
            bool useCurrentSlaveId = false,
            IModbusService? modbusService = null,
            bool liveRead = false,
            int readAttempts = 1,
            int readIntervalMs = 0)
        {
            _slaveId = slaveId;
            _address = address;
            _min = min;
            _max = max;
            _logger = logger;
            _useCurrentSlaveId = useCurrentSlaveId;
            _modbusService = modbusService;
            _liveRead = liveRead;
            _readAttempts = System.Math.Max(1, readAttempts);
            _readIntervalMs = System.Math.Max(0, readIntervalMs);
        }

        public async Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            var actualSlaveId = ResolveSlaveId(context);

            if (actualSlaveId == null)
            {
                _logger.Warning(
                    $"[ШАГ] Проверка диапазона → устройство не задано, адрес {_address}, диапазон [{_min}..{_max}].");

                return StepResult.False;
            }

            for (var attempt = 1; attempt <= _readAttempts; attempt++)
            {
                // The old stand waits before each of its three PoE reads.
                if (_liveRead && _readIntervalMs > 0)
                    await Task.Delay(_readIntervalMs, cancellationToken);

                var read = await ModbusRegisterReadHelper.ReadAsync(
                    context, _modbusService, actualSlaveId.Value, _address, _liveRead, cancellationToken);

                if (read.Success)
                {
                    var inRange = read.Value >= _min && read.Value <= _max;
                    _logger.Info(
                        $"[ШАГ] Проверка диапазона → устройство {actualSlaveId}, адрес {_address}, значение {read.Value}, диапазон [{_min}..{_max}], попытка {attempt}/{_readAttempts}, источник {(_liveRead ? "live Modbus" : "RegisterState")}.");
                    if (inRange)
                    {
                        _logger.Info($"[OK] Значение {read.Value} в диапазоне [{_min}..{_max}].");
                        return StepResult.True;
                    }
                    if (attempt == _readAttempts)
                        _logger.Warning($"[ОШИБКА] Значение {read.Value} вне диапазона [{_min}..{_max}]. Устройство {actualSlaveId}, адрес {_address}.");
                }
                else if (attempt == _readAttempts)
                {
                    _logger.Warning($"[ОШИБКА] Регистр не прочитан. Устройство {actualSlaveId}, адрес {_address}: {read.Error}");
                }
            }

            return StepResult.False;
        }

        private byte? ResolveSlaveId(TestContext context)
        {
            return _useCurrentSlaveId ? context.CurrentSlaveId : _slaveId;
        }
    }
}
