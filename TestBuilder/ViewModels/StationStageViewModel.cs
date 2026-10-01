using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels;

public enum StationStageState { Pending, Running, Completed, Attention, Interrupted, Skipped }

public partial class StationStageViewModel : ViewModelBase
{
    private readonly Dictionary<NodeViewModel, int> _stepNumbers = new();
    public NodeViewModel Source { get; }
    public string Name { get; }
    public string Caption => Regex.Replace(Name, @"^\d+[a-zа-я]?[.)]\s*", string.Empty, RegexOptions.IgnoreCase);
    public int Number { get; set; }
    public int StepCount => _stepNumbers.Count;
    public bool HasSteps => StepCount > 0;
    [ObservableProperty] private string stepProgressText;
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
        if (source is ICompositeNodeViewModel composite)
        {
            foreach (var node in StationDashboardViewModel.OrderedNodes(composite.BodyGraph))
                if (!StationDashboardViewModel.IsBoundary(node))
                    _stepNumbers.Add(node, _stepNumbers.Count + 1);
        }
        stepProgressText = HasSteps ? $"Шагов: {StepCount}" : string.Empty;
    }

    internal void RefreshProgress(IEnumerable<NodeViewModel> activeNodes)
    {
        if (!IsActive) return;
        // Count the body's own steps. A nested subtest or loop stays one step while its body runs.
        var steps = activeNodes.Where(_stepNumbers.ContainsKey).Distinct()
            .OrderBy(node => _stepNumbers[node]).ToArray();
        // Keep the last step between notifications, on pause, and after completion.
        if (steps.Length == 0) return;
        StepProgressText = steps.Length == 1
            ? $"Шаг {_stepNumbers[steps[0]]} из {StepCount} — {StationDashboardViewModel.DisplayName(steps[0])}"
            : $"Шаги {string.Join(", ", steps.Select(node => _stepNumbers[node]))} из {StepCount} — " +
              string.Join(" · ", steps.Select(StationDashboardViewModel.DisplayName));
    }
}
