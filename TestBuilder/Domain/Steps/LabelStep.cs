using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public class LabelStep : ITestStep
    {
        private readonly string _text;
        private readonly ILogger _logger;

        public LabelStep(string text, ILogger logger)
        {
            _text = text;
            _logger = logger;
        }

        public Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            _logger.Info($"=== {_text} ===");
            return Task.FromResult(StepResult.Next);
        }
    }
}