using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.ViewModels;

public partial class FailureCleanupOrderViewModel : ObservableObject
{
    public ObservableCollection<FailureCleanupItemViewModel> Items { get; }
    public bool HasItems => Items.Count > 0;

    public FailureCleanupOrderViewModel(IEnumerable<SubtestNodeViewModel> nodes)
    {
        Items = new(nodes.Select(n => new FailureCleanupItemViewModel(n)));
        Refresh();
    }

    public void MoveTo(FailureCleanupItemViewModel item, int index)
    {
        var from = Items.IndexOf(item);
        if (from < 0 || index < 0 || index >= Items.Count || from == index) return;
        Items.Move(from, index);
        Refresh();
    }

    private void Refresh()
    {
        for (var i = 0; i < Items.Count; i++) Items[i].Number = i + 1;
    }

    public SubtestNodeViewModel[] BuildResult() => Items.Select(i => i.Node).ToArray();
}

public partial class FailureCleanupItemViewModel(SubtestNodeViewModel node) : ObservableObject
{
    public SubtestNodeViewModel Node { get; } = node;
    public string Name => Node.DisplayTitle;
    public bool IsDisabled => !Node.IsEnabled;
    [ObservableProperty] private int number;
    [ObservableProperty] private bool isDragging;
    [ObservableProperty] private bool dropBefore;
    [ObservableProperty] private bool dropAfter;
}
