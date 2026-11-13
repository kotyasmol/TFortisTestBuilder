using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Services.Graph;

/// <summary>Derived colors never rewrite legacy JSON colors or graph connections.</summary>
internal sealed class ProfileNodePresentation : IDisposable
{
    readonly GraphWorkspaceViewModel root;
    readonly Action changed;
    readonly Action<NodeViewModel> confirmReview;
    readonly HashSet<GraphWorkspaceViewModel> graphs = new();
    readonly HashSet<NodeViewModel> nodes = new();
    int deferred;
    bool pending;
    public int ReviewCount { get; private set; }
    public string Details { get; private set; } = "";

    public ProfileNodePresentation(GraphWorkspaceViewModel root, Action changed, Action<NodeViewModel> confirmReview)
    {
        this.root = root; this.changed = changed; this.confirmReview = confirmReview;
        root.QuickActions.CollectionChanged += CollectionChanged;
        Refresh();
    }

    public IDisposable Defer()
    {
        deferred++;
        return new Resume(() => { if (--deferred == 0 && pending) Refresh(); });
    }

    void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();
    void NodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NodeViewModel.ModelReview) or nameof(SubtestNodeViewModel.RunOnFailure)
            or nameof(NodeViewModel.CustomName) or nameof(NodeViewModel.Title) or nameof(SubtestNodeViewModel.Name)) Refresh();
    }

    public void Refresh()
    {
        if (deferred > 0) { pending = true; return; }
        pending = false;
        var activeGraphs = new HashSet<GraphWorkspaceViewModel>();
        var activeNodes = new HashSet<NodeViewModel>();
        var details = new List<string>();
        int Visit(GraphWorkspaceViewModel graph, bool failure, string path)
        {
            if (!activeGraphs.Add(graph)) return 0;
            if (graphs.Add(graph))
            {
                graph.Nodes.CollectionChanged += CollectionChanged;
                graph.Connections.CollectionChanged += CollectionChanged;
            }
            var normal = Reach(graph, graph.Nodes.Where(n => n is StartNodeViewModel or BodyStartNodeViewModel), false);
            var errorStarts = graph.Connections.Where(c => ErrorOutput(c.Source.Title)).Select(c => c.Target.Parent!).ToArray();
            var errors = Reach(graph, errorStarts, true);
            errors.ExceptWith(normal); // A shared join/end belongs to normal execution.
            int count = 0;
            foreach (var node in graph.Nodes)
            {
                activeNodes.Add(node);
                if (nodes.Add(node)) { node.PropertyChanged += NodeChanged; node.ConfirmReviewRequested = confirmReview; }
                var isFailure = failure || errors.Contains(node) || node is SubtestNodeViewModel { RunOnFailure: true };
                var nodePath = path + " / " + (node is SubtestNodeViewModel sub ? sub.Name : node.DisplayTitle);
                var children = node is ICompositeNodeViewModel composite ? Visit(composite.BodyGraph, isFailure, nodePath) : 0;
                node.SetPresentation(isFailure, children);
                if (node.ModelReview is { } review)
                {
                    details.Add(nodePath + "\n" + review.Message);
                    count++;
                }
                count += children;
            }
            return count;
        }
        var total = Visit(root, false, "Профиль");
        foreach (var action in root.QuickActions)
        {
            total += Visit(action.Graph, false, "Кнопка «" + action.Name + "»");
            total += Visit(action.OffGraph, false, "Кнопка «" + action.Name + "» / выключение");
        }
        foreach (var graph in graphs.Where(g => !activeGraphs.Contains(g)).ToArray())
        {
            graph.Nodes.CollectionChanged -= CollectionChanged;
            graph.Connections.CollectionChanged -= CollectionChanged;
            graphs.Remove(graph);
        }
        foreach (var node in nodes.Where(n => !activeNodes.Contains(n)).ToArray())
        {
            node.PropertyChanged -= NodeChanged;
            node.ConfirmReviewRequested = null;
            nodes.Remove(node);
        }
        ReviewCount = total;
        Details = string.Join("\n\n", details);
        changed();
    }

    static bool ErrorOutput(string title) => title is "False" or "Error" or "Отмена";
    static HashSet<NodeViewModel> Reach(GraphWorkspaceViewModel graph, IEnumerable<NodeViewModel> start, bool allOutputs)
    {
        var reached = new HashSet<NodeViewModel>();
        var queue = new Queue<NodeViewModel>(start.Where(n => n != null));
        while (queue.TryDequeue(out var node))
        {
            if (!reached.Add(node)) continue;
            foreach (var edge in graph.Connections.Where(c => ReferenceEquals(c.Source.Parent, node) && (allOutputs || !ErrorOutput(c.Source.Title))))
                if (edge.Target.Parent is { } target) queue.Enqueue(target);
        }
        return reached;
    }

    public void Dispose()
    {
        root.QuickActions.CollectionChanged -= CollectionChanged;
        foreach (var graph in graphs)
        {
            graph.Nodes.CollectionChanged -= CollectionChanged;
            graph.Connections.CollectionChanged -= CollectionChanged;
        }
        foreach (var node in nodes) { node.PropertyChanged -= NodeChanged; node.ConfirmReviewRequested = null; }
        graphs.Clear(); nodes.Clear();
    }
    sealed class Resume(Action resume) : IDisposable { public void Dispose() => resume(); }
}
