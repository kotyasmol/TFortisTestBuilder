using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public class DelayStep : ITestStep
    {
        public int Milliseconds { get; }
        private readonly ILogger _logger;

        public DelayStep(int milliseconds, ILogger logger)
        {
            Milliseconds = milliseconds;
            _logger = logger;
        }

        public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            _logger.Info($"Delay: ожидание {Milliseconds} мс...");
            await Task.Delay(Milliseconds, cancellationToken);
            _logger.Info($"Delay: завершено");
            return StepResult.True;
        }
    }
}