namespace TestBuilder.Domain.Execution
{
    /// <summary>
    /// Итог выполнения графа или вложенного подграфа.
    /// </summary>
    public enum ExecutionStatus
    {
        Completed,
        Failed,
        Cancelled
    }
}
