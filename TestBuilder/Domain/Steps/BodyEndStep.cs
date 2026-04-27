using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    /// <summary>
    /// Завершает выполнение тела составной ноды.
    /// Например, завершает текущую итерацию For Slaves.
    /// </summary>
    public sealed class BodyEndStep : ITestStep
    {
        private readonly ILogger _logger;

        public BodyEndStep(ILogger logger)
        {
            _logger = logger;
        }

        public Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            _logger.Info($"ТЕЛО ЦИКЛА: итерация завершена. Текущий slave={context.CurrentSlaveId?.ToString() ?? "не задан"}.");
            return Task.FromResult(StepResult.Stop);
        }
    }
}
