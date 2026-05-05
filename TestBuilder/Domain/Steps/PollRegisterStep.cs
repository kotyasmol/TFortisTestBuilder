using System;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public class PollRegisterStep : ITestStep
    {
        private readonly byte _slaveId;
        private readonly int _address;
        private readonly int _min;
        private readonly int _max;
        private readonly int _durationMs;
        private readonly bool _useCurrentSlaveId;
        private readonly ILogger _logger;

        private const int PollIntervalMs = 200;

        public PollRegisterStep(
            byte slaveId,
            int address,
            int min,
            int max,
            int durationMs,
            ILogger logger,
            bool useCurrentSlaveId = false)
        {
            _slaveId = slaveId;
            _address = address;
            _min = min;
            _max = max;
            _durationMs = durationMs;
            _logger = logger;
            _useCurrentSlaveId = useCurrentSlaveId;
        }

        public async Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            var actualSlaveId = _useCurrentSlaveId ? context.CurrentSlaveId : _slaveId;

            if (actualSlaveId == null)
            {
                _logger.Warning(
                    $"[ШАГ] Опрос регистра → устройство не задано, адрес {_address}, диапазон [{_min}..{_max}].");

                return StepResult.False;
            }

            _logger.Info(
                $"[ШАГ] Опрос регистра → устройство {actualSlaveId}, адрес {_address}, диапазон [{_min}..{_max}], длительность {_durationMs}мс.");

            int successes = 0;
            int failures = 0;

            var deadline = DateTime.UtcNow.AddMilliseconds(_durationMs);

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (context.RegisterState.TryGet(actualSlaveId.Value, _address, out var value))
                {
                    if (value >= _min && value <= _max)
                        successes++;
                    else
                        failures++;
                }

                await Task.Delay(PollIntervalMs, cancellationToken);
            }

            _logger.Info(
                $"[ШАГ] Опрос завершён → устройство {actualSlaveId}, адрес {_address}: успехов {successes}, провалов {failures}.");

            if (successes > failures)
            {
                _logger.Info($"[OK] Успехов ({successes}) > провалов ({failures}).");
                return StepResult.True;
            }

            _logger.Warning($"[ОШИБКА] Провалов ({failures}) >= успехов ({successes}).");
            return StepResult.False;
        }
    }
}
