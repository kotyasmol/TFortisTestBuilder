using System;
using System.Collections.Generic;
using System.Linq;
using TestBuilder.Domain.Execution;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.ViewModels;

/// <summary>UI-thread state for interleaved execution branches and their nested subtests.</summary>
internal sealed class ExecutionUiState
{
    private readonly Dictionary<Guid, Guid?> _parents = new();
    private readonly Dictionary<Guid, List<SubtestNodeViewModel>> _subtests = new();
    private readonly HashSet<(Guid Scope, NodeViewModel Node)> _active = new();
    private readonly HashSet<Guid> _waitingScopes = new();

    public bool CanPause(bool awaitingOperator) => _active.All(item =>
        _waitingScopes.Contains(item.Scope) ||
        item.Node is ICompositeNodeViewModel ||
        (awaitingOperator && item.Node is OperatorActionNodeViewModel));

    public void SetScopeWaiting(Guid scope, bool waiting)
    {
        if (waiting) _waitingScopes.Add(scope);
        else _waitingScopes.Remove(scope);
    }

    public void NodeStarted(NodeViewModel node, Guid scope, Guid? parent)
    {
        _parents[scope] = parent;
        var activeSubtest = FindSubtest(scope);
        if (activeSubtest != null && !ReferenceEquals(activeSubtest, node))
            activeSubtest.UpdateProgress(node);

        if (node is SubtestNodeViewModel subtest)
        {
            if (!_subtests.TryGetValue(scope, out var stack))
                _subtests.Add(scope, stack = new List<SubtestNodeViewModel>());
            subtest.BeginProgress();
            stack.Add(subtest);
        }

        _active.Add((scope, node));
        node.HasExecutionSucceeded = false;
        node.IsExecuting = true;
    }

    public void NodeCompleted(NodeViewModel node, StepResult result)
    {
        // A finally notification also happens on cancellation; only a returned
        // successful result can turn a node green. Disabled subtests are skipped.
        node.HasExecutionSucceeded = result is StepResult.Next or StepResult.True or StepResult.Stop
            && !node.HasExecutionError && node is not SubtestNodeViewModel { IsEnabled: false };
    }

    public void NodeFinished(NodeViewModel node, Guid scope)
    {
        _active.Remove((scope, node));
        node.IsExecuting = _active.Any(item => ReferenceEquals(item.Node, node));
        if (node is SubtestNodeViewModel subtest)
        {
            if (_subtests.TryGetValue(scope, out var stack)) stack.Remove(subtest);
            subtest.EndProgress();
        }
    }

    private SubtestNodeViewModel? FindSubtest(Guid scope)
    {
        while (true)
        {
            if (_subtests.TryGetValue(scope, out var stack) && stack.Count > 0)
                return stack[^1];
            if (!_parents.TryGetValue(scope, out var parent) || parent == null) return null;
            scope = parent.Value;
        }
    }

    public void FinishScope(Guid scope)
    {
        foreach (var item in _active.Where(item => item.Scope == scope).ToArray())
            NodeFinished(item.Node, scope);
        _subtests.Remove(scope);
        _parents.Remove(scope);
        _waitingScopes.Remove(scope);
    }

    public void Clear()
    {
        foreach (var item in _active.ToArray()) NodeFinished(item.Node, item.Scope);
        _active.Clear();
        _subtests.Clear();
        _parents.Clear();
        _waitingScopes.Clear();
    }
}
