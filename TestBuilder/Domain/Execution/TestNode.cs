using System.Collections.Generic;

namespace TestBuilder.Domain.Execution
{
    /// <summary>
    /// Узел графа тестирования.
    /// Содержит ссылку на шаг и возможные переходы выполнения.
    /// </summary>
    public sealed class TestNode
    {
        /// <summary>
        /// Логика, выполняемая в данном узле.
        /// </summary>
        public ITestStep? Step { get; init; }

        /// <summary>
        /// Исходная визуальная нода, из которой был скомпилирован TestNode.
        /// Domain не знает конкретный тип UI, поэтому поле имеет тип object.
        /// </summary>
        public object? Source { get; init; }

        public string DisplayName { get; init; } = string.Empty;

        /// <summary>
        /// Следующий узел при линейном выполнении.
        /// </summary>
        public TestNode? Next { get; set; }

        /// <summary>
        /// Узел, выполняемый при результате True.
        /// </summary>
        public TestNode? OnTrue { get; set; }

        /// <summary>
        /// Узел, выполняемый при результате False.
        /// </summary>
        public TestNode? OnFalse { get; set; }

        /// <summary>Validated fan-outs of one output; each branch stops before its common join.</summary>
        public Dictionary<StepResult, ParallelFork> ParallelTransitions { get; } = new();

        public TestNode(ITestStep? step, object? source = null)
        {
            Step = step;
            Source = source;
        }
    }
}
