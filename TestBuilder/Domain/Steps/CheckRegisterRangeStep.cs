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
        private readonly ILogger _logger;

        public CheckRegisterRangeStep(
            byte slaveId,
            int address,
            int min,
            int max,
            ILogger logger,
            bool useCurrentSlaveId = false,
            IModbusService? modbusService = null,
            bool liveRead = false)
        {
            _slaveId = slaveId;
            _address = address;
            _min = min;
            _max = max;
            _logger = logger;
            _useCurrentSlaveId = useCurrentSlaveId;
            _modbusService = modbusService;
            _liveRead = liveRead;
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

            var read = await ModbusRegisterReadHelper.ReadAsync(
                context,
                _modbusService,
                actualSlaveId.Value,
                _address,
                _liveRead,
                cancellationToken);

            if (!read.Success)
            {
                _logger.Warning(
                    $"[ОШИБКА] Регистр не прочитан. Устройство {actualSlaveId}, адрес {_address}: {read.Error}");

                return StepResult.False;
            }

            var value = read.Value;
            var inRange = value >= _min && value <= _max;

            _logger.Info(
                $"[ШАГ] Проверка диапазона → устройство {actualSlaveId}, адрес {_address}, значение {value}, диапазон [{_min}..{_max}], источник {(_liveRead ? "live Modbus" : "RegisterState")}.");

            if (!inRange)
            {
                _logger.Warning(
                    $"[ОШИБКА] Значение {value} вне диапазона [{_min}..{_max}]. Устройство {actualSlaveId}, адрес {_address}.");
            }

            if (inRange) _logger.Info($"[OK] Значение {value} в диапазоне [{_min}..{_max}].");
            return inRange ? StepResult.True : StepResult.False;
        }

        private byte? ResolveSlaveId(TestContext context)
        {
            return _useCurrentSlaveId ? context.CurrentSlaveId : _slaveId;
        }
    }
}
