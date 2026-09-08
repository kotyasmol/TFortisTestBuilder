namespace TestBuilder.Domain.Execution
{
    public sealed record TestReportEntry(
        string Name,
        bool IsSuccess,
        string Value);
}
