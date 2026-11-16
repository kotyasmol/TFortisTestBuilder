using System;
using System.Collections.Generic;
using System.Linq;
using TestBuilder.Serialization;

namespace TestBuilder.Services;

internal static class ProfilePurpose
{
    internal const string Production = "production";
    internal const string Diagnostic = "diagnostic";

    internal static string Resolve(GraphDto graph)
    {
        if (!string.IsNullOrWhiteSpace(graph.ProfilePurpose))
        {
            if (graph.ProfilePurpose.Equals(Production, StringComparison.OrdinalIgnoreCase)) return Production;
            if (graph.ProfilePurpose.Equals(Diagnostic, StringComparison.OrdinalIgnoreCase)) return Diagnostic;
            throw new InvalidOperationException("Назначение профиля: production или diagnostic.");
        }
        // Compatibility with already distributed diagnostic JSONs: use report
        // semantics, never a filename, date or a fixed list of product models.
        var reports = Nodes(graph).Where(n => n.Type.StartsWith("Build Test Report", StringComparison.Ordinal)).ToArray();
        return reports.Length > 0 && reports.All(n =>
            string.Equals(n.TestType, Diagnostic, StringComparison.OrdinalIgnoreCase)) ? Diagnostic : Production;
    }

    private static IEnumerable<NodeDto> Nodes(GraphDto graph) => graph.Nodes.SelectMany(node =>
        new[] { node }.Concat(node.BodyGraph == null ? Enumerable.Empty<NodeDto>() : Nodes(node.BodyGraph)));
}
