using System;
using System.Collections.Generic;
using System.Linq;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Services.Graph;

/// <summary>
/// Preparation is a consecutive sequence of root operations immediately after Start.
/// A selected composite executes its complete body in the normal runtime; extracting
/// arbitrary nested nodes would lose their slave, variables and control-flow context.
/// </summary>
internal sealed class ProfilePreparationPlan
{
    private ProfilePreparationPlan(List<NodeViewModel> nodes) => Nodes = nodes.AsReadOnly();

    public IReadOnlyList<NodeViewModel> Nodes { get; }

    public bool RequiresStandConnection => Nodes.Any(GraphConnectionRequirements.RequiresStandConnection);

    public static ProfilePreparationPlan Create(GraphWorkspaceViewModel root)
    {
        ValidateFlags(root, insidePreparation: false, isRoot: true, new HashSet<GraphWorkspaceViewModel>());
        var marked = root.Nodes.Where(node => node.RunOnlyOnFirstRun).ToHashSet();
        var ordered = new List<NodeViewModel>();
        if (marked.Count == 0) return new ProfilePreparationPlan(ordered);

        var starts = root.Nodes.OfType<StartNodeViewModel>().ToArray();
        if (starts.Length != 1)
            throw new InvalidOperationException("Для подготовки профиля в основном графе должна быть ровно одна нода «Старт».");

        NodeViewModel current = starts[0];
        var visited = new HashSet<NodeViewModel>();
        while (current.Output.Count > 0)
        {
            // Every executable node exposes its normal/success output first.
            var normalOutput = current.Output[0];
            var nextEdges = root.Connections.Where(edge => ReferenceEquals(edge.Source, normalOutput)).ToArray();
            if (nextEdges.Any(edge => edge.Target.Parent is not { } target ||
                !root.Nodes.Contains(target) || !target.Input.Contains(edge.Target)))
                throw new InvalidOperationException($"Нода «{current.DisplayTitle}» должна быть связана со входом ноды в основном графе.");
            var nextNodes = nextEdges.Select(edge => edge.Target.Parent).ToArray();
            if (nextNodes.Length == 0) break;
            if (nextNodes.Length > 1)
            {
                if (nextNodes.Any(node => node!.RunOnlyOnFirstRun))
                    throw new InvalidOperationException("Ноды подготовки при открытии профиля должны идти последовательно. Поместите параллельные действия внутрь одного отмеченного подтеста.");
                // Normal testing may fork immediately after the last preparation node.
                break;
            }

            var next = nextNodes[0]!;
            if (!next.RunOnlyOnFirstRun) break;
            if (!visited.Add(next))
                throw new InvalidOperationException("Ноды подготовки при открытии профиля образуют цикл. Соедините их последовательно после ноды «Старт».");
            ordered.Add(next);
            current = next;
        }

        var outsidePrefix = marked.Except(ordered).FirstOrDefault();
        if (outsidePrefix != null)
            throw new InvalidOperationException($"Нода «{outsidePrefix.DisplayTitle}» отмечена для подготовки при открытии профиля. Разместите все такие ноды подряд после «Старт», перед проверками изделия, и соедините их обычными выходами.");

        return new ProfilePreparationPlan(ordered);
    }

    private static void ValidateFlags(GraphWorkspaceViewModel graph, bool insidePreparation, bool isRoot,
        HashSet<GraphWorkspaceViewModel> activeGraphs)
    {
        if (!activeGraphs.Add(graph))
            throw new InvalidOperationException($"Граф «{graph.Title}» содержит рекурсивное вложение.");
        try
        {
            foreach (var node in graph.Nodes)
            {
                if (node.RunOnlyOnFirstRun)
                {
                    if (!node.CanRunOnlyOnFirstRun)
                        throw new InvalidOperationException($"Нода «{node.DisplayTitle}» не поддерживает подготовку при открытии профиля.");
                    if (node is SubtestNodeViewModel { RunOnFailure: true })
                        throw new InvalidOperationException($"Подтест «{node.DisplayTitle}» выполняется при аварийном завершении. Снимите с него галочку подготовки при открытии профиля.");
                    if (!isRoot && !insidePreparation)
                        throw new InvalidOperationException($"Нода «{node.DisplayTitle}» находится во вложенном графе. Для подготовки при открытии профиля отметьте весь подтест или цикл в основном графе и разместите его после «Старт».");
                }
                if (node is ICompositeNodeViewModel composite)
                    ValidateFlags(composite.BodyGraph, insidePreparation || node.RunOnlyOnFirstRun,
                        isRoot: false, activeGraphs);
            }
        }
        finally { activeGraphs.Remove(graph); }
    }
}
