using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        public async Task ExecuteAsync(
            TestNode startNode,
            TestContext context,
            CancellationToken cancellationToken)
        {
            TestNode? current = startNode;

            while (current != null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await current.Step
                    .ExecuteAsync(context, cancellationToken);

                current = result switch
                {
                    StepResult.Next => current.Next,
                    StepResult.True => current.OnTrue,
                    StepResult.False => current.OnFalse,
                    _ => throw new InvalidOperationException()
                };
            }
        }
    }
}
