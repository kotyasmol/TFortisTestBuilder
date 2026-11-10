using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Execution
{
    /// <summary>Executes sequential paths and structured parallel regions with an explicit join barrier.</summary>
    public sealed class TestExecutor
    {
        public Task<ExecutionStatus> ExecuteAsync(TestNode startNode, TestContext context,
            CancellationToken cancellationToken) => ExecuteUntilAsync(startNode, null, context, cancellationToken);

        private async Task<ExecutionStatus> ExecuteUntilAsync(TestNode startNode, TestNode? stopBefore,
            TestContext context, CancellationToken cancellationToken)
        {
            TestNode? current = startNode;
            while (current != null && !ReferenceEquals(current, stopBefore))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await context.WaitWhilePausedAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                var (result, failure) = await ExecuteNodeAsync(current, context, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                if (context.HasExecutionException && !context.IsCleanup)
                {
                    context.Failure = failure;
                    return ExecutionStatus.Failed;
                }
                if ((context.StopOnPreparationError && result == StepResult.False) ||
                    (context.IsParallelBranch && context.HasCriticalError))
                {
                    context.Failure = failure;
                    return ExecutionStatus.Failed;
                }
                if (result == StepResult.Stop)
                {
                    if (stopBefore != null)
                        throw new InvalidOperationException("Параллельная ветвь завершилась до точки объединения.")
                            .WithCode(TestErrorCode.ParallelGraph);
                    return ExecutionStatus.Completed;
                }

                if (current.ParallelTransitions.TryGetValue(result, out var fork))
                {
                    var status = await ExecuteParallelAsync(fork, context, cancellationToken);
                    if (status != ExecutionStatus.Completed) return status;
                    current = fork.JoinNode;
                }
                else
                {
                    var next = result switch
                    {
                        StepResult.Next => current.Next,
                        StepResult.True => current.OnTrue,
                        StepResult.False => current.OnFalse,
                        _ => throw new InvalidOperationException($"Неизвестный результат шага: {result}.")
                    };
                    if (next == null && (result == StepResult.False || current.RequiresSuccessTransition))
                    {
                        context.Failure = failure ?? new ExecutionFailure(current.DisplayName,
                            "Нет перехода после успешного выполнения. Соедините выход ноды со следующим шагом или нодой «Конец».",
                            context.CurrentSlaveId) { Code = TestErrorCode.MissingTransition };
                        return ExecutionStatus.Failed;
                    }
                    current = next;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (stopBefore != null && current == null)
                throw new InvalidOperationException("Параллельная ветвь не достигла точки объединения.")
                    .WithCode(TestErrorCode.ParallelGraph);
            return ExecutionStatus.Completed;
        }

        private static async Task<(StepResult Result, ExecutionFailure? Failure)> ExecuteNodeAsync(TestNode node, TestContext context,
            CancellationToken cancellationToken)
        {
            if (node.RunOnlyOnFirstRun && context.SkipFirstRunNodes)
            {
                if (context.ExecutionObserver != null)
                    await context.ExecutionObserver.NodeSkippedAsync(node, context, cancellationToken);
                return (node.SkippedResult, null);
            }
            using var diagnostics = new StepLogScope(TestErrorCodes.ForStep(node.Step));
            context.Failure = null;
            var stepName = string.IsNullOrWhiteSpace(node.DisplayName)
                ? node.Step?.GetType().Name ?? "Неизвестный шаг" : node.DisplayName;
            var result = StepResult.Next;
            Exception? failure = null;
            var failureNotificationAttempted = false;
            try
            {
                if (context.ExecutionObserver != null)
                    await context.ExecutionObserver.NodeStartedAsync(node, context, cancellationToken);
                var reportStart = context.ReportEntries.Count;
                if (node.Step != null)
                    result = await node.Step.ExecuteAsync(context, cancellationToken);
                // Routing may continue after an optional failure, while the UI must still
                // show the actual failed check (especially an unsuccessful power-off).
                var failedCheck = context.ReportEntries.Skip(reportStart).FirstOrDefault(entry => !entry.IsSuccess);
                var observedResult = failedCheck != null || (context.StopOnPreparationError && context.HasCriticalError)
                    ? StepResult.False : result;
                if (context.StopOnPreparationError && observedResult == StepResult.False)
                {
                    result = StepResult.False;
                    if (failedCheck != null)
                        context.Failure ??= new ExecutionFailure(stepName, failedCheck.Value, context.CurrentSlaveId)
                        {
                            Code = failedCheck.ErrorCode ?? diagnostics.Code
                        };
                }
                if (context.ExecutionObserver != null)
                    await context.ExecutionObserver.NodeCompletedAsync(node, observedResult, context, CancellationToken.None);
                if (observedResult == StepResult.False)
                {
                    failureNotificationAttempted = true;
                    await NotifyNodeFailedAsync(node, context);
                }
            }
            catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested)
            {
                failure = error;
            }
            catch (Exception error)
            {
                failure = error;
                if (!failureNotificationAttempted)
                {
                    try { await NotifyNodeFailedAsync(node, context); }
                    catch (Exception observerError) { failure = new AggregateException(error, observerError); }
                }
            }
            finally
            {
                // Terminal callbacks cannot be skipped by cancellation and cannot hide the step's actual cause.
                try
                {
                    if (context.ExecutionObserver != null)
                        await context.ExecutionObserver.NodeFinishedAsync(node, context, CancellationToken.None);
                }
                catch (Exception observerError)
                {
                    failure = failure == null ? observerError : new AggregateException(failure, observerError);
                }
            }
            if (failure != null)
            {
                if (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                    context.Failure ??= new ExecutionFailure(stepName, failure.Message, context.CurrentSlaveId)
                    {
                        Code = TestErrorCodes.Resolve(failure, diagnostics.Code)
                    };
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            var detail = result == StepResult.False || (context.IsParallelBranch && context.HasCriticalError)
                ? context.Failure ?? new ExecutionFailure(stepName, diagnostics.Reason, context.CurrentSlaveId)
                {
                    Code = diagnostics.Code
                }
                : null;
            if (context.HasCriticalError && detail != null)
                context.CriticalFailure ??= detail;
            // A handled False is an alternative path, not the cause of a failed run.
            context.Failure = null;
            return (result, detail);
        }

        private async Task<ExecutionStatus> ExecuteParallelAsync(ParallelFork fork, TestContext context,
            CancellationToken cancellationToken)
        {
            using var branchesCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // Snapshot every branch before starting any work. Unsupported variables cannot cause a half-started fork.
            TestContext[] contexts;
            try { contexts = fork.Branches.Select(_ => context.CreateParallelBranch(branchesCts.Token)).ToArray(); }
            catch
            {
                context.HasCriticalError = true;
                throw;
            }
            var tasks = fork.Branches.Select((branch, index) => RunBranchAsync(branch, fork.JoinNode,
                contexts[index], branchesCts)).ToArray();
            var outcomes = await Task.WhenAll(tasks); // Wrappers capture failures and always drain every branch.
            Exception? mergeError = null;
            try
            {
                context.MergeParallelBranches(contexts, outcomes.Select(result => result.Status).ToArray(),
                    outcomes.Select(result => result.Error).ToArray());
            }
            catch (Exception error)
            {
                context.HasCriticalError = true;
                mergeError = error;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var errors = outcomes.Where(result => result.Error != null && !result.CancelledByGroup)
                .Select(result => result.Error!).ToList();
            if (mergeError != null) errors.Add(mergeError);
            if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
            if (errors.Count > 1) throw new AggregateException("Ошибки параллельных ветвей.", errors);
            if (outcomes.Any(result => result.Status != ExecutionStatus.Completed))
                return ExecutionStatus.Failed;
            return ExecutionStatus.Completed;
        }

        private async Task<BranchOutcome> RunBranchAsync(TestNode start, TestNode join,
            TestContext context, CancellationTokenSource groupCts)
        {
            var status = ExecutionStatus.Failed;
            Exception? failure = null;
            var cancelledByGroup = false;
            try
            {
                status = await ExecuteUntilAsync(start, join, context, groupCts.Token);
                if (status != ExecutionStatus.Completed) groupCts.Cancel();
            }
            catch (OperationCanceledException error) when (groupCts.IsCancellationRequested)
            {
                status = ExecutionStatus.Cancelled;
                failure = error;
                cancelledByGroup = true;
            }
            catch (Exception error)
            {
                failure = error;
                context.HasCriticalError = true;
                // Cancellation callbacks must not replace the actual step exception.
                try { groupCts.Cancel(); }
                catch (Exception cancellationError) { failure = new AggregateException(error, cancellationError); }
            }
            finally
            {
                try
                {
                    if (context.ExecutionObserver != null)
                        await context.ExecutionObserver.ParallelBranchFinishedAsync(context, CancellationToken.None);
                }
                catch (Exception observerError)
                {
                    failure = failure == null ? observerError : new AggregateException(failure, observerError);
                    status = ExecutionStatus.Failed;
                    cancelledByGroup = false;
                    try { groupCts.Cancel(); }
                    catch (Exception cancellationError) { failure = new AggregateException(failure, cancellationError); }
                }
            }
            return new BranchOutcome(status, failure, cancelledByGroup);
        }

        private static Task NotifyNodeFailedAsync(TestNode node, TestContext context) =>
            context.ExecutionObserver?.NodeFailedAsync(node, context, CancellationToken.None) ?? Task.CompletedTask;

        private sealed record BranchOutcome(ExecutionStatus Status, Exception? Error, bool CancelledByGroup);
    }
}
