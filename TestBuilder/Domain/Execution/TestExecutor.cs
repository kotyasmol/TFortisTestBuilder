using System;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Domain.Execution
{
    /// <summary>
    /// Исполнитель графа тестирования.
    /// Последовательно выполняет узлы, обрабатывает линейные и условные переходы.
    /// </summary>
    public sealed class TestExecutor
    {
        public async Task<ExecutionStatus> ExecuteAsync(
            TestNode startNode,
            TestContext context,
            CancellationToken cancellationToken)
        {
            try
            {
                TestNode? current = startNode;

                while (current != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (current.Step == null)
                    {
                        current = current.Next;
                        continue;
                    }

                    var result = await current.Step.ExecuteAsync(context, cancellationToken);

                    if (result == StepResult.Stop)
                        return ExecutionStatus.Completed;

                    var next = result switch
                    {
                        StepResult.Next => current.Next,
                        StepResult.True => current.OnTrue,
                        StepResult.False => current.OnFalse,
                        _ => throw new InvalidOperationException($"Неизвестный результат шага: {result}")
                    };

                    if (next == null && result == StepResult.False)
                        return ExecutionStatus.Failed;

                    current = next;
                }

                return ExecutionStatus.Completed;
            }
            catch (OperationCanceledException)
            {
                return ExecutionStatus.Cancelled;
            }
        }
    }
}
