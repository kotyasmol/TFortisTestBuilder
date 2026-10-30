namespace TestBuilder.Domain.Execution;

// Restore only failure control state; observations and failed report entries must survive.
internal readonly record struct ExecutionFailureState(bool Critical, bool Exception,
    ExecutionFailure? Failure, ExecutionFailure? CriticalFailure)
{
    public static ExecutionFailureState Capture(TestContext context) =>
        new(context.HasCriticalError, context.HasExecutionException, context.Failure, context.CriticalFailure);

    public void Restore(TestContext context)
    {
        context.HasCriticalError = Critical;
        context.HasExecutionException = Exception;
        context.Failure = Failure;
        context.CriticalFailure = CriticalFailure;
    }
}

// Continuing is unsafe while a previous operation may still affect the equipment.
internal class ExecutionCleanupException(string message, System.Exception? inner = null)
    : System.InvalidOperationException(message, inner)
{
    public static bool Contains(System.Exception error) => error is ExecutionCleanupException ||
        error is System.AggregateException aggregate && System.Linq.Enumerable.Any(aggregate.InnerExceptions, Contains) ||
        error.InnerException != null && Contains(error.InnerException);
}
