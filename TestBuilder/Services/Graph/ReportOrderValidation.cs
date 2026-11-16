using System;
using System.Collections.Generic;
using System.Linq;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Domain.Steps;

namespace TestBuilder.Services.Graph;

internal static class ReportOrderValidation
{
    internal static void Validate(GraphWorkspaceViewModel graph, IReadOnlySet<NodeViewModel> nodes)
    {
        foreach (var report in nodes.Where(n => Contains<SendTestReportNodeViewModel>(n, new())))
        {
            var seen = new HashSet<NodeViewModel> { report };
            var pending = new Queue<NodeViewModel>();
            pending.Enqueue(report);
            while (pending.TryDequeue(out var current))
            foreach (var next in graph.Connections.Where(c => ReferenceEquals(c.Source.Parent, current))
                         .Select(c => c.Target.Parent).OfType<NodeViewModel>().Where(nodes.Contains))
            {
                if (!seen.Add(next)) continue;
                if (Contains<PrintLabelNodeViewModel>(next, new()))
                    throw new InvalidOperationException($"Граф «{graph.Title}»: печать «{next.DisplayTitle}» находится после отправки отчёта «{report.DisplayTitle}». " +
                        "Перенесите печать перед итоговым отчётом: её ошибка должна попасть в результат проверки.");
                pending.Enqueue(next);
            }
        }
    }

    private static bool Contains<T>(NodeViewModel node, HashSet<GraphWorkspaceViewModel> seen)
    {
        if (node is SubtestNodeViewModel { IsEnabled: false }) return false;
        // RTL POST also allocates the board serial number. Its label necessarily
        // follows that request; keep that protocol distinct from switch reports.
        if (node is T) return node is not SendTestReportNodeViewModel send || !RtlTestReport.IsEndpoint(send.Endpoint);
        return node is ICompositeNodeViewModel composite && seen.Add(composite.BodyGraph) &&
            composite.BodyGraph.Nodes.Any(n => Contains<T>(n, seen));
    }
}
