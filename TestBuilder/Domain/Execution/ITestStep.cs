using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Domain.Execution
{
    /// <summary>
    /// Контракт атомарного шага тестирования.
    /// Шаг выполняет одну законченную операцию и возвращает результат,
    /// определяющий дальнейший переход в графе теста.
    /// </summary>
    public interface ITestStep
    {
        /// <summary>
        /// Выполняет шаг теста.
        /// </summary>
        /// <param name="context">
        /// Общий контекст выполнения теста, содержащий состояние и данные,
        /// необходимые для работы шагов.
        /// </param>
        /// <param name="cancellationToken">
        /// Токен отмены для корректного прерывания выполнения теста.
        /// </param>
        /// <returns>
        /// Результат выполнения шага, определяющий следующий переход.
        /// </returns>
        Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken);
    }
}
