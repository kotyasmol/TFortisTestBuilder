using System;
using System.Collections.Generic;
using System.Linq;

namespace TestBuilder.Domain.Execution;

/// <summary>A compiler-validated, structured parallel region. The join runs in the parent scope.</summary>
public sealed class ParallelFork
{
    public IReadOnlyList<TestNode> Branches { get; }
    public TestNode JoinNode { get; }

    public ParallelFork(IReadOnlyList<TestNode> branches, TestNode joinNode)
    {
        ArgumentNullException.ThrowIfNull(branches);
        ArgumentNullException.ThrowIfNull(joinNode);
        if (branches.Count < 2 || branches.Any(branch => branch == null))
            throw new ArgumentException("Параллельное ветвление требует хотя бы две ветви.", nameof(branches));
        if (branches.Distinct().Count() != branches.Count)
            throw new ArgumentException("Одинаковая ветвь не может запускаться дважды.", nameof(branches));
        Branches = Array.AsReadOnly(branches.ToArray());
        JoinNode = joinNode;
    }
}
