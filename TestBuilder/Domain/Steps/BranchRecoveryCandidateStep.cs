using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps;

internal sealed class BranchRecoveryCandidateStep(ITestStep inner, Guid key, string name, ILogger logger) : ITestStep
{
    internal ITestStep Inner => inner;

    public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken token)
    {
        if (context.RecoveryScope == null || context.StopOnPreparationError)
            return await inner.ExecuteAsync(context, token);
        var before = ExecutionFailureState.Capture(context);
        var reportStart = context.ReportEntries.Count;
        StepResult result;
        ExecutionFailure? cause = null;
        try { result = await inner.ExecuteAsync(context, token); }
        catch (Exception error) when (!token.IsCancellationRequested && !ExecutionCleanupException.Contains(error))
        {
            var code = TestErrorCodes.Resolve(error, TestErrorCodes.ForStep(inner));
            logger.Warning($"[ОШИБКА] {name}: {error.Message}", code);
            cause = context.Failure ?? new ExecutionFailure(name, error.Message, context.CurrentSlaveId) { Code = code };
            result = StepResult.False;
        }
        token.ThrowIfCancellationRequested();
        if (result != StepResult.False) return result;
        var failed = context.ReportEntries.Skip(reportStart).FirstOrDefault(e => !e.IsSuccess);
        cause ??= context.Failure ?? new ExecutionFailure(name, failed?.Value ?? "Проверка не пройдена.", context.CurrentSlaveId)
        { Code = failed?.ErrorCode ?? TestErrorCodes.ForStep(inner) };
        if (failed == null) context.AddReportEntry(name, false, cause.Reason, cause.Code);
        context.RecoveryScope.Add(key, cause, context.ReportEntries.Skip(reportStart), logger);
        before.Restore(context);
        logger.Warning($"[ОБРАБОТКА ОШИБКИ] «{name}»: переход по False. Ошибка сохранена до успешного подтверждения.", cause.Code);
        return result;
    }
}
