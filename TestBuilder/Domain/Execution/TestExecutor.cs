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
                if (context.IsParallelBranch && context.HasCriticalError)
                {
                    context.Failure = failure;
                    return ExecutionStatus.Failed;
                }
                if (result == StepResult.Stop)
                {
                    if (stopBefore != null)
                        throw new InvalidOperationException("Параллельная ветвь завершилась до точки объединения.");
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
                    current = result switch
                    {
                        StepResult.Next => current.Next,
                        StepResult.True => current.OnTrue,
                        StepResult.False => current.OnFalse,
                        _ => throw new InvalidOperationException($"Неизвестный результат шага: {result}.")
                    };
                    if (current == null && result == StepResult.False)
                    {
                        context.Failure = failure;
                        return ExecutionStatus.Failed;
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (stopBefore != null && current == null)
                throw new InvalidOperationException("Параллельная ветвь не достигла точки объединения.");
            return ExecutionStatus.Completed;
        }

        private static async Task<(StepResult Result, ExecutionFailure? Failure)> ExecuteNodeAsync(TestNode node, TestContext context,
            CancellationToken cancellationToken)
        {
            using var diagnostics = new StepLogScope();
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
                if (node.Step != null)
                    result = await node.Step.ExecuteAsync(context, cancellationToken);
                if (context.ExecutionObserver != null)
                    await context.ExecutionObserver.NodeCompletedAsync(node, result, context, CancellationToken.None);
                if (result == StepResult.False)
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
                    context.Failure ??= new ExecutionFailure(stepName, failure.Message, context.CurrentSlaveId);
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            var detail = result == StepResult.False || (context.IsParallelBranch && context.HasCriticalError)
                ? context.Failure ?? new ExecutionFailure(stepName, diagnostics.Reason, context.CurrentSlaveId)
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
