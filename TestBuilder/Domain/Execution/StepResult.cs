using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestBuilder.Domain.Execution
{
    /// <summary>
    /// Результат выполнения шага теста.
    /// Определяет, по какому переходу будет продолжено выполнение графа.
    /// </summary>
    public enum StepResult
    {
        /// <summary>
        /// Переход по основному (линейному) пути выполнения.
        /// </summary>
        Next,

        /// <summary>
        /// Условный переход при положительном результате.
        /// </summary>
        True,

        /// <summary>
        /// Условный переход при отрицательном результате.
        /// </summary>
        False
    }
}
