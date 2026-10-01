using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.ViewModels;

public enum StationStageState { Pending, Running, Completed, Attention, Interrupted, Skipped }

public partial class StationStageViewModel : ViewModelBase
{
    public NodeViewModel Source { get; }
    public string Name { get; }
    public string Caption => Regex.Replace(Name, @"^\d+[a-zа-я]?[.)]\s*", string.Empty, RegexOptions.IgnoreCase);
    public int Number { get; set; }
    public IReadOnlyList<string> Details { get; }
    public bool HasDetails => Details.Count > 0;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveOperations))]
    private string activeOperationsText = string.Empty;
    public bool HasActiveOperations => !string.IsNullOrWhiteSpace(ActiveOperationsText);
    [ObservableProperty] private bool isExpanded;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ShowStatus))]
    private bool isPaused;
    public bool IsCleanup { get; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(NeedsAttention))]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(IsSkipped))]
    [NotifyPropertyChangedFor(nameof(ShowStatus))]
    private StationStageState state;
    partial void OnStateChanged(StationStageState value)
    {
        if (value != StationStageState.Running) IsPaused = false;
    }
    public bool IsActive => State == StationStageState.Running;
    public bool IsCompleted => State == StationStageState.Completed;
    public bool IsSkipped => State == StationStageState.Skipped;
    public bool ShowStatus => IsPaused || State is not (StationStageState.Pending or StationStageState.Completed);
    public bool NeedsAttention => State is StationStageState.Attention or StationStageState.Interrupted;
    public string StatusText => IsPaused ? "На паузе" : State switch
    {
        StationStageState.Running => "Выполняется",
        StationStageState.Completed => "Завершён",
        StationStageState.Attention => "Требует внимания",
        StationStageState.Interrupted => "Прерван",
        StationStageState.Skipped => "Не выполнялся",
        _ => "Ожидает"
    };
    public StationStageViewModel(NodeViewModel source, string name, bool isCleanup = false)
    {
        Source = source;
        Name = name;
        IsCleanup = isCleanup;
        var details = new List<string>();
        if (source is ICompositeNodeViewModel composite)
        {
            foreach (var node in StationDashboardViewModel.OrderedNodes(composite.BodyGraph))
                if (!StationDashboardViewModel.IsBoundary(node))
                    details.Add(StationDashboardViewModel.DisplayName(node));
        }
        Details = details;
    }

    [RelayCommand]
    private void ToggleDetails() => IsExpanded = !IsExpanded;
}
