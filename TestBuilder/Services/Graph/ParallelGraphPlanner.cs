using System;
using System.Collections.Generic;
using System.Linq;
using TestBuilder.Domain.Execution;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Services.Graph;

/// <summary>
/// Infers structured fork/join regions from ordinary connections. A join belongs to
/// an activation of one output, not to every incoming edge of a node. Conditional
/// alternatives therefore never wait for a path that was not selected.
/// </summary>
internal static class ParallelGraphPlanner
{
    public static void Plan(Dictionary<TestNode, Dictionary<StepResult, List<TestNode>>> edges)
    {
        foreach (var (source, outgoing) in edges)
        foreach (var (result, branches) in outgoing.Where(pair => pair.Value.Count > 1))
        {
            var distances = branches.Select(branch => Distances(branch, edges)).ToArray();
            var common = new HashSet<TestNode>(distances[0].Keys);
            foreach (var distance in distances.Skip(1)) common.IntersectWith(distance.Keys);
            common.Remove(source);

            // Pick the earliest common node which every connected path reaches.
            // Looking only at reachability is insufficient (conditional bypasses,
            // partial merges and cycles could otherwise create duplicate work).
            TestNode? join = null;
            List<HashSet<TestNode>>? regions = null;
            string? reason = null;
            foreach (var candidate in common.OrderBy(n => distances.Sum(d => (long)d[n])))
            {
                var candidateRegions = new List<HashSet<TestNode>>();
                var valid = true;
                foreach (var branch in branches)
                {
                    var region = new HashSet<TestNode>();
                    if (!ReachesJoin(branch, candidate, source, edges, region, new(), new()))
                    {
                        valid = false;
                        break;
                    }
                    candidateRegions.Add(region);
                }
                if (!valid) continue;
                if (!IsStructured(source, branches, candidateRegions, edges, out reason)) continue;
                join = candidate;
                regions = candidateRegions;
                break;
            }

            if (join == null || regions == null)
                throw new InvalidOperationException($"Параллельные ветви '{Name(source)}' ({result}): " +
                    (reason ?? "нет общего объединения для всех путей. Соедините ветви общей следующей нодой; циклы и выход из ветви до объединения недопустимы."));

            ParallelResourceValidator.Validate(source, regions);
            source.ParallelTransitions[result] = new ParallelFork(branches.ToArray(), join);
        }
    }

    private static Dictionary<TestNode, int> Distances(TestNode start,
        Dictionary<TestNode, Dictionary<StepResult, List<TestNode>>> edges)
    {
        var distance = new Dictionary<TestNode, int> { [start] = 0 };
        var pending = new Queue<TestNode>();
        pending.Enqueue(start);
        while (pending.TryDequeue(out var node))
        foreach (var next in edges[node].Values.SelectMany(x => x))
            if (distance.TryAdd(next, distance[node] + 1)) pending.Enqueue(next);
        return distance;
    }

    private static bool ReachesJoin(TestNode node, TestNode join, TestNode fork,
        Dictionary<TestNode, Dictionary<StepResult, List<TestNode>>> edges,
        HashSet<TestNode> region, HashSet<TestNode> visiting, HashSet<TestNode> completed)
    {
        if (ReferenceEquals(node, join)) return true;
        if (ReferenceEquals(node, fork) || node.Source is BodyEndNodeViewModel) return false;
        if (completed.Contains(node)) return true;
        if (!visiting.Add(node)) return false;
        region.Add(node);

        // An unconnected False aborts the branch, which is allowed. An unconnected
        // successful result would silently leave the region without joining.
        var success = node.Source is ForEachSlaveNodeViewModel ? StepResult.Next
            : node.Source is NodeViewModel vm && vm.Output.Count > 1 ? StepResult.True : StepResult.Next;
        if (!edges[node].ContainsKey(success)) return false;
        foreach (var next in edges[node].Values.SelectMany(x => x))
            if (!ReachesJoin(next, join, fork, edges, region, visiting, completed)) return false;
        visiting.Remove(node);
        completed.Add(node);
        return true;
    }

    private static bool IsStructured(TestNode source, IReadOnlyList<TestNode> branches,
        IReadOnlyList<HashSet<TestNode>> regions,
        Dictionary<TestNode, Dictionary<StepResult, List<TestNode>>> edges, out string? reason)
    {
        for (var i = 0; i < regions.Count; i++)
        {
            for (var j = i + 1; j < regions.Count; j++)
                if (regions[i].Overlaps(regions[j]))
                {
                    reason = "ветви пересекаются до общего объединения. Частичные объединения и переходы между соседними ветвями недопустимы.";
                    return false;
                }
            foreach (var (predecessor, outgoing) in edges)
            foreach (var target in outgoing.Values.SelectMany(x => x))
                if (regions[i].Contains(target) && !regions[i].Contains(predecessor)
                    && !(ReferenceEquals(predecessor, source) && ReferenceEquals(target, branches[i])))
                {
                    reason = $"внешняя связь входит в середину ветви ('{Name(target)}'). Входить в ветвь можно только через её разветвление.";
                    return false;
                }
        }
        reason = null;
        return true;
    }

    internal static string Name(TestNode node) => node.Source switch
    {
        SubtestNodeViewModel subtest => subtest.Name,
        NodeViewModel vm => vm.Title,
        _ => node.Step?.GetType().Name ?? "нода"
    };
}
