using System;
using System.Collections.Generic;
using System.Linq;
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
    public string Name => Definition.Name;
    public bool IsToggle => Definition.Display == ProfileActionDisplay.Toggle;
    public bool ShowProgress => !IsToggle && (Definition.Display == ProfileActionDisplay.Progress || IsRunning);
    public bool UsesSystemColor => ProfileActionDefinition.IsSystemColor(Definition.Color);
    public IBrush? Background => UsesSystemColor ? null : Brush.Parse(Definition.Color);
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool isOn;
    [ObservableProperty] private bool isStateKnown;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string currentStep = "";
    [ObservableProperty] private int completedSteps;
    public int? ExpectedSteps { get; private set; }
    public double? Progress => ExpectedSteps is > 0 ? Math.Min(1, (double)CompletedSteps / ExpectedSteps.Value) : null;
    public string StateText => IsRunning ? $"{CurrentStep} · {CompletedSteps}{(ExpectedSteps.HasValue ? $"/{ExpectedSteps}" : " шагов")}" :
        !string.IsNullOrEmpty(Status) ? Status : IsToggle ? "Не задано" : "Готово";
    public double ToggleOffset => IsStateKnown && IsOn ? 14 : 0;
    public double ToggleOpacity => IsStateKnown ? 1 : .4;
    public bool CanRun => !IsRunning && _canRun();
    public IAsyncRelayCommand RunCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public event Action? StateChanged;

    public ProfileActionViewModel(ProfileActionDefinition definition,
        Func<ProfileActionDefinition, bool, IExecutionObserver, CancellationToken, Task<ProfileActionResult>> run,
        Func<bool> canRun, Action availabilityChanged)
    {
        Definition = definition; _run = run; _canRun = canRun; _availabilityChanged = availabilityChanged;
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
        OnPropertyChanged(nameof(ToggleOffset)); OnPropertyChanged(nameof(ToggleOpacity)); OnPropertyChanged(nameof(Progress));
        StateChanged?.Invoke();
    }
    private async Task RunAsync()
    {
        if (!CanRun) return;
        // Retry an unsuccessful command in the same direction. Only success flips IsOn.
        var turnOff = IsToggle && IsOn;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        ExpectedSteps = CountLinearSteps(turnOff ? Definition.OffGraph : Definition.Graph);
        IsRunning = true; Status = ""; CurrentStep = "Запуск"; CompletedSteps = 0;
        CancelCommand.NotifyCanExecuteChanged(); _availabilityChanged(); NotifyState();
        try
        {
            var result = await _run(Definition, turnOff, this, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (result.Success)
            {
                if (IsToggle) { IsOn = !turnOff; IsStateKnown = true; }
                Status = IsToggle ? (IsOn ? "Включено" : "Выключено") : "Выполнено";
            }
            else { IsStateKnown = false; Status = "Ошибка: " + result.Message; }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { IsStateKnown = false; Status = "Остановлено · состояние не подтверждено"; }
        catch (Exception e) { IsStateKnown = false; Status = "Ошибка: " + e.Message; }
        finally
        {
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
    public void InvalidateState()
    {
        IsStateKnown = false; IsOn = false;
        if (!IsRunning) Status = "";
        NotifyState();
    }
    public Task NodeStartedAsync(TestNode node, TestContext context, CancellationToken token) =>
        Ui(() => { CurrentStep = node.DisplayName; NotifyState(); });
    public Task NodeCompletedAsync(TestNode node, StepResult result, TestContext context, CancellationToken token) =>
        Ui(() => { CompletedSteps++; NotifyState(); });
    public Task NodeFinishedAsync(TestNode node, TestContext context, CancellationToken token) => Task.CompletedTask;
    public Task NodeFailedAsync(TestNode node, TestContext context, CancellationToken token) => Task.CompletedTask;
}
