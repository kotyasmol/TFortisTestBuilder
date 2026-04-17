using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public sealed class StartStep : ITestStep
    {
        private readonly ILogger _logger;

        public StartStep(ILogger logger)
        {
            _logger = logger;
        }

        public Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            _logger.Info("Тест начат");
            return Task.FromResult(StepResult.True);
        }
    }
}