using System.Threading;

namespace TestBuilder.Domain.Execution;

/// <summary>The original failed step, retained separately from cleanup and branch cancellation.</summary>
public sealed record ExecutionFailure(string StepName, string Reason, byte? SlaveId = null)
{
    private static long _nextSequence;
    internal long Sequence { get; } = Interlocked.Increment(ref _nextSequence);

    public string Summary => $"Шаг «{StepName}»{(SlaveId.HasValue ? $", устройство {SlaveId}" : string.Empty)}: {Reason}";

    public string LogMessage => $"[ПРИЧИНА СБОЯ] {Summary}";
}
