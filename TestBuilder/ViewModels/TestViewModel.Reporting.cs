using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TestBuilder.Domain.Execution;
using TestBuilder.Services;
using TestBuilder.Views;

namespace TestBuilder.ViewModels;

public partial class TestViewModel
{
    private readonly ReportVariableHistory _reportVariableHistory = new();

    internal async Task ConfigureReportTemplateAsync(Window owner)
    {
        if (!CanEditQuickActions) return;
        var graph = RootGraph;
        var old = graph.ReportTemplate;
        var path = _currentProfilePath;
        var observedNames = _reportVariableHistory.Read(path);
        var knownKeys = Enum.GetValues<ReportSourceKind>().ToDictionary(kind => kind,
            kind => ReportSourceCatalog.Collect(graph, kind, observedNames));
        var draft = new ReportTemplateEditorViewModel(old, kind => knownKeys[kind],
            initiallyIncludeKnownVariables: old == null && ReportSourceCatalog.UsesAllVariables(graph));
        var result = await new ReportTemplateWindow(draft).ShowDialog<ReportTemplateEditResult?>(owner);
        if (result == null || !CanEditQuickActions || path != _currentProfilePath ||
            !ReferenceEquals(old, graph.ReportTemplate)) return;

        var next = result.Template;
        if (old == null ? next == null : next != null &&
            old.IncludeAutomaticEntries == next.IncludeAutomaticEntries && old.Fields.SequenceEqual(next.Fields)) return;

        _history.Execute(graph, new ProfileMetadataCommand(
            () => graph.ReportTemplate = next,
            () => graph.ReportTemplate = old));
    }
}
