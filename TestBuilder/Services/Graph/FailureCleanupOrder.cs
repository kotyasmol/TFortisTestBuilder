using System.Collections.Generic;
using System.Linq;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Services.Graph;

internal static class FailureCleanupOrder
{
    // Legacy profiles keep their collection order. New, unnumbered actions go last;
    // stable sorting also makes copied nodes with the same number deterministic.
    public static IReadOnlyList<SubtestNodeViewModel> GetNodes(GraphWorkspaceViewModel graph, bool includeDisabled = false) =>
        graph.Nodes.OfType<SubtestNodeViewModel>()
            .Where(n => n.RunOnFailure && (includeDisabled || n.IsEnabled))
            .OrderBy(n => n.FailureOrder.HasValue ? 0 : 1)
            .ThenBy(n => n.FailureOrder)
            .ToArray();
}
