using System;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;

namespace TestBuilder.Domain.Steps
{
    /// <summary>
    /// Шаг теста, который сравнивает значение регистра с заданным диапазоном.
    /// Возвращает True (StepResult.True), если значение в пределах,
    /// и False (StepResult.False), если нет.
    /// </summary>
    public class CompareStep : ITestStep
    {
        public string RegisterName { get; }
        public int Min { get; }
        public int Max { get; }

        public CompareStep(string registerName, int min, int max)
        {
            RegisterName = registerName;
            Min = min;
            Max = max;
        }

        public Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            // Читаем актуальное значение из RegisterState
            if (!context.RegisterState.TryGet(RegisterName, out var value))
            {
                // Если регистра нет, считаем шаг неудачным
                return Task.FromResult(StepResult.False);
            }

            // Проверяем, находится ли значение в заданном диапазоне
            return Task.FromResult(value >= Min && value <= Max
                ? StepResult.True
                : StepResult.False);
        }
    }
}
