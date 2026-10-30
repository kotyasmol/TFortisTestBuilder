using System;
using System.Collections.Generic;
using System.Diagnostics;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Domain.Execution;
using TestBuilder.Models;

namespace TestBuilder.ViewModels;

public sealed record ProfileActionResult(bool Success, string Message);

public partial class ProfileActionViewModel : ObservableObject, IExecutionObserver
{
    public ProfileActionDefinition Definition { get; }
    private readonly Func<ProfileActionDefinition, bool, IExecutionObserver, CancellationToken, Task<ProfileActionResult>> _run;
    private readonly Func<bool> _canRun;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private CancellationTokenSource? _cancellation;
    private readonly Action _availabilityChanged;
    private readonly Action? _refreshFeedback;
    private readonly ProfileActionLabels _labels;
    private bool _hasOutcome;
    private long _feedbackNotBefore;
    private string _feedbackReason = "Нет данных";
    public bool HasStateBinding => Definition.StateBinding != null;
    public string FeedbackText => IsStateKnown ? (IsOn ? _labels.On : _labels.Off) : _labels.Unknown;
    public bool ShowStatusText => _labels.ShowStatus;
    public string Name => Definition.Name;
    public bool IsToggle => Definition.Display == ProfileActionDisplay.Toggle;
    public bool ShowProgress => !IsToggle && (Definition.Display == ProfileActionDisplay.Progress || IsRunning);
    public bool UsesSystemColor => ProfileActionDefinition.IsSystemColor(Definition.Color);
    public IBrush? Background => UsesSystemColor ? null : Brush.Parse(Definition.Color);
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool isOn;
    [ObservableProperty] private bool isStateKnown;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string errorMessage = "";
    public string DetailText => string.IsNullOrEmpty(ErrorMessage) ?
        HasStateBinding && !IsStateKnown ? _feedbackReason : DisplayStatus : ErrorMessage;
    [ObservableProperty] private string currentStep = "";
    [ObservableProperty] private int completedSteps;
    public int? ExpectedSteps { get; private set; }
    public double? Progress => ExpectedSteps is > 0 ? Math.Min(1, (double)CompletedSteps / ExpectedSteps.Value) : null;
    public string StateText => ShowStatusText ? DisplayStatus : "";
    private string DisplayStatus => IsRunning ? Regex.Replace(_labels.Running, @"\{(step|done|total)\}", match => match.Groups[1].Value switch {
        "step" => CurrentStep, "done" => CompletedSteps.ToString(), "total" => ExpectedSteps?.ToString() ?? "?", _ => match.Value
    }) : HasStateBinding ? string.Join(" · ", new[] { FeedbackText, Status }.Where(s => !string.IsNullOrEmpty(s))) :
        _hasOutcome ? Status : IsToggle ? FeedbackText : _labels.Ready;
    public double ToggleOffset => IsStateKnown && IsOn ? 14 : 0;
    public double ToggleOpacity => IsStateKnown ? 1 : .4;
    public bool CanRun => !IsRunning && _canRun() && (!IsToggle || !HasStateBinding || IsStateKnown);
    public IAsyncRelayCommand RunCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public event Action? StateChanged;

    public ProfileActionViewModel(ProfileActionDefinition definition,
        Func<ProfileActionDefinition, bool, IExecutionObserver, CancellationToken, Task<ProfileActionResult>> run,
        Func<bool> canRun, Action availabilityChanged, Action? refreshFeedback = null)
    {
        Definition = definition; _labels = definition.GetLabels(); _refreshFeedback = refreshFeedback; _run = run; _canRun = canRun; _availabilityChanged = availabilityChanged;
        RunCommand = new AsyncRelayCommand(RunAsync, () => CanRun);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
    }
    public void RefreshAvailability() => RunCommand.NotifyCanExecuteChanged();
    public void Cancel() => _cancellation?.Cancel();
    private Task Ui(Action update)
    {
        if (_ui == null || SynchronizationContext.Current == _ui) { update(); return Task.CompletedTask; }
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Post(_ => { try { update(); done.SetResult(); } catch (Exception e) { done.SetException(e); } }, null);
        return done.Task;
    }
    private void NotifyState()
    {
        OnPropertyChanged(nameof(StateText)); OnPropertyChanged(nameof(ShowProgress));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(ToggleOffset)); OnPropertyChanged(nameof(ToggleOpacity)); OnPropertyChanged(nameof(Progress));
        StateChanged?.Invoke();
    }
    private async Task RunAsync()
    {
        _refreshFeedback?.Invoke();
        if (!CanRun) return;
        // Bound toggles follow feedback; unbound toggles retry an unsuccessful command in the same direction.
        var turnOff = IsToggle && IsOn;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        ExpectedSteps = CountLinearSteps(turnOff ? Definition.OffGraph : Definition.Graph);
        IsRunning = true; _hasOutcome = false; Status = ""; ErrorMessage = ""; CurrentStep = Name; CompletedSteps = 0;
        CancelCommand.NotifyCanExecuteChanged(); _availabilityChanged(); NotifyState();
        try
        {
            var result = await _run(Definition, turnOff, this, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (result.Success)
            {
                if (IsToggle && !HasStateBinding) { IsOn = !turnOff; IsStateKnown = true; }
                Status = HasStateBinding ? "" : IsToggle ? FeedbackText : _labels.Success;
            }
            else { if (!HasStateBinding) IsStateKnown = false; Status = _labels.Error; ErrorMessage = result.Message; }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { if (!HasStateBinding) IsStateKnown = false; Status = _labels.Cancelled; }
        catch (Exception e) { if (!HasStateBinding) IsStateKnown = false; Status = _labels.Error; ErrorMessage = e.Message; }
        finally
        {
            if (HasStateBinding)
            {
                // Completion is not feedback. Require a read started after the command graph finished.
                _feedbackNotBefore = Stopwatch.GetTimestamp();
                IsStateKnown = false; _feedbackReason = "Ожидание свежих данных";
                _refreshFeedback?.Invoke();
            }
            _hasOutcome = true;
            _cancellation = null; IsRunning = false; CancelCommand.NotifyCanExecuteChanged();
            _availabilityChanged(); RefreshAvailability(); NotifyState();
        }
    }
    // Percentages are exact only for a linear route. Loops, subtests and conditional
    // paths use the activity ring with the number of steps already completed.
    private static int? CountLinearSteps(Graphs.GraphWorkspaceViewModel graph)
    {
        var starts = graph.Nodes.OfType<StepVM.StartNodeViewModel>().ToArray();
        if (starts.Length != 1) return null;
        var node = starts[0];
        NodifyVM.NodeViewModel? current = node;
        var visited = new HashSet<NodifyVM.NodeViewModel>();
        while (current != null)
        {
            if (!visited.Add(current) || current is NodifyVM.ICompositeNodeViewModel || current.Output.Count > 1) return null;
            if (current is StepVM.EndNodeViewModel) return visited.Count;
            var next = graph.Connections.Where(c => ReferenceEquals(c.Source.Parent, current)).ToArray();
            if (next.Length != 1) return null;
            current = next[0].Target.Parent;
        }
        return null;
    }
    internal void RefreshFeedback(RegisterState state, HardwareConfiguration? hardware, bool connected, bool monitoring, long? now = null)
    {
        if (Definition.StateBinding is not { } binding) return;
        var result = ProfileActionStateEvaluator.Evaluate(binding, state, hardware, connected, monitoring, _feedbackNotBefore, now);
        var changed = IsStateKnown != result.IsOn.HasValue || IsOn != (result.IsOn ?? false) || _feedbackReason != result.Reason;
        IsStateKnown = result.IsOn.HasValue; IsOn = result.IsOn ?? false; _feedbackReason = result.Reason;
        if (changed) { RefreshAvailability(); NotifyState(); }
    }

    public void InvalidateState()
    {
        IsStateKnown = false; IsOn = false;
        _feedbackNotBefore = Stopwatch.GetTimestamp(); _feedbackReason = "Нет данных";
        RefreshAvailability();
        if (!IsRunning) { _hasOutcome = false; Status = ""; ErrorMessage = ""; }
        NotifyState();
    }
    public Task NodeStartedAsync(TestNode node, TestContext context, CancellationToken token) =>
        Ui(() => { CurrentStep = node.DisplayName; NotifyState(); });
    public Task NodeCompletedAsync(TestNode node, StepResult result, TestContext context, CancellationToken token) =>
        Ui(() => { CompletedSteps++; NotifyState(); });
    public Task NodeFinishedAsync(TestNode node, TestContext context, CancellationToken token) => Task.CompletedTask;
    public Task NodeFailedAsync(TestNode node, TestContext context, CancellationToken token) => Task.CompletedTask;
}
