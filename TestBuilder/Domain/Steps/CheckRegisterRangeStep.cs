using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

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
        private readonly ILogger _logger;

        public CheckRegisterRangeStep(
            byte slaveId,
            int address,
            int min,
            int max,
            ILogger logger,
            bool useCurrentSlaveId = false)
        {
            _slaveId = slaveId;
            _address = address;
            _min = min;
            _max = max;
            _logger = logger;
            _useCurrentSlaveId = useCurrentSlaveId;
        }

        public Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            var actualSlaveId = ResolveSlaveId(context);

            if (actualSlaveId == null)
            {
                _logger.Warning(
                    $"[ШАГ] Проверка диапазона → устройство не задано, адрес {_address}, диапазон [{_min}..{_max}].");

                return Task.FromResult(StepResult.False);
            }

            if (!context.RegisterState.TryGet(actualSlaveId.Value, _address, out var value))
            {
                _logger.Warning(
                    $"[ОШИБКА] Регистр не найден. Устройство {actualSlaveId}, адрес {_address}.");

                return Task.FromResult(StepResult.False);
            }

            var inRange = value >= _min && value <= _max;

            _logger.Info(
                $"[ШАГ] Проверка диапазона → устройство {actualSlaveId}, адрес {_address}, значение {value}, диапазон [{_min}..{_max}].");

            if (!inRange)
            {
                _logger.Warning(
                    $"[ОШИБКА] Значение {value} вне диапазона [{_min}..{_max}]. Устройство {actualSlaveId}, адрес {_address}.");
            }

            if (inRange) _logger.Info($"[OK] Значение {value} в диапазоне [{_min}..{_max}].");
            return Task.FromResult(inRange ? StepResult.True : StepResult.False);
        }

        private byte? ResolveSlaveId(TestContext context)
        {
            return _useCurrentSlaveId ? context.CurrentSlaveId : _slaveId;
        }
    }
}