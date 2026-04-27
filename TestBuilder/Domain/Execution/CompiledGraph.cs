namespace TestBuilder.Domain.Execution
{
    /// <summary>
    /// Результат компиляции визуального графа в исполняемый граф TestNode.
    /// </summary>
    public sealed class CompiledGraph
    {
        public TestNode StartNode { get; }

        public CompiledGraph(TestNode startNode)
        {
            StartNode = startNode;
        }
    }
}
