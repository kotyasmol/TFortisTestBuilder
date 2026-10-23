using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Services;
using TestBuilder.Services.Graph;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.ViewModels;

public partial class TestViewModel
{
    private GraphWorkspaceViewModel? _runWorkspace;
    private NodeViewModel? _runEntry;
    private bool _runSingleNode;
    [ObservableProperty] private decimal? runSlaveId;

    public bool HasPartialRun => _runEntry != null;
    public string PartialRunCaption => _runEntry == null ? string.Empty :
        $"{(_runSingleNode ? "Только нода" : "Старт с ноды")}: {_runEntry.DisplayTitle} · {_runWorkspace!.Title}";
    public string PartialRunHint => "Однократный запуск в пределах выбранного графа. Подтест / цикл выполняется целиком. Контекст новый; при ошибке выполняются аварийные подтесты.";

    public bool CanConfigureNodeRun(NodeViewModel? node) =>
        CanEditGraph && !IsConnecting && node != null && CurrentGraph.Nodes.Contains(node) && node is not LabelNodeViewModel;

    public void ConfigureNodeRun(NodeViewModel node, bool singleNode)
    {
        if (!CanConfigureNodeRun(node)) return;
        ResetPartialRun();
        _runEntry = node;
        _runWorkspace = CurrentGraph;
        _runSingleNode = singleNode;
        node.RunStartLabel = singleNode ? "ТОЛЬКО ЭТА НОДА" : "СТАРТ ЗАПУСКА";
        NotifyPartialRun();
    }

    [RelayCommand(CanExecute = nameof(CanEditGraph))]
    private void ClearPartialRun() => ResetPartialRun();

    private void ResetPartialRun()
    {
        if (_runEntry != null) _runEntry.RunStartLabel = string.Empty;
        _runEntry = null;
        _runWorkspace = null;
        RunSlaveId = null;
        NotifyPartialRun();
    }

    private void NotifyPartialRun()
    {
        OnPropertyChanged(nameof(HasPartialRun));
        OnPropertyChanged(nameof(PartialRunCaption));
    }

    private bool IsRunEntryAttached()
    {
        var visited = new HashSet<GraphWorkspaceViewModel>();
        bool Find(GraphWorkspaceViewModel graph) => visited.Add(graph) &&
            (ReferenceEquals(graph, _runWorkspace) && graph.Nodes.Contains(_runEntry!) ||
             graph.Nodes.OfType<ICompositeNodeViewModel>().Any(n => Find(n.BodyGraph)));
        return _runEntry == null || Find(RootGraph);
    }

    private bool PartialRunRequiresStand() =>
        GraphCompiler.GetRunNodes(_runWorkspace!, _runEntry!, _runSingleNode)
            .Any(GraphConnectionRequirements.RequiresStandConnection) ||
        RootGraph.Nodes.OfType<SubtestNodeViewModel>().Where(n => n.RunOnFailure && n.IsEnabled)
            .Any(GraphConnectionRequirements.RequiresStandConnection);
}
