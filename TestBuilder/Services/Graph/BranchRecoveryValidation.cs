using System;
using System.Collections.Generic;
using System.Linq;
using TestBuilder.Domain.Execution;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Services.Graph;

internal static class BranchRecoveryValidation
{
    internal static bool IsFalse(ConnectorViewModel connector) => connector.Title is "False" or "Error";
    private static bool IsSuccess(ConnectionViewModel edge) => !IsFalse(edge.Source) &&
        edge.Source.Parent is { Output.Count: > 1 } node && node.Output.Any(IsFalse);

    internal static IReadOnlyList<NodeViewModel> Candidates(GraphWorkspaceViewModel graph, ConnectionViewModel edge)
    {
        if (!IsSuccess(edge) || !graph.Connections.Contains(edge)) return Array.Empty<NodeViewModel>();
        return graph.Nodes.Where(node => node.Output.Any(IsFalse) &&
            FailureRoutingEnabled(node) && ReachableOnFalse(graph, node, edge.Source.Parent!)).ToArray();
    }

    internal static void Validate(GraphWorkspaceViewModel graph)
    {
        foreach (var edge in graph.Connections.Where(c => c.RecoversFailureOf != null))
        {
            var source = edge.RecoversFailureOf!;
            if (!graph.Nodes.Contains(source) || !graph.Nodes.Contains(edge.Source.Parent!) || !graph.Nodes.Contains(edge.Target.Parent!))
                throw new InvalidOperationException("Связь устранения ошибки должна ссылаться на ноды этого же графа.");
            if (!IsSuccess(edge))
                throw new InvalidOperationException("Устранение ошибки задаётся на успешном выходе проверяющей ноды, у которой есть выход ошибки.");
            if (!FailureRoutingEnabled(source))
                throw new InvalidOperationException($"Нода «{source.DisplayTitle}»: включите остановку при ошибке, чтобы выполнялся выход False, а не продолжение по True.");
            if (!ReachableOnFalse(graph, source, edge.Source.Parent!))
                throw new InvalidOperationException($"Подтверждающая проверка должна быть достижима по ветке False ноды «{source.DisplayTitle}».");
        }
    }

    private static bool ReachableOnFalse(GraphWorkspaceViewModel graph, NodeViewModel source, NodeViewModel target)
    {
        var seen = new HashSet<NodeViewModel>();
        var pending = new Queue<NodeViewModel>(graph.Connections.Where(c => c.Source.Parent == source && IsFalse(c.Source))
            .Select(c => c.Target.Parent).OfType<NodeViewModel>());
        while (pending.TryDequeue(out var node))
        {
            if (!seen.Add(node)) continue;
            if (node == target) return true;
            foreach (var next in graph.Connections.Where(c => c.Source.Parent == node).Select(c => c.Target.Parent).OfType<NodeViewModel>()) pending.Enqueue(next);
        }
        return false;
    }

    private static bool FailureRoutingEnabled(NodeViewModel node) => node switch
    {
        SelfTestCheckNodeViewModel n => n.FailOnError,
        ClearArpCacheNodeViewModel n => n.FailOnError,
        GetSerialNumberFromServerNodeViewModel n => n.FailOnError,
        SetProMacNodeViewModel n => n.FailOnError,
        SetPswMacNodeViewModel n => n.FailOnError,
        RunDataTestNodeViewModel n => n.FailOnError,
        ReadHttpVariableNodeViewModel n => n.FailOnError,
        BuildMacFromSerialNodeViewModel n => n.FailOnError,
        PrintLabelNodeViewModel n => n.FailOnPrinterError,
        SendTestReportNodeViewModel n => n.FailOnError,
        WaitVariableUntilNodeViewModel n => n.FailOnTimeout,
        SubtestNodeViewModel n => n.StopOnError,
        ForEachSlaveNodeViewModel n => n.StopOnError,
        _ => true
    };

    internal static void ValidateExecutionScopes(GraphWorkspaceViewModel graph, Dictionary<NodeViewModel, TestNode> map,
        Dictionary<TestNode, Dictionary<StepResult, List<TestNode>>> transitions)
    {
        if (!graph.Connections.Any(c => c.RecoversFailureOf != null)) return;
        var owners = map.Values.ToDictionary(node => node, _ => new HashSet<TestNode>());
        foreach (var fork in map.Values.SelectMany(node => node.ParallelTransitions.Values))
        foreach (var branch in fork.Branches)
        {
            var seen = new HashSet<TestNode>(); var pending = new Stack<TestNode>(); pending.Push(branch);
            while (pending.TryPop(out var node))
            {
                if (node == fork.JoinNode || !seen.Add(node)) continue;
                owners[node].Add(branch);
                foreach (var next in transitions[node].Values.SelectMany(x => x)) pending.Push(next);
            }
        }
        foreach (var edge in graph.Connections.Where(c => c.RecoversFailureOf != null && map.ContainsKey(c.Source.Parent!)))
        {
            if (map.ContainsKey(edge.RecoversFailureOf!) &&
                !owners[map[edge.Source.Parent!]].SetEquals(owners[map[edge.RecoversFailureOf!]]))
                throw new InvalidOperationException("Ошибка и её подтверждение должны находиться в одной параллельной ветви либо оба вне параллельных ветвей. Перенесите подтверждение после объединения ветвей.")
                    .WithCode(TestErrorCode.ParallelGraph);
        }
    }
}
