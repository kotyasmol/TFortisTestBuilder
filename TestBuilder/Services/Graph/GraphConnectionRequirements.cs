using System.Linq;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Services.Graph
{
    internal static class GraphConnectionRequirements
    {
        public static bool RequiresStandConnection(GraphWorkspaceViewModel graph) =>
            graph.Nodes.Any(RequiresStandConnection);

        private static bool RequiresStandConnection(NodeViewModel node)
        {
            if (node is ModbusWriteNodeViewModel or
                CheckRegisterRangeNodeViewModel or
                CheckRegisterEqualityNodeViewModel or
                WaitUntilNodeViewModel or
                PollRegisterNodeViewModel)
            {
                return true;
            }

            if (node is SubtestNodeViewModel { IsEnabled: false })
            {
                return false;
            }

            return node is ICompositeNodeViewModel composite &&
                   RequiresStandConnection(composite.BodyGraph);
        }
    }
}
