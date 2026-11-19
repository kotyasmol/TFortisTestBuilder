using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps;

internal sealed class RetryCheckStep(CompiledGraph body, ILogger logger, int maxAttempts, int delayMs) : ITestStep
{
    public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken token)
    {
        var before = ExecutionFailureState.Capture(context);
        var result = await new GraphRepeatStep(body, logger, maxAttempts, delayMs, true, true, null, "Повтор проверки")
            .ExecuteAsync(context, token);
        if (result == StepResult.False)
        {
            // An exhausted, handled attempt must reach this block's Error output.
            // Its final failed report entries still make the product result negative.
            var cause = context.Failure ?? context.CriticalFailure;
            before.Restore(context);
            context.Failure = cause ?? context.Failure;
        }
        return result;
    }
}
