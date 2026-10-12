using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels;

public partial class NodePaletteCategoryViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleIcon))]
    private bool isExpanded = true;

    public NodePaletteCategoryViewModel(string title, params NodeViewModel[] nodes)
    {
        Title = title;

        foreach (var node in nodes)
            Nodes.Add(node);
    }

    public string Title { get; }
    public string ToggleIcon => IsExpanded ? "▾" : "▸";

    public ObservableCollection<NodeViewModel> Nodes { get; } = new();

    [RelayCommand]
    private void Toggle()
    {
        IsExpanded = !IsExpanded;
    }
}
