using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;

namespace TestBuilder.Domain.Steps
{
    public class PassThroughStep : ITestStep
    {
        public Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(StepResult.Next);
        }
    }
}