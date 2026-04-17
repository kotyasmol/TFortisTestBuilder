using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public class CheckRegisterRangeStep : ITestStep
    {
        private readonly byte _slaveId;
        private readonly int _address;
        private readonly int _min;
        private readonly int _max;
        private readonly ILogger _logger;

        public CheckRegisterRangeStep(byte slaveId, int address, int min, int max, ILogger logger)
        {
            _slaveId = slaveId;
            _address = address;
            _min = min;
            _max = max;
            _logger = logger;
        }

        public Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            if (!context.RegisterState.TryGet(_slaveId, _address, out var value))
            {
                _logger.Warning($"CheckRange: регистр {_address} слейва {_slaveId} не найден");
                return Task.FromResult(StepResult.False);
            }

            bool inRange = value >= _min && value <= _max;

            _logger.Info($"CheckRange: слейв={_slaveId}, адрес={_address}, значение={value}, диапазон=[{_min}..{_max}] → {(inRange ? "True" : "False")}");
            if(!inRange)
                _logger.Warning($"CheckRange: значение {value} вне диапазона [{_min}..{_max}]");

            return Task.FromResult(inRange ? StepResult.True : StepResult.False);
        }
    }
}