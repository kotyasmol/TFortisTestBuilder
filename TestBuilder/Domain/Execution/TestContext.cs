using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Modbus;

namespace TestBuilder.Domain.Execution
{
    public sealed class TestContext
    {
        public Guid ExecutionScopeId { get; } = Guid.NewGuid();

        public Guid? ParentExecutionScopeId { get; private init; }

        public bool IsParallelBranch => ParentExecutionScopeId.HasValue;

        public RegisterMonitor? RegisterMonitor { get; set; }

        public RegisterState RegisterState { get; }

        public ModbusRegisterCatalog ModbusRegisters { get; init; } = ModbusRegisterCatalog.Empty;

        public CancellationToken CancellationToken { get; set; }

        public bool IsConnected { get; set; }

        public string? ProfileName { get; set; }

        public bool HasCriticalError { get; set; }

        public ExecutionFailure? Failure { get; internal set; }
        public ExecutionFailure? CriticalFailure { get; internal set; }

        public byte? CurrentSlaveId { get; set; }

        public IExecutionObserver? ExecutionObserver { get; set; }

        public Func<string, Task<bool>>? OperatorPrompt { get; set; }

        public Func<CancellationToken, Task>? WaitIfPausedAsync { get; set; }

        public SelfTestPageState? SelfTestPageState { get; set; }

        public ContextVariables Variables { get; } = new();

        public List<TestReportEntry> ReportEntries { get; } = new();

        public List<ParallelBranchResult> ParallelBranchResults { get; } = new();

        internal TestContext CreateParallelBranch(CancellationToken cancellationToken)
        {
            var branch = new TestContext(RegisterState)
            {
                ParentExecutionScopeId = ExecutionScopeId,
                RegisterMonitor = RegisterMonitor,
                ModbusRegisters = ModbusRegisters,
                CancellationToken = cancellationToken,
                IsConnected = IsConnected,
                ProfileName = ProfileName,
                HasCriticalError = HasCriticalError,
                CurrentSlaveId = CurrentSlaveId,
                ExecutionObserver = ExecutionObserver,
                OperatorPrompt = OperatorPrompt,
                WaitIfPausedAsync = WaitIfPausedAsync,
                SelfTestPageState = SelfTestPageState
            };
            foreach (var pair in Variables)
            {
                try { branch.Variables.Seed(pair.Key, ParallelVariableSnapshot.Clone(pair.Value)); }
                catch (InvalidOperationException error)
                {
                    throw new InvalidOperationException($"Нельзя разделить переменную '{pair.Key}': {error.Message}", error);
                }
            }
            branch.ReportEntries.AddRange(ReportEntries);
            return branch;
        }

        internal void MergeParallelBranches(IReadOnlyList<TestContext> branches,
            IReadOnlyList<ExecutionStatus> statuses, IReadOnlyList<Exception?> errors)
        {
            Failure = branches.Where((branch, index) => statuses[index] == ExecutionStatus.Failed && branch.Failure != null)
                .Select(branch => branch.Failure).OrderBy(failure => failure!.Sequence).FirstOrDefault() ?? Failure;
            CriticalFailure ??= branches.Select(branch => branch.CriticalFailure)
                .Where(failure => failure != null).OrderBy(failure => failure!.Sequence).FirstOrDefault();
            var reportPrefixLength = ReportEntries.Count;
            var writes = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var snapshots = new List<Dictionary<string, object>>();
            var invalidKeys = new HashSet<string>(StringComparer.Ordinal);
            var conflicts = new List<string>();
            for (var index = 0; index < branches.Count; index++)
            {
                var branch = branches[index];
                HasCriticalError |= branch.HasCriticalError || statuses[index] != ExecutionStatus.Completed;
                ReportEntries.AddRange(branch.ReportEntries.Skip(reportPrefixLength));
                ParallelBranchResults.AddRange(branch.ParallelBranchResults);
                var snapshot = new Dictionary<string, object>();
                var branchErrors = new List<string>();
                foreach (var pair in branch.Variables)
                {
                    try { snapshot[pair.Key] = ParallelVariableSnapshot.Clone(pair.Value); }
                    catch (InvalidOperationException error)
                    {
                        var message = $"'{pair.Key}' (ветвь {index + 1}): {error.Message}";
                        invalidKeys.Add(pair.Key);
                        conflicts.Add(message);
                        branchErrors.Add(message);
                    }
                }
                snapshots.Add(snapshot);
                ParallelBranchResults.Add(new ParallelBranchResult(branch.ExecutionScopeId, ExecutionScopeId,
                    index, branchErrors.Count == 0 ? statuses[index] : ExecutionStatus.Failed,
                    string.Join("; ", branchErrors.Prepend(errors[index]?.Message ?? string.Empty)
                        .Where(message => message.Length > 0)), new ReadOnlyDictionary<string, object>(snapshot)));
            }
            for (var index = 0; index < branches.Count; index++)
            {
                var branch = branches[index];
                // Structural comparison also finds mutations performed inside a cloned container.
                foreach (var key in branch.Variables.WrittenKeys.Concat(Variables.Keys).Concat(snapshots[index].Keys).Distinct())
                {
                    if (key == "slaveId" || invalidKeys.Contains(key)) continue;
                    var beforeExists = Variables.TryGetValue(key, out var before);
                    var afterExists = snapshots[index].TryGetValue(key, out var after);
                    if (!branch.Variables.WrittenKeys.Contains(key) && beforeExists == afterExists &&
                        ParallelVariableSnapshot.AreEqual(before, after)) continue;
                    if (!writes.TryGetValue(key, out var writers)) writes[key] = writers = new List<int>();
                    writers.Add(index);
                }
            }

            var changes = new Dictionary<string, (bool Exists, object? Value)>();
            var ambiguousDiagnosticPrefixes = new[] { "LastCheck.", "WaitVariable." }
                .Where(prefix => writes.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                    .SelectMany(pair => pair.Value).Distinct().Count() > 1).ToArray();
            bool IsAmbiguousDiagnostic(string key) => ambiguousDiagnosticPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal));
            foreach (var key in Variables.Keys.Where(IsAmbiguousDiagnostic))
                    changes[key] = (false, null);
            foreach (var pair in writes)
            {
                var first = snapshots[pair.Value[0]];
                var exists = first.TryGetValue(pair.Key, out var value);
                var ambiguous = pair.Value.Skip(1).Any(index =>
                    snapshots[index].TryGetValue(pair.Key, out var other) != exists ||
                    !ParallelVariableSnapshot.AreEqual(value, other));
                if (IsAmbiguousDiagnostic(pair.Key))
                    changes[pair.Key] = (false, null);
                else if (ambiguous)
                    conflicts.Add($"'{pair.Key}' (ветви {string.Join(", ", pair.Value.Select(index => index + 1))})");
                else
                    changes[pair.Key] = (exists, value);
            }

            // Preserve independent observations for cleanup/reporting even when another key conflicts.
            foreach (var change in changes)
            {
                if (change.Value.Exists) Variables[change.Key] = ParallelVariableSnapshot.Clone(change.Value.Value!);
                else Variables.Remove(change.Key);
            }
            if (conflicts.Count > 0)
            {
                HasCriticalError = true;
                throw new InvalidOperationException("Конфликт переменных параллельных ветвей: " +
                    string.Join("; ", conflicts) + ". Используйте разные имена и поддерживаемые типы выходных переменных.");
            }
        }

        public TestContext(RegisterState registerState)
        {
            RegisterState = registerState;
        }

        public T? GetVariable<T>(string name)
        {
            if (Variables.TryGetValue(name, out var value) && value is T typed)
                return typed;

            return default;
        }

        public void SetVariable(string name, object value)
        {
            Variables[name] = value;
        }

        public void AddReportEntry(string name, bool isSuccess, string value)
        {
            ReportEntries.Add(new TestReportEntry(
                name?.Trim() ?? string.Empty,
                isSuccess,
                value ?? string.Empty));
        }

        public async Task WaitWhilePausedAsync(CancellationToken cancellationToken)
        {
            var waiting = WaitIfPausedAsync?.Invoke(cancellationToken) ?? Task.CompletedTask;
            if (waiting.IsCompleted || ExecutionObserver == null)
            {
                await waiting;
                return;
            }
            await ExecutionObserver.PauseWaitingChangedAsync(this, true, CancellationToken.None);
            try { await waiting; }
            finally { await ExecutionObserver.PauseWaitingChangedAsync(this, false, CancellationToken.None); }
        }
    }
}
