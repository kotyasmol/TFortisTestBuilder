using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public sealed class EndStep : ITestStep
    {
        private readonly ILogger _logger;

        public EndStep(ILogger logger)
        {
            _logger = logger;
        }

        public Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            _logger.Info("Тест завершён");
            return Task.FromResult(StepResult.True);
        }
    }
}