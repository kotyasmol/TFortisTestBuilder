using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.StepTests;

public class ProfilePreparationPresentationTests
{
    [Fact]
    public void PreparationNodeStarted_KeepsPreparationHeadline()
    {
        var preparation = new SubtestNodeViewModel { RunOnlyOnFirstRun = true };
        var inner = new DelayNodeViewModel();
        preparation.BodyGraph.Nodes.Add(inner);
        var graph = Chain(preparation);
        var dashboard = new StationDashboardViewModel();
        var context = new TestContext(new RegisterState());

        dashboard.BeginRun(graph, preparing: true);
        Assert.Equal("Настройка стенда", dashboard.Headline);
        dashboard.NodeStarted(preparation, context);
        Assert.Equal("Настройка стенда", dashboard.Headline);
        dashboard.NodeStarted(inner, context);
        Assert.Equal("Настройка стенда", dashboard.Headline);
        Assert.False(dashboard.HasResult);
        Assert.False(dashboard.IsSuccessful);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OperatorActionCompletion_RestoresPreparationHeadline(bool confirmed)
    {
        var action = new OperatorActionNodeViewModel { RunOnlyOnFirstRun = true };
        var dashboard = new StationDashboardViewModel();
        var context = new TestContext(new RegisterState())
        {
            OperatorPrompt = message => dashboard.RequestActionAsync(message, CancellationToken.None)
        };
        dashboard.BeginRun(Chain(action), preparing: true);
        dashboard.NodeStarted(action, context);

        var pending = new OperatorActionStep("Подключите стенд", NullLogger.Instance)
            .ExecuteAsync(context, CancellationToken.None);
        Assert.True(dashboard.IsAwaitingAction);
        Assert.Equal("Требуется ваше действие", dashboard.Headline);
        if (confirmed) dashboard.ConfirmActionCommand.Execute(null);
        else dashboard.DeclineActionCommand.Execute(null);

        var result = await pending;
        Assert.Equal(confirmed ? StepResult.True : StepResult.False, result);
        dashboard.NodeCompleted(action, result, context);
        dashboard.NodeFinished(action, context);
        Assert.False(dashboard.IsAwaitingAction);
        Assert.Empty(dashboard.OperatorMessage);
        Assert.Equal("Настройка стенда", dashboard.Headline);
        Assert.False(dashboard.HasResult);
        Assert.False(dashboard.IsSuccessful);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompletedPreparation_DoesNotBecomeDeviceResult(bool succeeded)
    {
        var prep = new DelayNodeViewModel { RunOnlyOnFirstRun = true };
        var graph = Chain(prep);
        var dashboard = new StationDashboardViewModel();
        var context = new TestContext(new RegisterState());
        // A preceding passed device must not leave a success result on the preparation screen.
        dashboard.BeginRun(graph);
        dashboard.CompleteRun(ExecutionStatus.Completed, context, elapsed: TimeSpan.FromSeconds(15));
        Assert.True(dashboard.IsSuccessful);
        dashboard.BeginRun(graph, preparing: true);
        dashboard.NodeStarted(prep, context);
        dashboard.NodeCompleted(prep, succeeded ? StepResult.Next : StepResult.False, context);
        dashboard.NodeFinished(prep, context);

        dashboard.CompletePreparation(succeeded, needsInspection: false);

        Assert.False(dashboard.HasResult);
        Assert.False(dashboard.IsSuccessful);
        Assert.False(dashboard.HasDuration);
        Assert.Equal(!succeeded, dashboard.NeedsAttention);
        Assert.Equal(succeeded ? "Стенд настроен" : "Подготовка стенда не завершена", dashboard.Headline);
        dashboard.BeginRun(graph);
        dashboard.NodeStarted(prep, context);
        Assert.Equal("Проверка выполняется", dashboard.Headline);
    }

    private static GraphWorkspaceViewModel Chain(NodeViewModel operation)
    {
        var graph = new GraphWorkspaceViewModel();
        var start = new StartNodeViewModel();
        var end = new EndNodeViewModel();
        graph.Nodes.Add(start);
        graph.Nodes.Add(operation);
        graph.Nodes.Add(end);
        graph.Connections.Add(new(start.Output[0], operation.Input[0]));
        graph.Connections.Add(new(operation.Output[0], end.Input[0]));
        return graph;
    }
}
