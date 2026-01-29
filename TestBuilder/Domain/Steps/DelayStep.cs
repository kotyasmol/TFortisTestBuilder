using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;

namespace TestBuilder.Domain.Steps
{
    public class DelayStep : ITestStep
    {
        public int Milliseconds { get; }

        public DelayStep(int milliseconds)
        {
            Milliseconds = milliseconds;
        }

        public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            await Task.Delay(Milliseconds, cancellationToken);
            return StepResult.Next;
        }
    }
}
