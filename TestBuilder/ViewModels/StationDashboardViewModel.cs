using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Domain.Execution;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.ViewModels;

/// <summary>Read-only presentation of the same graph execution used by the editor.</summary>
public partial class StationDashboardViewModel : ViewModelBase
{
    private readonly Dictionary<NodeViewModel, StationStageViewModel> _stageByNode = new();
    private readonly HashSet<(Guid Scope, NodeViewModel Node)> _activeNodes = new();
    private TaskCompletionSource<bool>? _prompt;
    private bool _stopping;
    private bool _cleanup;
    public ObservableCollection<StationStageViewModel> Stages { get; } = new();
    [ObservableProperty] private string headline = "Пульт стенда";
    [ObservableProperty] private string guidance = "Подготовьте изделие и подключите стенд.";
    [ObservableProperty] private string currentStage = "Готов к работе";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSerialNumber))]
    private string serialNumber = "Будет получен во время проверки";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReportStatus))]
    private string reportStatus = "Отчёт ещё не сформирован";
    [ObservableProperty] private string operatorMessage = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmActionCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeclineActionCommand))]
    private bool isAwaitingAction;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmActionCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeclineActionCommand))]
    private bool isPaused;
    public bool CanRespond => IsAwaitingAction && !IsPaused;
    public bool HasSerialNumber => long.TryParse(SerialNumber, out var number) && number > 0;
    public bool HasReportStatus => ReportStatus is not ("Отчёт не предусмотрен программой" or "Отчёт ещё не сформирован");
    [ObservableProperty] private bool hasResult;
    [ObservableProperty] private bool isSuccessful;
    [ObservableProperty] private bool needsAttention;

    public void Prepare(GraphWorkspaceViewModel graph)
    {
        _stageByNode.Clear();
        _activeNodes.Clear();
        Stages.Clear();
        HasResult = IsSuccessful = NeedsAttention = false;
        SerialNumber = "Будет получен во время проверки";
        ReportStatus = "Отчёт ещё не сформирован";
        Headline = "Можно начинать подготовку";
        Guidance = "Проверьте подключение изделия. Затем нажмите «Начать проверку».";
        CurrentStage = "Готов к работе";
        foreach (var node in OrderedNodes(graph))
            if (!IsBoundary(node)) AddStage(node);
        // Unconnected failure cleanup becomes visible only if it actually runs.
        foreach (var cleanup in graph.Nodes.OfType<SubtestNodeViewModel>().Where(n => n.RunOnFailure && n.IsEnabled))
            if (!_stageByNode.ContainsKey(cleanup)) AddStage(cleanup, true);
        if (!_stageByNode.Keys.Any(n => n is GetSerialNumberFromServerNodeViewModel or PrintLabelNodeViewModel))
            SerialNumber = "Не используется";
        if (!_stageByNode.Keys.Any(n => n is BuildTestReportNodeViewModel or SendTestReportNodeViewModel))
            ReportStatus = "Отчёт не предусмотрен программой";
    }

    internal static IReadOnlyList<NodeViewModel> OrderedNodes(GraphWorkspaceViewModel graph)
    {
        var reachable = new List<NodeViewModel>();
        var visited = new HashSet<NodeViewModel>();
        void Visit(NodeViewModel node)
        {
            if (!visited.Add(node)) return;
            reachable.Add(node);
            foreach (var output in node.Output)
                foreach (var connection in graph.Connections.Where(c => c.Source == output))
                    if (connection.Target.Parent is { } next) Visit(next);
        }
        var start = graph.Nodes.FirstOrDefault(n => n is StartNodeViewModel or BodyStartNodeViewModel);
        if (start != null) Visit(start);

        // Keep connector order as the tie-breaker, but place a shared successor after both branches.
        var result = new List<NodeViewModel>();
        var remaining = new HashSet<NodeViewModel>(reachable);
        while (remaining.Count > 0)
        {
            var next = reachable.FirstOrDefault(node => remaining.Contains(node) &&
                !graph.Connections.Any(c => c.Target.Parent == node && c.Source.Parent != null &&
                    remaining.Contains(c.Source.Parent))) ?? reachable.First(remaining.Contains);
            result.Add(next);
            remaining.Remove(next);
        }
        return result;
    }

    private void AddStage(NodeViewModel node, bool cleanup = false)
    {
        var stage = new StationStageViewModel(node, DisplayName(node), cleanup);
        if (!cleanup)
        {
            stage.Number = Stages.Count + 1;
            Stages.Add(stage);
        }
        Map(node, stage);
    }

    private void Map(NodeViewModel node, StationStageViewModel stage)
    {
        _stageByNode[node] = stage;
        if (node is ICompositeNodeViewModel composite)
            foreach (var child in composite.BodyGraph.Nodes) Map(child, stage);
    }

    public void BeginRun(GraphWorkspaceViewModel graph)
    {
        Prepare(graph);
        _stopping = _cleanup = false;
        IsPaused = false;
        Headline = "Проверка выполняется";
        Guidance = "Дождитесь результата. Если потребуется ваше действие, инструкция появится здесь.";
    }

    public void NodeStarted(NodeViewModel node, TestContext context)
    {
        UpdateContext(context);
        if (!_stageByNode.TryGetValue(node, out var stage)) return;
        _activeNodes.Add((context.ExecutionScopeId, node));
        if (!Stages.Contains(stage))
        {
            stage.Number = Stages.Count + 1;
            Stages.Add(stage);
        }
        if (stage.State != StationStageState.Running)
            stage.State = StationStageState.Running;
        RefreshActiveStages();
        if (!_stopping && !_cleanup && !IsAwaitingAction) Headline = "Проверка выполняется";
    }

    public void NodeCompleted(NodeViewModel node, StepResult result, TestContext context)
    {
        UpdateContext(context);
        if (_stageByNode.TryGetValue(node, out var stage) && ReferenceEquals(stage.Source, node))
            stage.State = node is SubtestNodeViewModel { IsEnabled: false }
                ? StationStageState.Skipped
                : result == StepResult.False ? StationStageState.Attention : StationStageState.Completed;
        RefreshActiveStages();
    }

    public void NodeFailed(NodeViewModel node, TestContext context)
    {
        if (_stageByNode.TryGetValue(node, out var stage) && ReferenceEquals(stage.Source, node))
            stage.State = StationStageState.Attention;
        RefreshActiveStages();
    }

    public void NodeFinished(NodeViewModel node, TestContext context)
    {
        _activeNodes.Remove((context.ExecutionScopeId, node));
        // A finally notification without NodeCompleted is an interruption, not a passing check.
        if (_stageByNode.TryGetValue(node, out var stage) && ReferenceEquals(stage.Source, node) &&
            stage.State == StationStageState.Running)
            stage.State = StationStageState.Interrupted;
        RefreshActiveStages();
    }

    public void FinishScope(TestContext context)
    {
        foreach (var item in _activeNodes.Where(item => item.Scope == context.ExecutionScopeId).ToArray())
            NodeFinished(item.Node, context);
    }

    private void RefreshActiveStages()
    {
        foreach (var stage in Stages)
            stage.RefreshProgress(_activeNodes.Select(item => item.Node));
        var active = Stages.Where(stage => stage.IsActive).Select(stage => stage.Name).ToArray();
        CurrentStage = active.Length == 0 ? "Ожидание следующего этапа" :
            active.Length == 1 ? active[0] : "Одновременно: " + string.Join(" · ", active);
    }

    public void BeginCleanup(SubtestNodeViewModel node)
    {
        _cleanup = true;
        foreach (var stage in Stages.Where(s => s.State == StationStageState.Running))
            stage.State = StationStageState.Interrupted;
        Headline = "Завершаем работу стенда";
        Guidance = "Дождитесь завершения действий по остановке.";
        if (_stageByNode.TryGetValue(node, out var cleanup))
        {
            if (!Stages.Contains(cleanup))
            {
                cleanup.Number = Stages.Count + 1;
                Stages.Add(cleanup);
            }
            cleanup.State = StationStageState.Running;
        }
        RefreshActiveStages();
    }

    public void FinishCleanup(SubtestNodeViewModel node, bool success)
    {
        if (_stageByNode.TryGetValue(node, out var stage))
            stage.State = success ? StationStageState.Completed : StationStageState.Attention;
        RefreshActiveStages();
    }

    public void RequestStop()
    {
        _stopping = true;
        Headline = "Останавливаем проверку";
        Guidance = "Дождитесь завершения действий по остановке.";
    }

    public void SetPaused(bool paused)
    {
        foreach (var stage in Stages) stage.IsPaused = paused && stage.IsActive;
    }

    public void CompleteRun(ExecutionStatus status, TestContext? context)
    {
        if (context != null) UpdateContext(context);
        foreach (var stage in Stages)
        {
            if (stage.State == StationStageState.Pending) stage.State = StationStageState.Skipped;
            else if (stage.State == StationStageState.Running) stage.State = StationStageState.Interrupted;
        }
        HasResult = true;
        _activeNodes.Clear();
        RefreshActiveStages();
        var cleanupFailed = Stages.Any(s => s.IsCleanup && s.State == StationStageState.Attention);
        var checksPassed = context != null && !context.HasCriticalError &&
            context.ReportEntries.All(e => e.IsSuccess) &&
            (!context.Variables.ContainsKey("BuildReport.DevicePassed") || context.GetVariable<bool>("BuildReport.DevicePassed"));
        var delivery = context?.GetVariable<string>("ReportDelivery.Status");
        var reportPending = delivery != null && delivery != "Sent";
        IsSuccessful = status == ExecutionStatus.Completed && checksPassed && !reportPending && !cleanupFailed;
        NeedsAttention = !IsSuccessful;
        if (cleanupFailed)
        {
            Headline = "Проверьте состояние стенда";
            Guidance = "Не все завершающие действия выполнены. Обратитесь к инженеру перед следующим запуском.";
        }
        else if (status == ExecutionStatus.Cancelled)
        {
            Headline = "Проверка остановлена";
            Guidance = "Результат проверки не получен. Подготовьте изделие перед новым запуском.";
        }
        else if (status != ExecutionStatus.Completed)
        {
            Headline = "Проверка не завершена";
            Guidance = "Обратитесь к инженеру. Не считайте изделие прошедшим проверку.";
        }
        else if (!checksPassed)
        {
            Headline = "Проверки не пройдены";
            Guidance = "Передайте изделие инженеру вместе с результатом проверки.";
        }
        else if (reportPending)
        {
            Headline = "Проверки пройдены · отчёт не отправлен";
            Guidance = "Обратитесь к инженеру для отправки отчёта. Повторная проверка изделия не требуется.";
        }
        else
        {
            Headline = "Программа выполнена";
            Guidance = "Выполните завершающие инструкции. Затем можно подготовить следующее изделие.";
            if (context?.GetVariable<bool>("BuildReport.DevicePassed") == true) Headline = "Проверки пройдены";
        }
    }

    public void UpdateContext(TestContext context)
    {
        if (context.Variables.TryGetValue("SerialNumber", out var serial))
            SerialNumber = Convert.ToString(serial, CultureInfo.InvariantCulture) ?? string.Empty;
        ReportStatus = context.GetVariable<string>("ReportDelivery.Status") switch
        {
            "Sent" => "Отчёт принят сервером",
            "Failed" => "Отчёт не отправлен — обратитесь к инженеру",
            "Sending" => "Отправляем отчёт",
            "Pending" => "Отчёт подготовлен",
            _ => ReportStatus
        };
    }

    // Called on the UI thread; the caller marshals completion back to that thread too.
    public async Task<bool> RequestActionAsync(string message, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _prompt = completion;
        OperatorMessage = message;
        IsAwaitingAction = true;
        Headline = "Требуется ваше действие";
        try { return await completion.Task.WaitAsync(token); }
        finally
        {
            if (ReferenceEquals(_prompt, completion))
            {
                _prompt = null;
                IsAwaitingAction = false;
                OperatorMessage = string.Empty;
                Headline = _cleanup ? "Завершаем работу стенда" :
                    _stopping ? "Останавливаем проверку" : "Проверка выполняется";
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanRespond))]
    private void ConfirmAction() { if (CanRespond) _prompt?.TrySetResult(true); }
    [RelayCommand(CanExecute = nameof(CanRespond))]
    private void DeclineAction() { if (CanRespond) _prompt?.TrySetResult(false); }

    internal static bool IsBoundary(NodeViewModel node) => node is StartNodeViewModel or EndNodeViewModel or
        BodyStartNodeViewModel or BodyEndNodeViewModel or LabelNodeViewModel;

    internal static string DisplayName(NodeViewModel node) => node switch
    {
        SubtestNodeViewModel subtest => subtest.Name,
        OperatorActionNodeViewModel => "Действие оператора",
        GetSerialNumberFromServerNodeViewModel => "Получение серийного номера",
        BuildTestReportNodeViewModel => "Подготовка отчёта",
        SendTestReportNodeViewModel => "Отправка отчёта",
        PrintLabelNodeViewModel => "Печать этикеток",
        RunDataTestNodeViewModel => "Проверка сетевых портов",
        SelfTestCheckNodeViewModel => "Встроенная проверка устройства",
        _ => node.Title
    };
}
