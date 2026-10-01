using System;
using System.Collections.Generic;

namespace TestBuilder.Domain.Execution;

/// <summary>Diagnostics retained after a join, including values with no unambiguous global meaning.</summary>
public sealed record ParallelBranchResult(
    Guid ExecutionScopeId,
    Guid ParentExecutionScopeId,
    int BranchIndex,
    ExecutionStatus Status,
    string Error,
    IReadOnlyDictionary<string, object> Variables);
