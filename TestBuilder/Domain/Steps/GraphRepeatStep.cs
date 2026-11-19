using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps;

// Shared attempt scope for the dedicated retry block and the legacy loop mode.
internal sealed class GraphRepeatStep(CompiledGraph body, ILogger logger, int count,
    int delayMs, bool untilSuccess, bool stopOnError, string? indexVariable, string label) : ITestStep
{
    internal static void Validate(int count, int delayMs)
    {
        if (count is < 1 or > 100000 || delayMs is < 0 or > 3600000)
            throw new InvalidOperationException("Повтор проверки: всего попыток от 1 до 100000, пауза от 0 до 3600000 мс.");
    }

    public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken token)
    {
        try
        {
            Validate(count, delayMs);
            if (indexVariable != null && (string.IsNullOrWhiteSpace(indexVariable) || indexVariable == "slaveId"))
                throw new InvalidOperationException("Укажите отдельное имя счётчика повторов; slaveId занят адресом устройства.");
        }
        catch (InvalidOperationException error)
        {
            logger.Warning("[ОШИБКА] " + error.Message);
            context.AddReportEntry(label, false, error.Message);
            return stopOnError ? StepResult.False : StepResult.Next;
        }
        var previousSlave = context.CurrentSlaveId;
        object? previousIndex = null;
        var hadIndex = indexVariable != null && context.Variables.TryGetValue(indexVariable, out previousIndex);
        var initialCritical = context.HasCriticalError;
        var failureState = ExecutionFailureState.Capture(context);
        var reportStart = context.ReportEntries.Count;
        var anyFailure = false;
        var executor = new TestExecutor();
        try
        {
            for (var i = 1; i <= count; i++)
            {
                token.ThrowIfCancellationRequested();
                await context.WaitWhilePausedAsync(token);
                if (i > 1 && delayMs > 0) await Task.Delay(delayMs, token);
                token.ThrowIfCancellationRequested();
                context.CurrentSlaveId = previousSlave;
                if (indexVariable != null) context.SetVariable(indexVariable, i);
                logger.Info($"[ШАГ] {label}: попытка {i}/{count}.");
                var iterationReportStart = context.ReportEntries.Count;
                var result = await ExecuteIterationAsync(executor, body, context, token,
                    $"{label}: попытка {i}", !stopOnError || untilSuccess, logger);
                token.ThrowIfCancellationRequested();
                var success = result == ExecutionStatus.Completed && context.HasCriticalError == initialCritical &&
                    (!untilSuccess || !context.ReportEntries.Skip(iterationReportStart).Any(entry => !entry.IsSuccess));
                if (success && untilSuccess)
                {
                    logger.Info($"[OK] {label}: успешная попытка {i}/{count}.");
                    return StepResult.Next;
                }
                if (success) continue;
                anyFailure = true;
                if (!untilSuccess && stopOnError) return StepResult.False;
                if (untilSuccess && i < count)
                {
                    logger.Warning($"[ПОВТОР] {label}: попытка {i}/{count} неуспешна. Следующая попытка через {delayMs} мс.");
                    // Discard only this block's intermediate result. Other failures survive.
                    failureState.Restore(context);
                    context.ReportEntries.RemoveRange(reportStart, context.ReportEntries.Count - reportStart);
                }
                else if (!stopOnError) failureState.Restore(context);
            }
            return anyFailure && stopOnError ? StepResult.False : StepResult.Next;
        }
        finally
        {
            context.CurrentSlaveId = previousSlave;
            if (indexVariable != null)
            {
                if (hadIndex) context.Variables[indexVariable] = previousIndex!;
                else context.Variables.Remove(indexVariable);
            }
        }
    }

    internal static async Task<ExecutionStatus> ExecuteIterationAsync(TestExecutor executor, CompiledGraph body,
        TestContext context, CancellationToken token, string name, bool tolerateException, ILogger logger)
    {
        var reportStart = context.ReportEntries.Count;
        var failureState = ExecutionFailureState.Capture(context);
        ExecutionStatus result;
        try { result = await executor.ExecuteAsync(body.StartNode, context, token); }
        catch (Exception error) when (tolerateException && !token.IsCancellationRequested && !ExecutionCleanupException.Contains(error))
        {
            var code = context.Failure?.Code ?? TestErrorCodes.Resolve(error, TestErrorCode.Loop);
            logger.Warning($"[ОШИБКА] {name}: {error.Message}.", code);
            context.AddReportEntry(name, false, error.Message, code);
            result = ExecutionStatus.Failed;
        }
        token.ThrowIfCancellationRequested();
        if (result == ExecutionStatus.Completed && failureState.HasNewFailure(context)) result = ExecutionStatus.Failed;
        if (result != ExecutionStatus.Completed && !context.ReportEntries.Skip(reportStart).Any(entry => !entry.IsSuccess))
            context.AddReportEntry(name, false, context.Failure?.Summary ?? "Проверка не пройдена.", context.Failure?.Code ?? TestErrorCode.Loop);
        return result;
    }
}
