using System;
using System.Collections.Generic;
using System.Linq;
using TestBuilder.Services.Help;

namespace TestBuilder.ViewModels.NodifyVM;

internal static class NodeHelpTextProvider
{
    public static string GetHelp(Type nodeType) => UserGuide.ForNode(nodeType)?.BodyText ?? string.Empty;
    public static string GetSummary(Type nodeType) => UserGuide.ForNode(nodeType)?.Summary ?? string.Empty;
    public static IReadOnlyList<string> GetDetails(Type nodeType) =>
        UserGuide.ForNode(nodeType)?.Blocks.Select(b => b.Heading + "\n" + string.Join("\n", b.Items)).ToArray() ?? Array.Empty<string>();
}
