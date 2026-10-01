using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Domain.Execution
{
    /// <summary>
    /// Получает уведомления о том, какая нода сейчас выполняется.
    /// Используется UI для визуальной подсветки активного шага.
    /// </summary>
    public interface IExecutionObserver
    {
        Task ParallelBranchFinishedAsync(TestContext context, CancellationToken cancellationToken) => Task.CompletedTask;

        Task PauseWaitingChangedAsync(TestContext context, bool isWaiting, CancellationToken cancellationToken) => Task.CompletedTask;

        // NodeFinishedAsync also runs on exceptions; this notification carries a real result.
        Task NodeCompletedAsync(TestNode node, StepResult result, TestContext context,
            CancellationToken cancellationToken) => Task.CompletedTask;

        Task NodeStartedAsync(
            TestNode node,
            TestContext context,
            CancellationToken cancellationToken);

        Task NodeFinishedAsync(
            TestNode node,
            TestContext context,
            CancellationToken cancellationToken);

        Task NodeFailedAsync(
            TestNode node,
            TestContext context,
            CancellationToken cancellationToken);
    }
}
