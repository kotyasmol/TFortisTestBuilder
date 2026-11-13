using TestBuilder.Domain.Execution;

namespace TestBuilder.Services.Graph;

internal static class FailureReportPolicy
{
    internal static bool ShouldSend(ExecutionStatus status, TestContext context,
        bool partialRun, bool preparing, bool preparationForRun)
    {
        if (status != ExecutionStatus.Failed || partialRun) return false;
        var model = context.ProductionModel;
        // An assigned number is sufficient. A service number is needed only
        // when the failure happened before assignment, not for every model.
        var hasRoute = context.IssuedSerialNumber.HasValue || model?.SerialSeries?.FailureReportSerialNumber != null;
        return hasRoute && (!preparing || preparationForRun) || !preparing && model?.RtlReport != null;
    }
}
