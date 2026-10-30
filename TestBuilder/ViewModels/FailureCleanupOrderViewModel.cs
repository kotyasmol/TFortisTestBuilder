using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    private bool CanMoveUp(FailureCleanupItemViewModel? item) => item != null && Items.IndexOf(item) > 0;
    private bool CanMoveDown(FailureCleanupItemViewModel? item) => item != null && Items.IndexOf(item) is var i && i >= 0 && i < Items.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp(FailureCleanupItemViewModel item) => Move(item, -1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown(FailureCleanupItemViewModel item) => Move(item, 1);

    private void Move(FailureCleanupItemViewModel item, int delta)
    {
        var from = Items.IndexOf(item);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Items.Count) return;
        Items.Move(from, to);
        Refresh();
    }

    private void Refresh()
    {
        for (var i = 0; i < Items.Count; i++) Items[i].Number = i + 1;
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    public SubtestNodeViewModel[] BuildResult() => Items.Select(i => i.Node).ToArray();
}

public partial class FailureCleanupItemViewModel(SubtestNodeViewModel node) : ObservableObject
{
    public SubtestNodeViewModel Node { get; } = node;
    public string Name => Node.DisplayTitle;
    public bool IsDisabled => !Node.IsEnabled;
    [ObservableProperty] private int number;
}
