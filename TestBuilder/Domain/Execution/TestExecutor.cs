using System;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Domain.Execution
{
    /// <summary>
    /// Исполнитель графа тестирования.
    /// Отвечает за последовательное выполнение узлов и обработку переходов.
    /// </summary>
    public sealed class TestExecutor
    {
        /// <summary>
        /// Запускает выполнение теста, начиная с указанного узла.
        /// </summary>
        public async Task<ExecutionStatus> ExecuteAsync(
            TestNode startNode,
            TestContext context,
            CancellationToken cancellationToken)
        {
            TestNode? current = startNode;

            while (current != null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await NotifyNodeStartedAsync(current, context, cancellationToken);

                StepResult result;

                try
                {
                    if (current.Step == null)
                    {
                        result = StepResult.Next;
                    }
                    else
                    {
                        result = await current.Step.ExecuteAsync(
                            context,
                            cancellationToken);
                    }

                    if (result == StepResult.Stop)
                        return ExecutionStatus.Completed;
                }
                finally
                {
                    await NotifyNodeFinishedAsync(current, context, cancellationToken);
                }

                Console.WriteLine(
                    $"Step: {current.Step?.GetType().Name}, Result: {result}, OnTrue: {current.OnTrue?.Step?.GetType().Name}, OnFalse: {current.OnFalse?.Step?.GetType().Name}, Next: {current.Next?.Step?.GetType().Name}");

                current = result switch
                {
                    StepResult.Next => current.Next,
                    StepResult.True => current.OnTrue,
                    StepResult.False => current.OnFalse,
                    _ => throw new InvalidOperationException()
                };

                if (current == null && result == StepResult.False)
                    return ExecutionStatus.Failed;
            }

            return ExecutionStatus.Completed;
        }

        private static async Task NotifyNodeStartedAsync(
            TestNode node,
            TestContext context,
            CancellationToken cancellationToken)
        {
            if (context.ExecutionObserver != null)
            {
                await context.ExecutionObserver.NodeStartedAsync(
                    node,
                    context,
                    cancellationToken);
            }
        }

        private static async Task NotifyNodeFinishedAsync(
            TestNode node,
            TestContext context,
            CancellationToken cancellationToken)
        {
            if (context.ExecutionObserver != null)
            {
                await context.ExecutionObserver.NodeFinishedAsync(
                    node,
                    context,
                    cancellationToken);
            }
        }
    }
}