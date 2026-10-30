using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Services.Graph;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.ViewModels;

public partial class TestViewModel
{
    [RelayCommand(CanExecute = nameof(CanEditQuickActions))]
    private async Task ConfigureFailureCleanup(Window? owner)
    {
        if (!CanEditQuickActions || owner == null) return;
        var graph = RootGraph;
        var draft = new FailureCleanupOrderViewModel(FailureCleanupOrder.GetNodes(graph, includeDisabled: true));
        var result = await new Views.FailureCleanupOrderWindow(draft).ShowDialog<SubtestNodeViewModel[]?>(owner);
        if (result == null || !CanEditQuickActions || !ReferenceEquals(graph, RootGraph)) return;
        try { ApplyFailureCleanupOrder(result); }
        catch (InvalidOperationException ex) { StatusMessage = ex.Message; }
    }

    internal void ApplyFailureCleanupOrder(IReadOnlyList<SubtestNodeViewModel> order)
    {
        if (!CanEditQuickActions) return;
        var graph = RootGraph;
        var current = FailureCleanupOrder.GetNodes(graph, includeDisabled: true);
        if (order.Count != current.Count || order.Distinct().Count() != order.Count || order.Any(n => !current.Contains(n)))
            throw new InvalidOperationException("Список аварийных подтестов изменился. Откройте настройку порядка заново.");
        if (order.Count == 0 || order.Select((n, i) => n.FailureOrder == i + 1).All(equal => equal)) return;

        var nodes = order.ToArray();
        var previous = nodes.Select(n => n.FailureOrder).ToArray();
        void Apply() { for (var i = 0; i < nodes.Length; i++) nodes[i].FailureOrder = i + 1; }
        void Undo() { for (var i = 0; i < nodes.Length; i++) nodes[i].FailureOrder = previous[i]; }
        _history.Execute(graph, new ProfileMetadataCommand(Apply, Undo));
    }
}
