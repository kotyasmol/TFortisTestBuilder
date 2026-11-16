using TestBuilder.Services.Graph;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Domain.Steps;

namespace TestBuilder.Tests.SerializationTests;

public class ReportOrderValidationTests
{
    [Fact]
    public void RtlSerialAllocationResponseMayPrecedeItsLabel()
    {
        var send = new SendTestReportNodeViewModel { Endpoint = RtlTestReport.Endpoint };
        var print = new PrintLabelNodeViewModel();
        var graph = new GraphWorkspaceViewModel(); graph.Nodes.Add(send); graph.Nodes.Add(print);
        Link(graph, send, print);
        ReportOrderValidation.Validate(graph, graph.Nodes.ToHashSet());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReportBeforeEnabledPrintIsRejectedEvenAcrossCompositeBoundaries(bool nested)
    {
        var send = new SendTestReportNodeViewModel();
        var print = new PrintLabelNodeViewModel();
        var stage = new SubtestNodeViewModel { IsEnabled = true };
        stage.BodyGraph.Nodes.Add(print);
        var graph = new GraphWorkspaceViewModel();
        graph.Nodes.Add(send); graph.Nodes.Add(nested ? stage : print);
        Link(graph, send, nested ? stage : print);
        Assert.Throws<InvalidOperationException>(() => ReportOrderValidation.Validate(graph, graph.Nodes.ToHashSet()));
        stage.IsEnabled = false;
        if (nested) ReportOrderValidation.Validate(graph, graph.Nodes.ToHashSet());
    }

    [Fact]
    public void PrintBeforeReportAndIndependentParallelBranchesAreAllowed()
    {
        var send = new SendTestReportNodeViewModel(); var print = new PrintLabelNodeViewModel();
        var graph = new GraphWorkspaceViewModel(); graph.Nodes.Add(print); graph.Nodes.Add(send);
        Link(graph, print, send);
        var start = new StartNodeViewModel(); var unrelated = new DelayNodeViewModel();
        graph.Nodes.Add(start); graph.Nodes.Add(unrelated);
        Link(graph, start, print); Link(graph, start, unrelated);
        ReportOrderValidation.Validate(graph, graph.Nodes.ToHashSet());
    }

    private static void Link(GraphWorkspaceViewModel graph, NodeViewModel source, NodeViewModel target) =>
        graph.Connections.Add(new ConnectionViewModel(source.Output[0], target.Input[0]));
}
