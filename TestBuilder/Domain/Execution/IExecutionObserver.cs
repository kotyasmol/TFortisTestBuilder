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
        Task NodeStartedAsync(
            TestNode node,
            TestContext context,
            CancellationToken cancellationToken);

        Task NodeFinishedAsync(
            TestNode node,
            TestContext context,
            CancellationToken cancellationToken);
    }
}