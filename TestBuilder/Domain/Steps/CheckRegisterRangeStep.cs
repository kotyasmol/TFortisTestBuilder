using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;

namespace TestBuilder.Domain.Steps
{
    /// <summary>
    /// Проверяет, что значение регистра находится в диапазоне [min, max].
    /// Не обращается к Modbus — использует RegisterState.
    /// </summary>
    public class CheckRegisterRangeStep : ITestStep
    {
        private readonly byte _slaveId;
        private readonly int _address;
        private readonly int _min;
        private readonly int _max;

        public CheckRegisterRangeStep(byte slaveId, int address, int min, int max)
        {
            _slaveId = slaveId;
            _address = address;
            _min = min;
            _max = max;
        }

        public Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            // Берём значение из RegisterState (НЕ из Modbus)
            if (!context.RegisterState.TryGet(_slaveId, _address, out var value))
            {
                // мониторинг ещё не успел прочитать
                return Task.FromResult(StepResult.False);
            }

            bool inRange = value >= _min && value <= _max;

            return Task.FromResult(inRange ? StepResult.True : StepResult.False);
        }
    }
}