using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Domain.Modbus;
using TestBuilder.Models;
using TestBuilder.Services;
using TestBuilder.Services.Graph;
using TestBuilder.Services.Logging;

namespace TestBuilder.ViewModels;

public partial class TestViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuickActionsToggleTip))]
    private bool isQuickActionsCollapsed = true;
    public string QuickActionsToggleTip => IsQuickActionsCollapsed ? "Показать кнопки профиля" : "Скрыть кнопки профиля";
    [RelayCommand]
    private void ToggleQuickActions() => IsQuickActionsCollapsed = !IsQuickActionsCollapsed;

    public bool IsActionGraphEditor { get; internal set; }
    public ObservableCollection<ProfileActionViewModel> QuickActions { get; } = new();
    internal IRawLabelPrinter? ActionLabelPrinter { get; set; }
    public bool HasRunningQuickActions => QuickActions.Any(a => a.IsRunning);
    public bool CanEditQuickActions => CanEditGraph && !IsActionGraphEditor && !IsTraining;
    public bool ShowQuickActionTools => _allowEditing && !IsOperatorView && !IsActionGraphEditor && !IsTraining;
    public bool ShowQuickActions => QuickActions.Count > 0 || ShowQuickActionTools;
    public bool ShowQuickActionHeader => QuickActions.Count > 0 || ShowQuickActionTools;
    private Window? ActionOwner => Avalonia.Application.Current?.ApplicationLifetime is
        Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life ? life.MainWindow : null;

    private void RefreshQuickActions()
    {
        foreach (var action in QuickActions) action.Cancel();
        QuickActions.Clear();
        foreach (var definition in RootGraph.QuickActions)
        {
            definition.Graph.Hardware = RootGraph.Hardware; definition.OffGraph.Hardware = RootGraph.Hardware;
            QuickActions.Add(new ProfileActionViewModel(definition, ExecuteProfileActionAsync,
                () => !IsActionGraphEditor && !IsTraining && !IsConnecting && !IsStopping && !IsNetworkSetupBusy && !IsLabelToolBusy && !NeedsStandInspection && _profileLoadError.Length == 0,
                RefreshStationAvailability));
        }
        RefreshStationAvailability();
    }
    public void CancelQuickActions() { foreach (var action in QuickActions) action.Cancel(); }
    private async Task DrainQuickActionsAsync()
    {
        var tasks = QuickActions.Where(a => a.IsRunning).Select(a => a.RunCommand.ExecutionTask).OfType<Task>().ToArray();
        CancelQuickActions();
        await Task.WhenAll(tasks);
    }

    [RelayCommand(CanExecute = nameof(CanEditQuickActions))]
    private async Task AddQuickAction()
    {
        if (!CanEditQuickActions || ActionOwner == null) return;
        var result = await new Views.ProfileActionEditorWindow(new ProfileActionDefinition(), RootGraph.Hardware).ShowDialog<ProfileActionDefinition?>(ActionOwner);
        if (result == null || !CanEditQuickActions) return;
        _history.Execute(RootGraph, new ProfileMetadataCommand(() => RootGraph.QuickActions.Add(result), () => RootGraph.QuickActions.Remove(result)));
    }
    [RelayCommand(CanExecute = nameof(CanEditQuickActions))]
    private async Task EditQuickAction(ProfileActionViewModel? action)
    {
        if (!CanEditQuickActions || action == null || ActionOwner == null) return;
        var index = RootGraph.QuickActions.IndexOf(action.Definition);
        if (index < 0) return;
        var result = await new Views.ProfileActionEditorWindow(action.Definition, RootGraph.Hardware).ShowDialog<ProfileActionDefinition?>(ActionOwner);
        if (result == null || !CanEditQuickActions) return;
        var previous = action.Definition;
        _history.Execute(RootGraph, new ProfileMetadataCommand(() => RootGraph.QuickActions[index] = result, () => RootGraph.QuickActions[index] = previous));
    }
    internal Func<string, Task<bool>> ConfirmDeleteQuickActionAsync { get; set; } = async name =>
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is { } owner)
            return await new Views.DeleteProfileActionDialog(name).ShowDialog<bool>(owner);
        return false;
    };

    [RelayCommand(CanExecute = nameof(CanEditQuickActions))]
    private async Task DeleteQuickAction(ProfileActionViewModel? action)
    {
        if (!CanEditQuickActions || action == null) return;
        var graph = RootGraph;
        var definition = action.Definition;
        if (!graph.QuickActions.Contains(definition)) return;
        if (!await ConfirmDeleteQuickActionAsync(definition.Name) || !CanEditQuickActions || !ReferenceEquals(graph, RootGraph)) return;
        var index = graph.QuickActions.IndexOf(definition);
        if (index < 0) return;
        _history.Execute(graph, new ProfileMetadataCommand(() => graph.QuickActions.Remove(definition), () => graph.QuickActions.Insert(index, definition)));
    }

    internal async Task<ProfileActionResult> ExecuteProfileActionAsync(ProfileActionDefinition action, bool turnOff,
        IExecutionObserver observer, CancellationToken token)
    {
        var logger = new ProfileActionLogger(TestingLogger, action.Name);
        try
        {
            token.ThrowIfCancellationRequested();
            action.Validate();
            var graph = turnOff ? action.OffGraph : action.Graph;
            if (GraphConnectionRequirements.RequiresStandConnection(graph) && !IsConnected)
                throw new InvalidOperationException("Подключитесь к стенду для выполнения этой кнопки.");
            logger.Info(turnOff ? "Выключение начато." : "Действие начато.");
            using var compiler = new GraphCompiler(_modbusService, logger) { ProfileHardware = RootGraph.Hardware, ConnectedStandPort = IsConnected ? SelectedPort : null, LabelPrinter = ActionLabelPrinter };
            GraphCompiler.ValidateRunConnections(graph, RootGraph.Hardware, IsConnected ? SelectedPort : null);
            var compiled = compiler.Compile(graph);
            var context = new TestContext(_registerState) {
                CancellationToken = token, IsConnected = IsConnected, ProfileName = action.Name, DeviceModel = RootGraph.DeviceModel,
                LastTestLabel = _lastCompletedTest?.Label?.Copy(),
                RegisterMonitor = _registerMonitor, RequireFreshMonitoring = RootGraph.Hardware != null,
                MaxRegisterAgeMs = Math.Max(10000, (RootGraph.Hardware?.PollIntervalMs ?? 1000) * 3),
                ModbusRegisters = new ModbusRegisterCatalog(_slaveManager.GetSlavesSnapshot()),
                ExecutionObserver = observer, SelfTestPageState = new SelfTestPageState(),
                OperatorPrompt = async message => await PromptActionAsync(message, false, token) != null,
                OperatorInput = (message, cancellation) => PromptActionAsync(message, true, cancellation)
            };
            var result = await new TestExecutor().ExecuteAsync(compiled.StartNode, context, token);
            var failedCheck = context.ReportEntries.FirstOrDefault(entry => !entry.IsSuccess);
            var success = result == ExecutionStatus.Completed && !context.HasCriticalError && !context.HasExecutionException && failedCheck == null;
            var message = success ? "Выполнено" : (context.CriticalFailure ?? context.Failure)?.Summary ??
                (failedCheck != null ? $"{failedCheck.Name}: {failedCheck.Value}" : "Граф завершился с ошибкой.");
            if (success) logger.Info(message); else logger.Error(message);
            return new(success, message);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { logger.Warning("Действие остановлено."); throw; }
        catch (Exception e) { logger.Error(e.Message); throw; }
    }
    private async Task<string?> PromptActionAsync(string message, bool input, CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () => {
            if (ActionOwner == null) throw new InvalidOperationException("Нет окна для подтверждения действия.");
            var dialog = new Views.OperatorActionDialog(message, requestInput: input);
            using var registration = token.Register(() => Dispatcher.UIThread.Post(() => dialog.Close()));
            await dialog.ShowDialog(ActionOwner); token.ThrowIfCancellationRequested();
            return dialog.Confirmed ? (input ? dialog.InputText : "OK") : null;
        });
    }
}
