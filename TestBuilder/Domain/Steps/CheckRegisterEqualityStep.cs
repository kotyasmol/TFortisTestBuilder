using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public class CheckRegisterEqualityStep : ITestStep
    {
        private readonly byte _slaveId;
        private readonly int _address;
        private readonly int _expectedValue;
        private readonly bool _useCurrentSlaveId;
        private readonly ILogger _logger;

        public CheckRegisterEqualityStep(
            byte slaveId,
            int address,
            int expectedValue,
            ILogger logger,
            bool useCurrentSlaveId = false)
        {
            _slaveId = slaveId;
            _address = address;
            _expectedValue = expectedValue;
            _logger = logger;
            _useCurrentSlaveId = useCurrentSlaveId;
        }

        public Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            var actualSlaveId = _useCurrentSlaveId ? context.CurrentSlaveId : _slaveId;

            if (actualSlaveId == null)
            {
                _logger.Warning(
                    $"[ШАГ] Проверка равенства → устройство не задано, адрес {_address}, ожидалось {_expectedValue}.");

                return Task.FromResult(StepResult.False);
            }

            if (!context.RegisterState.TryGet(actualSlaveId.Value, _address, out var value))
            {
                _logger.Warning(
                    $"[ОШИБКА] Регистр не найден. Устройство {actualSlaveId}, адрес {_address}.");

                return Task.FromResult(StepResult.False);
            }

            var equal = value == _expectedValue;

            _logger.Info(
                $"[ШАГ] Проверка равенства → устройство {actualSlaveId}, адрес {_address}, значение {value}, ожидалось {_expectedValue}.");

            if (!equal)
            {
                _logger.Warning(
                    $"[ОШИБКА] Значение {value} != {_expectedValue}. Устройство {actualSlaveId}, адрес {_address}.");
            }
            else
            {
                _logger.Info($"[OK] Значение {value} == {_expectedValue}.");
            }

            return Task.FromResult(equal ? StepResult.True : StepResult.False);
        }
    }
}
