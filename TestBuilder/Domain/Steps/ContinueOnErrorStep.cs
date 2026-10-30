using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps;

// The unchecked policy also applies to exceptions from transports, not just False results.
internal sealed class ContinueOnErrorStep(ITestStep inner, string name, ILogger logger) : ITestStep
{
    public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken token)
    {
        var state = ExecutionFailureState.Capture(context);
        var reportStart = context.ReportEntries.Count;
        using var diagnostics = new StepLogScope();
        StepResult result;
        try { result = await inner.ExecuteAsync(context, token); }
        catch (Exception error) when (!token.IsCancellationRequested && !ExecutionCleanupException.Contains(error))
        {
            logger.Warning($"[ОШИБКА] {name}: {error.Message}. Выполнение продолжится по обычному выходу.");
            context.AddReportEntry(name, false, error.Message);
            result = StepResult.False;
        }
        token.ThrowIfCancellationRequested();
        if (result == StepResult.False && !context.ReportEntries.Skip(reportStart).Any(entry => !entry.IsSuccess))
            context.AddReportEntry(name, false, context.Failure?.Reason ?? diagnostics.Reason);
        state.Restore(context);
        return result == StepResult.False ? StepResult.True : result;
    }
}
