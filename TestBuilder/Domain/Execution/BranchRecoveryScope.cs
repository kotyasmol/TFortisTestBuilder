using System;
using System.Collections.Generic;
using System.Linq;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Execution;

// One execution frame owns its pending failures. Nested graphs and parallel
// branches cannot acknowledge a failure in another frame by accident.
internal sealed class BranchRecoveryScope(TestContext context) : IDisposable
{
    private sealed record Pending(ExecutionFailure Cause, HashSet<TestReportEntry> Entries, ILogger Logger);
    private readonly BranchRecoveryScope? _parent = context.RecoveryScope;
    private readonly Dictionary<Guid, Pending> _pending = new();

    internal void Add(Guid key, ExecutionFailure cause, IEnumerable<TestReportEntry> entries, ILogger logger)
    {
        var failed = new HashSet<TestReportEntry>(entries.Where(e => !e.IsSuccess), ReferenceEqualityComparer.Instance);
        if (_pending.TryGetValue(key, out var earlier)) { earlier.Entries.UnionWith(failed); return; }
        _pending[key] = new(cause, failed, logger);
    }

    internal void Confirm(Guid key, string confirmationName)
    {
        if (!_pending.Remove(key, out var pending)) return;
        context.ReportEntries.RemoveAll(entry => pending.Entries.Contains(entry));
        pending.Logger.Info($"[ВОССТАНОВЛЕНО] Ошибка шага «{pending.Cause.StepName}» устранена. Подтверждение: «{confirmationName}».");
    }

    internal ExecutionStatus Complete(ExecutionStatus status)
    {
        if (status != ExecutionStatus.Completed || _pending.Count == 0) return status;
        context.Failure = _pending.Values.Select(p => p.Cause).OrderBy(c => c.Sequence).First();
        context.CriticalFailure ??= context.Failure;
        context.HasCriticalError = true;
        return ExecutionStatus.Failed;
    }

    public void Dispose() => context.RecoveryScope = _parent;
}
