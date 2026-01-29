using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;

namespace TestBuilder.Domain.Steps
{
    public sealed class EndStep : ITestStep
    {
        public Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            Console.WriteLine("End step");
            return Task.FromResult(StepResult.Next);
        }
    }
}
