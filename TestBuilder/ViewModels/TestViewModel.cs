using Avalonia;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.Views;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Services.Graph;
using TestBuilder.Services.Graph.Commands;

namespace TestBuilder.ViewModels;

public partial class TestViewModel : ViewModelBase, IGraphEditor, IExecutionObserver, IDisposable
{
    private sealed record ClipboardNode(NodeViewModel Node, Point Location);

    private sealed record ClipboardConnection(
        int SourceNodeIndex,
        int SourceConnectorIndex,
        int TargetNodeIndex,
        int TargetConnectorIndex);

    private readonly ModbusService _modbusService;
    private readonly SlaveManager _slaveManager;
    private readonly RegisterState _registerState = new();
    private readonly Stack<GraphWorkspaceViewModel> _graphStack = new();
    private readonly List<string> _graphPath = new();
    private readonly ExecutionUiState _executionUi = new();

    private string? _currentProfilePath;
    private string? _savedProfileName;
    private RegisterMonitor? _registerMonitor;
    private UndoRedoManager? _currentUndoRedo;
    private readonly ProfileEditHistory _history;
    private CancellationTokenSource? _testRunCts;
    private string? _runConnectionFailure;
    private Views.ModbusReconnectDialog? _modbusReconnectDialog;
    private TaskCompletionSource<bool> _pauseCompletion =
        CreateCompletedPauseCompletion();

    public event Action? CurrentGraphOpened;

    public ILogger TestingLogger { get; }

    public SelfTestPageState SelfTestPageState { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionButtonText))]
    private bool isConnected;

    [ObservableProperty]
    private bool isMonitoringActive;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? statusMessage;

    [ObservableProperty]
    private string? selectedPort;

    [ObservableProperty]
    private GraphWorkspaceViewModel currentGraph;

    [ObservableProperty]
    private bool canGoBackGraph;

    [ObservableProperty]
    private bool isPaletteCollapsed;

    [ObservableProperty]
    private bool isTestRunning;

    [ObservableProperty]
    private bool isTestPaused;

    public string PaletteToggleIcon => IsPaletteCollapsed ? "‹" : "›";
    public string PaletteToggleTip => IsPaletteCollapsed ? "Развернуть палитру" : "Свернуть палитру";
    public string CurrentGraphPath => string.Join(" / ", _graphPath.Select((title, index) =>
        index == 0 ? GraphProfile.ComposeName(DeviceName, ProfileDescription) : title));
    public string CurrentProfileName
    {
        get => RootGraph.Title;
        set
        {
            if (!CanEditGraph || value.Trim() == CurrentProfileName.Trim()) return;
            var old = RootGraph.Title;
            var oldDescription = RootGraph.ConfigurationName;
            _history.Execute(RootGraph, new ProfileMetadataCommand(
                () => SetProfileIdentity(value, DescriptionFromTitle(value, DeviceName)), () => SetProfileIdentity(old, oldDescription)));
        }
    }
    private void SetProfileTitle(string title)
    {
        RootGraph.Title = title;
        if (_graphPath.Count > 0) _graphPath[0] = title;
        OnPropertyChanged(nameof(CurrentProfileName));
        OnPropertyChanged(nameof(ProfileDescription));
        OnPropertyChanged(nameof(CurrentGraphPath));
    }
    private sealed class ProfileMetadataCommand(Action apply, Action undo) : IGraphCommand
    { public void Execute() => apply(); public void Undo() => undo(); }

    public ICommand TogglePaletteCommand { get; }

    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }

    public bool CanUndo => CurrentGraph.UndoRedo.CanUndo;
    public bool CanRedo => CurrentGraph.UndoRedo.CanRedo;

    private List<ClipboardNode>? _clipboardNodes;
    private List<ClipboardConnection>? _clipboardConnections;

    public bool CanPaste => CanEditGraph && _clipboardNodes?.Count > 0;
    public int CopiedNodeCount => _clipboardNodes?.Count ?? 0;

    public GraphWorkspaceViewModel RootGraph { get; } = new()
    {
        Title = "Новый профиль",
        IsBodyGraph = false
    };

    public string ConnectionButtonText => IsConnected ? "Отключиться" : "Подключиться";

    public IAsyncRelayCommand ToggleConnectionCommand { get; }

    public IAsyncRelayCommand RunGraphCommand { get; }

    public ICommand PauseTestCommand { get; }

    public ICommand ResumeTestCommand { get; }

    public ICommand StopTestCommand { get; }

    public ObservableCollection<NodeViewModel> Nodes => CurrentGraph.Nodes;

    public ObservableCollection<ConnectionViewModel> Connections => CurrentGraph.Connections;

    public ObservableCollection<NodeViewModel> SelectedNodes => CurrentGraph.SelectedNodes;

    public PendingConnectionViewModel PendingConnection { get; }

    public ICommand DisconnectConnectorCommand { get; }

    public ICommand DeleteSelectedNodesCommand { get; }

    public ICommand ClearGraphCommand { get; }

    public ICommand AddNodeCommand { get; }

    public ICommand GoBackGraphCommand { get; }

    public ICommand NewProfileCommand { get; }

    public IAsyncRelayCommand SaveGraphCommand { get; }

    public IAsyncRelayCommand LoadProfileCommand { get; }

    public IAsyncRelayCommand ImportProfilesCommand { get; }

    public ObservableCollection<NodeViewModel> AvailableNodes { get; } = new()
    {
        new SerialConsoleNodeViewModel(),
        new RunProgramNodeViewModel(),
        new ParseResponseNodeViewModel(),
        new CheckLedColorNodeViewModel(),
        new StartNodeViewModel(),
        new EndNodeViewModel(),
        new ModbusWriteNodeViewModel(),
        new CheckRegisterRangeNodeViewModel(),
        new DelayNodeViewModel(),
        new SelfTestCheckNodeViewModel(),
        new CheckVariableEqualityNodeViewModel(),
        new CheckVariableRangeNodeViewModel(),
        new ClearArpCacheNodeViewModel(),
        new GetSerialNumberFromServerNodeViewModel(),
        new SetProMacNodeViewModel(),
        new SetPswMacNodeViewModel(),
        new UpdatePswFirmwareNodeViewModel(),
        new RunDataTestNodeViewModel(),
        new ReadHttpVariableNodeViewModel(),
        new WaitVariableUntilNodeViewModel(),
        new BuildMacFromSerialNodeViewModel(),
        new CompareVariablesNodeViewModel(),
        new BuildTestReportNodeViewModel(),
        new PrintLabelNodeViewModel(),
        new SendTestReportNodeViewModel(),
        new LabelNodeViewModel(),
        new SubtestNodeViewModel(),
        new ForEachSlaveNodeViewModel(),
        new WaitUntilNodeViewModel(),
        new OperatorActionNodeViewModel()
    };

    public ObservableCollection<NodePaletteCategoryViewModel> AvailableNodeCategories { get; } = new();

    public ObservableCollection<GraphProfile> Profiles { get; } = new();

    private string _profileSearch = string.Empty;

    public string ProfileSearch
    {
        get => _profileSearch;
        set
        {
            _profileSearch = value;
            OnPropertyChanged(nameof(ProfileSearch));
            RefreshProfiles();
        }
    }

    private GraphProfile? _selectedProfile;

    public GraphProfile? SelectedProfile
    {
        get => _selectedProfile;
        set => _ = SelectProfileAsync(value);
    }

    internal async Task SelectProfileAsync(GraphProfile? profile, bool reload = false)
    {
        if (_refreshingProfiles || !CanChooseProfile || (!reload && ReferenceEquals(_selectedProfile, profile)))
            return;

        var reconnect = IsConnected;
        IsConnecting = true;
        try
        {
            // The previous poller must finish before replacing its register map.
            ResetProfilePreparation();
            if (_registerMonitor != null) await _registerMonitor.StopAsync();
            DisposeRegisterMonitor();
            IsMonitoringActive = false;
            _registerState.Clear();
            _slaveManager.Slaves.Clear();
            SlaveRegistry.Instance.SyncSlaves(_slaveManager.Slaves);
            _selectedProfile = profile;
            if (profile == null) _currentProfilePath = null;
            OnPropertyChanged(nameof(SelectedProfile));
            OnPropertyChanged(nameof(SelectedStationProfile));
            UpdateStationProfileSelection();
            if (profile != null)
            {
                LoadProfile(profile.FilePath);
                if (_profileLoadError.Length == 0)
                {
                    if (reconnect)
                        await ValidateConnectedProfileAsync();
                    else StatusMessage = GraphConnectionRequirements.RequiresStandConnection(RootGraph)
                        ? "Профиль выбран. Подключите стенд." : "Профиль выбран.";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Не удалось переключить профиль. Подробности в журнале.";
            LogUiException("Переключение профиля", ex);
        }
        finally { IsConnecting = false; }
        if (profile != null && _profileLoadError.Length == 0)
            await PrepareOpenedProfileAsync();
    }

    private ConnectionViewModel? _selectedConnection;

    public ConnectionViewModel? SelectedConnection
    {
        get => _selectedConnection;
        private set
        {
            if (ReferenceEquals(_selectedConnection, value))
                return;

            if (_selectedConnection != null)
                _selectedConnection.IsSelected = false;

            _selectedConnection = value;

            if (_selectedConnection != null)
            {
                _selectedConnection.IsSelected = true;
                SelectedNodes.Clear();
            }

            OnPropertyChanged(nameof(SelectedConnection));
        }
    }

    public TestViewModel(ModbusService modbusService, SlaveManager slaveManager, bool allowEditing = true)
        : this(modbusService, slaveManager, allowEditing, null) { }

    internal TestViewModel(ModbusService modbusService, SlaveManager slaveManager, bool allowEditing, string? trainingDirectory)
    {
        _trainingDirectory = trainingDirectory;
        _allowEditing = allowEditing;
        _modbusService = modbusService ?? throw new ArgumentNullException(nameof(modbusService));
        _slaveManager = slaveManager ?? throw new ArgumentNullException(nameof(slaveManager));

        _graphPath.Add(RootGraph.Title);
        CurrentGraph = RootGraph;

        TestingLogger = IsTraining ? LoggingService.Instance.CreateTrainingLogger() : LoggingService.Instance.CreateLogger("Testing");
        _modbusService.ReconnectStatusChanged += OnReconnectStatusChanged;
        _slaveManager.Slaves.CollectionChanged += OnStandDevicesChanged;
        RefreshStandIdentity();

        ToggleConnectionCommand = new AsyncRelayCommand(ToggleConnectionAsync, () => IsConnected ? CanDisconnectStation : CanConnectStation);
        RunGraphCommand = new AsyncRelayCommand(RunGraphAsync, () => CanStartRun);
        PauseTestCommand = new RelayCommand(PauseTest, () => CanPauseTest);
        ResumeTestCommand = new RelayCommand(ResumeTest, () => CanResumeTest);
        StopTestCommand = new RelayCommand(StopTest, () => IsTestRunning && !IsStopping);

        PendingConnection = new PendingConnectionViewModel(this);

        AddNodeCommand = new RelayCommand<string?>(AddNode, _ => CanEditGraph);
        DisconnectConnectorCommand = new RelayCommand<ConnectorViewModel?>(DisconnectConnector, _ => CanEditGraph);
        DeleteSelectedNodesCommand = new RelayCommand(DeleteSelectedNodes, () => CanEditGraph);
        ClearGraphCommand = new AsyncRelayCommand(RequestClearGraphAsync, () => CanEditGraph);
        GoBackGraphCommand = new RelayCommand(GoBackGraph);
        NewProfileCommand = new RelayCommand(CreateNewProfile, () => CanEditGraph && CanChooseProfile && !IsConnected);
        SaveGraphCommand = new AsyncRelayCommand(SaveGraphAsync, () => CanEditGraph && !IsActionGraphEditor);
        LoadProfileCommand = new AsyncRelayCommand(async () => RefreshProfiles());
        ImportProfilesCommand = new AsyncRelayCommand(ImportProfilesAsync, () => CanEditGraph);
        TogglePaletteCommand = new RelayCommand(() => IsPaletteCollapsed = !IsPaletteCollapsed);
        UndoCommand = new RelayCommand(UndoAction, () => CanEditGraph && CurrentGraph.UndoRedo.CanUndo);
        RedoCommand = new RelayCommand(RedoAction, () => CanEditGraph && CurrentGraph.UndoRedo.CanRedo);

        _history = new ProfileEditHistory(RootGraph, () => CanEditGraph, NavigateToGraph);
        AttachUndoRedo(_history.Manager);

        Profiles.CollectionChanged += (_, _) => { if (!_refreshingProfiles) RefreshEngineerProfileGroups(); };
        BuildNodePaletteCategories();
        RootGraph.QuickActions.CollectionChanged += (_, _) => RefreshQuickActions();
        RootGraph.RegisterIndicators.CollectionChanged += (_, _) => RefreshRegisterIndicators();
        RefreshProfiles();

        StatusMessage = "Готов к подключению.";
    }

    private void BuildNodePaletteCategories()
    {
        NodeViewModel Find(string title) => AvailableNodes.First(n => n.Title == title);

        AvailableNodeCategories.Clear();
        AvailableNodeCategories.Add(new NodePaletteCategoryViewModel("Платы и инструменты", Find("Serial Console"), Find("Run Program"), Find("Parse Response"), Find("Check LED Color")));

        AvailableNodeCategories.Add(new NodePaletteCategoryViewModel(
            "Структура",
            Find("Старт"),
            Find("Конец"),
            Find("Подтест"),
            Find("Цикл For"),
            Find("Метка"),
            Find("Задержка")));

        AvailableNodeCategories.Add(new NodePaletteCategoryViewModel(
            "Modbus",
            Find("Запись регистра"),
            Find("Проверка диапазона"),
            Find("Ожидание значения")));

        AvailableNodeCategories.Add(new NodePaletteCategoryViewModel(
            "Проверки",
            Find("Selftest Check"),
            Find("Check Variable Equality"),
            Find("Check Variable Range")));

        AvailableNodeCategories.Add(new NodePaletteCategoryViewModel(
            "HTTP и сеть",
            Find("Clear ARP Cache"),
            Find("Get Serial Number"),
            Find("Set Pro MAC"),
            Find("Set PSW MAC (UDP)"),
            Find("Update PSW Firmware"),
            Find("Run Data Test"),
            Find("Read HTTP Variable"),
            Find("Wait Variable Until")));

        AvailableNodeCategories.Add(new NodePaletteCategoryViewModel(
            "Оператор и отчеты",
            Find("Действие оператора"),
            Find("Build MAC From Serial"),
            Find("Compare Variables"),
            Find("Build Test Report"),
            Find("Print Label"),
            Find("Send Test Report")));
    }

    private void OnUndoRedoStateChanged()
    {
        if (!IsRunEntryAttached()) ResetPartialRun();
        else NotifyPartialRun();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));

        if (UndoCommand is RelayCommand undoCommand)
            undoCommand.NotifyCanExecuteChanged();

        if (RedoCommand is RelayCommand redoCommand)
            redoCommand.NotifyCanExecuteChanged();
    }

    private void UndoAction()
    {
        if (!CanEditGraph) return;
        _history.BreakMerge();
        _history.Manager.Undo();
        ResetConnectorsState();
    }

    private void RedoAction()
    {
        if (!CanEditGraph) return;
        _history.BreakMerge();
        _history.Manager.Redo();
        ResetConnectorsState();
    }

    public void DuplicateNodes()
    {
        if (!CanEditGraph || !SelectedNodes.Any(n => !IsProtectedBoundaryNode(n))) return;
        CopyNodes();
        PasteNodes();
    }

    public void CopyNodes()
    {
        var selected = SelectedNodes
            .Where(n => !IsProtectedBoundaryNode(n))
            .ToList();

        if (selected.Count == 0) return;

        var selectedIndexes = selected
            .Select((node, index) => new { node, index })
            .ToDictionary(x => x.node, x => x.index);

        _clipboardNodes = selected
            .Select(node => new ClipboardNode(CloneNodeDeep(node), node.Location))
            .ToList();

        _clipboardConnections = Connections
            .Where(c => c.Source.Parent != null
                        && c.Target.Parent != null
                        && selectedIndexes.ContainsKey(c.Source.Parent)
                        && selectedIndexes.ContainsKey(c.Target.Parent))
            .Select(c => new ClipboardConnection(
                selectedIndexes[c.Source.Parent!],
                GetConnectorIndex(c.Source),
                selectedIndexes[c.Target.Parent!],
                GetConnectorIndex(c.Target)))
            .Where(c => c.SourceConnectorIndex >= 0 && c.TargetConnectorIndex >= 0)
            .ToList();

        OnPropertyChanged(nameof(CanPaste));
        OnPropertyChanged(nameof(CopiedNodeCount));
        StatusMessage = $"Скопировано нод: {CopiedNodeCount}. Можно вставить в другой профиль.";
    }

    public void PasteNodes(Point? location = null)
    {
        if (!CanEditGraph) return;
        if (_clipboardNodes == null || _clipboardNodes.Count == 0) return;

        var offset = location is { } target
            ? new Point(target.X - _clipboardNodes.Min(n => n.Location.X),
                target.Y - _clipboardNodes.Min(n => n.Location.Y))
            : new Point(30, 30);

        var pastedNodes = new List<NodeViewModel>();

        foreach (var entry in _clipboardNodes)
        {
            var clone = CloneNodeDeep(entry.Node);
            clone.Location = new Point(entry.Location.X + offset.X, entry.Location.Y + offset.Y);
            pastedNodes.Add(clone);
        }

        var newConnections = new List<ConnectionViewModel>();
        if (_clipboardConnections != null)
        {
            foreach (var conn in _clipboardConnections)
            {
                var srcNode = pastedNodes.ElementAtOrDefault(conn.SourceNodeIndex);
                var tgtNode = pastedNodes.ElementAtOrDefault(conn.TargetNodeIndex);

                if (srcNode == null || tgtNode == null)
                    continue;

                var srcConnector = srcNode.Output.ElementAtOrDefault(conn.SourceConnectorIndex);
                var tgtConnector = tgtNode.Input.ElementAtOrDefault(conn.TargetConnectorIndex);

                if (srcConnector != null && tgtConnector != null)
                    newConnections.Add(new ConnectionViewModel(srcConnector, tgtConnector));
            }
        }

        _history.Execute(CurrentGraph, new PasteNodesCommand(Nodes, Connections, pastedNodes, newConnections));

        // Select only pasted nodes
        SelectedNodes.Clear();
        foreach (var node in pastedNodes)
        {
            node.IsSelected = true;
            SelectedNodes.Add(node);
        }

        ResetConnectorsState();
        StatusMessage = $"Вставлено нод: {pastedNodes.Count}.";
    }

    private static int GetConnectorIndex(ConnectorViewModel connector)
    {
        if (connector.Parent == null)
            return -1;

        var outputIndex = connector.Parent.Output.IndexOf(connector);
        if (outputIndex >= 0)
            return outputIndex;

        return connector.Parent.Input.IndexOf(connector);
    }

    private static NodeViewModel CloneNodeDeep(NodeViewModel source)
    {
        var clone = source.Clone();
        clone.NodeColor = source.NodeColor;
        clone.CustomName = source.CustomName;
        clone.BoardElements = source.BoardElements;
        clone.RunOnlyOnFirstRun = source.RunOnlyOnFirstRun;

        if (source is ICompositeNodeViewModel sourceComposite
            && clone is ICompositeNodeViewModel cloneComposite)
        {
            CopyGraph(sourceComposite.BodyGraph, cloneComposite.BodyGraph);
        }

        return clone;
    }

    private static void CopyGraph(GraphWorkspaceViewModel source, GraphWorkspaceViewModel target)
    {
        target.Clear();
        target.Title = source.Title;
        target.IsBodyGraph = source.IsBodyGraph;
        target.UsesBodyBoundaryNodes = source.UsesBodyBoundaryNodes;

        var nodeMap = new Dictionary<NodeViewModel, NodeViewModel>();

        foreach (var sourceNode in source.Nodes)
        {
            var clone = CloneNodeDeep(sourceNode);
            clone.Location = sourceNode.Location;
            nodeMap[sourceNode] = clone;
            target.Nodes.Add(clone);
        }

        foreach (var connection in source.Connections)
        {
            if (connection.Source.Parent == null || connection.Target.Parent == null)
                continue;

            if (!nodeMap.TryGetValue(connection.Source.Parent, out var sourceNode))
                continue;

            if (!nodeMap.TryGetValue(connection.Target.Parent, out var targetNode))
                continue;

            var sourceConnector = sourceNode.Output.ElementAtOrDefault(GetConnectorIndex(connection.Source));
            var targetConnector = targetNode.Input.ElementAtOrDefault(GetConnectorIndex(connection.Target));

            if (sourceConnector != null && targetConnector != null)
                target.Connections.Add(new ConnectionViewModel(sourceConnector, targetConnector));
        }
    }

    [ObservableProperty] private bool isProfilesCollapsed;
    public string ProfilesToggleTip => IsProfilesCollapsed ? "Показать профили" : "Скрыть профили";
    partial void OnIsProfilesCollapsedChanged(bool value) => OnPropertyChanged(nameof(ProfilesToggleTip));
    [RelayCommand] private void ToggleProfiles() => IsProfilesCollapsed = !IsProfilesCollapsed;

    partial void OnIsPaletteCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(PaletteToggleIcon));
        OnPropertyChanged(nameof(PaletteToggleTip));
    }

    partial void OnCurrentGraphChanged(GraphWorkspaceViewModel value)
    {
        SelectedConnection = null;

        OnPropertyChanged(nameof(Nodes));
        OnPropertyChanged(nameof(Connections));
        OnPropertyChanged(nameof(SelectedNodes));
        OnPropertyChanged(nameof(CurrentGraphPath));

        CanGoBackGraph = _graphStack.Count > 0;

        if (_history != null) { _history.BreakMerge(); AttachUndoRedo(_history.Manager); }
        PendingConnection?.Reset();
    }

    private void AttachUndoRedo(UndoRedoManager undoRedo)
    {
        if (ReferenceEquals(_currentUndoRedo, undoRedo))
        {
            OnUndoRedoStateChanged();
            return;
        }

        if (_currentUndoRedo != null)
            _currentUndoRedo.StateChanged -= OnUndoRedoStateChanged;

        _currentUndoRedo = undoRedo;
        _currentUndoRedo.StateChanged += OnUndoRedoStateChanged;
        OnUndoRedoStateChanged();
    }
    public void SelectConnection(ConnectionViewModel? connection)
    {
        SelectedConnection = connection;
    }

    public void DeleteSelectedConnection()
    {
        DeleteConnection(SelectedConnection);
    }

    public void DeleteConnection(ConnectionViewModel? connection)
    {
        if (!CanEditGraph) return;
        if (connection == null) return;
        if (!Connections.Contains(connection)) return;

        if (ReferenceEquals(SelectedConnection, connection))
            SelectedConnection = null;

        _history.Execute(CurrentGraph, new DeleteConnectionCommand(Connections, connection));

        ResetConnectorsState();
        StatusMessage = "Соединение удалено.";
    }

    private void RemoveConnection(ConnectionViewModel connection)
    {
        connection.Source.IsConnected = false;
        connection.Target.IsConnected = false;

        Connections.Remove(connection);

        if (ReferenceEquals(SelectedConnection, connection))
            SelectedConnection = null;
    }

    private void NavigateToGraph(GraphWorkspaceViewModel target)
    {
        if (ReferenceEquals(CurrentGraph, target)) return;
        var path = new List<NodeViewModel>();
        bool Find(GraphWorkspaceViewModel graph)
        {
            if (ReferenceEquals(graph, target)) return true;
            foreach (var node in graph.Nodes.OfType<CompositeNodeViewModel>())
            {
                path.Add(node);
                if (Find(node.BodyGraph)) return true;
                path.RemoveAt(path.Count - 1);
            }
            return false;
        }
        if (!Find(RootGraph)) return;
        ResetToRootGraph();
        foreach (var node in path) OpenCompositeNodeBody(node);
        CurrentGraphOpened?.Invoke();
    }

    public void ResetToRootGraph()
    {
        _graphStack.Clear();
        _graphPath.Clear();
        _graphPath.Add(RootGraph.Title);
        CurrentGraph = RootGraph;
        CanGoBackGraph = false;
        PendingConnection.Reset();
        OnPropertyChanged(nameof(CurrentGraphPath));
    }

    [RelayCommand]
    private void OpenCompositeNodeBody(NodeViewModel? node)
    {
        if (node is not ICompositeNodeViewModel composite)
            return;

        if (node is CompositeNodeViewModel compositeNode)
            compositeNode.EnsureDefaultBodyNodes();

        _graphStack.Push(CurrentGraph);
        CurrentGraph = composite.BodyGraph;
        _graphPath.Add(node.DisplayTitle);
        CanGoBackGraph = true;

        StatusMessage = $"Открыто тело ноды: {node.DisplayTitle}.";
        OnPropertyChanged(nameof(CurrentGraphPath));
        CurrentGraphOpened?.Invoke();
    }

    private void GoBackGraph()
    {
        if (_graphStack.Count == 0)
            return;

        CurrentGraph = _graphStack.Pop();
        if (_graphPath.Count > 1)
            _graphPath.RemoveAt(_graphPath.Count - 1);

        CanGoBackGraph = _graphStack.Count > 0;

        StatusMessage = $"Открыт граф: {CurrentGraph.Title}.";
        OnPropertyChanged(nameof(CurrentGraphPath));
        CurrentGraphOpened?.Invoke();
    }

    internal Func<Task<bool>> ConfirmClearGraphAsync { get; set; } = async () =>
    {
        if (Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is { } owner)
            return await new ClearGraphDialog().ShowDialog<bool>(owner);
        return false;
    };

    private async Task RequestClearGraphAsync()
    {
        if (!CanEditGraph || Nodes.Count == 0) return;
        var graph = CurrentGraph;
        if (await ConfirmClearGraphAsync() && CanEditGraph && ReferenceEquals(graph, CurrentGraph))
            ClearGraph();
    }

    public void ClearGraph()
    {
        if (!CanEditGraph) return;
        foreach (var node in Nodes)
        {
            if (!SelectedNodes.Contains(node))
                SelectedNodes.Add(node);
        }

        DeleteSelectedNodes();

        PendingConnection.Reset();

        EnsureBodyBoundaryNodesIfNeeded();
    }

    private void CreateNewProfile()
    {
        if (!CanEditGraph || !CanChooseProfile || IsConnected) { StatusMessage = "Отключите стенд перед сменой профиля."; return; }
        ResetProfilePreparation();
        ResetToRootGraph();

        ResetPartialRun();
        RootGraph.Clear();
        RootGraph.QuickActions.Clear();
        RootGraph.RegisterIndicators.Clear();
        RootGraph.Hardware = null;
        RootGraph.Board = null;
        RootGraph.ProductKind = ProductFilter == "Платы" ? "Board" : "Switch";
        NotifyProfileMetadataChanged();
        RootGraph.Title = "Новый профиль";
        RootGraph.DeviceModel = null;
        RootGraph.DeviceModelId = null;
        RootGraph.ConfigurationName = null;
        NotifyDeviceNameChanged();
        RootGraph.IsBodyGraph = false;
        RootGraph.UsesBodyBoundaryNodes = false;
        _graphPath.Clear();
        _graphPath.Add(RootGraph.Title);
        OnPropertyChanged(nameof(CurrentGraphPath));

        ClearExecutionHighlightsRecursive(RootGraph, clearResults: true);
        _history.Clear();
        ResetConnectorsStateRecursive(RootGraph);

        _currentProfilePath = null;
        _savedProfileName = null;
        RootGraph.UpdatedAt = null;
        OnPropertyChanged(nameof(CurrentProfileName));
        SetSelectedProfileWithoutLoading(null);
        _profileLoadError = string.Empty;
        Station.Prepare(RootGraph);
        RefreshStationAvailability();

        StatusMessage = "Создан новый профиль. Укажите устройство, тип изделия и название проверки, затем нажмите «Сохранить».";
    }

    public void DeleteSelectedNodes()
    {
        if (!CanEditGraph) return;
        SelectedConnection = null;

        var selected = SelectedNodes
            .Where(node => !IsProtectedBoundaryNode(node))
            .ToList();

        if (selected.Count == 0) return;

        var removedConnections = selected
            .SelectMany(node => Connections
                .Where(c => c.Source.Parent == node || c.Target.Parent == node))
            .Distinct()
            .ToList();

        _history.Execute(CurrentGraph, new DeleteNodesCommand(Nodes, Connections, selected, removedConnections));

        SelectedNodes.Clear();
        ResetConnectorsState();
        EnsureBodyBoundaryNodesIfNeeded();
    }

    private bool IsProtectedBoundaryNode(NodeViewModel node)
    {
        if (node is BodyStartNodeViewModel or BodyEndNodeViewModel)
            return true;

        return CurrentGraph.IsBodyGraph
               && !CurrentGraph.UsesBodyBoundaryNodes
               && node is StartNodeViewModel or EndNodeViewModel;
    }

    public void Connect(ConnectorViewModel source, ConnectorViewModel target)
    {
        if (!CanEditGraph) return;
        if (source.Parent == null || target.Parent == null || source.Parent == target.Parent ||
            !Nodes.Contains(source.Parent) || !Nodes.Contains(target.Parent) ||
            !source.Parent.Output.Contains(source) || !target.Parent.Input.Contains(target) ||
            Connections.Any(connection => connection.Source == source && connection.Target == target))
            return;
        SelectedConnection = null;

        _history.Execute(CurrentGraph, new AddConnectionCommand(Connections, new ConnectionViewModel(source, target)));
    }

    private void DisconnectConnector(ConnectorViewModel? connector)
    {
        if (connector == null)
            return;

        var connection = Connections.FirstOrDefault(x =>
            x.Source == connector ||
            x.Target == connector);

        DeleteConnection(connection);
    }

    private async Task ToggleConnectionAsync()
    {
        if (IsTraining) return;
        if (IsConnected ? !CanDisconnectStation : !CanConnectStation) return;
        IsConnecting = true;
        try
        {
            if (IsConnected) await DisconnectAsync();
            else
            {
                await ConnectAsync();
                if (IsConnected && _currentProfilePath != null && _profileLoadError.Length == 0)
                    await ValidateConnectedProfileAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Не удалось подключить стенд. Проверьте кабель и питание.";
            LogUiException("Подключение/отключение стенда", ex);
        }
        finally { IsConnecting = false; }
        if (IsConnected) await PrepareOpenedProfileAsync();
    }

    internal static string[] GetStandPortCandidates(HardwareConfiguration? hardware, IEnumerable<string> availablePorts, string? requestedPort = null)
    {
        var consolePorts = (hardware?.ConsoleDevices ?? new()).Select(d => d.PortName?.Trim())
            .Where(p => !string.IsNullOrEmpty(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return availablePorts.Where(p => !consolePorts.Contains(p))
            .Where(p => string.IsNullOrWhiteSpace(requestedPort) || p.Equals(requestedPort.Trim(), StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal Func<string[]> AvailableStandPorts { get; set; } = SerialPort.GetPortNames;

    private async Task ConnectAsync()
    {
        ResetProfilePreparation();
        IsMonitoringActive = false;
        StatusMessage = "Открытие COM-порта...";
        _registerState.Clear();
        _slaveManager.Slaves.Clear();
        SlaveRegistry.Instance.SyncSlaves(_slaveManager.Slaves);
        var settings = AppSettings.Instance.Discovery;
        foreach (var port in GetStandPortCandidates(null, AvailableStandPorts(), AppSettings.Instance.StandPort))
        {
            if (!await _modbusService.ConnectAsync(port, settings.BaudRate, settings.Parity,
                    settings.DataBits, settings.StopBits))
            {
                ReportPortSearchFailure(port, new IOException(_modbusService.LastError ?? "Порт недоступен"), true);
                continue;
            }
            SelectedPort = port;
            IsConnected = true;
            _history.Clear();
            SlaveRegistry.Instance.NotifyConnected(true);
            StatusMessage = "COM-порт открыт. Выберите профиль.";
            TestingLogger.Info($"Открыт COM-порт {port}.");
            return;
        }
        StatusMessage = "Нет доступного COM-порта.";
        TestingLogger.Error(StatusMessage);
    }

    private async Task<bool> ValidateConnectedProfileAsync()
    {
        ResetProfilePreparation();
        IsMonitoringActive = false;
        if (!IsConnected) return false;
        if (!ProfileRequiresHardware) return true;
        try
        {
            var hardware = RootGraph.Hardware
                ?? throw new InvalidOperationException("В профиле не задано оборудование. Заполните вкладку «Оборудование».");
            hardware.Validate();
            // Apply the profile line settings only to the already selected port.
            await _modbusService.ConfigureLineAsync(hardware.BaudRate, hardware.Parity,
                hardware.DataBits, hardware.StopBits);
            if (!await StartMonitoringAsync()) return false;
            SlaveRegistry.Instance.SyncSlaves(_slaveManager.Slaves);
            return true;
        }
        catch (Exception ex)
        {
            IsConnected = _modbusService.IsConnected;
            SlaveRegistry.Instance.NotifyConnected(IsConnected);
            IsMonitoringActive = false;
            StatusMessage = "Оборудование не соответствует профилю. Подробности в журнале.";
            TestingLogger.Error($"Проверка оборудования на {SelectedPort}: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> StartMonitoringAsync()
    {
        var count = await _slaveManager.ConfigureAsync(RootGraph.Hardware
            ?? throw new InvalidOperationException("В профиле не задано оборудование."));

        if (count == 0)
        {
            StatusMessage = "Слейвы не найдены.";
            TestingLogger.Warning("Устройства не найдены. Проверьте подключение.");
            return false;
        }

        DisposeRegisterMonitor();

        _registerMonitor = new RegisterMonitor(
            _slaveManager,
            _registerState,
            TestingLogger);

        _registerMonitor.PollInterval = RootGraph.Hardware?.PollIntervalMs ?? 1000;
        _registerMonitor.ConnectionLost += OnConnectionLost;
        _registerMonitor.Start();

        IsMonitoringActive = true;
        StatusMessage = $"Найдено устройств: {count}";
        TestingLogger.Info(_currentProfilePath == null
            ? $"Найдено устройств: {count}. Выберите профиль проверки."
            : $"Найдено устройств: {count}. Оборудование проверено.");
        return true;
    }

    private void OnConnectionLost(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ResetProfilePreparation();
            IsMonitoringActive = false;
            StopRunForConnectionFailure();
            StatusMessage = "Связь Modbus потеряна. Выполняется автоматическое восстановление.";
            TestingLogger.Warning("[MODBUS] Мониторинг обнаружил потерю связи. Автоматическое восстановление выполнится при следующей Modbus-операции.");
        });
    }

    private void OnReconnectStatusChanged(object? sender, string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!message.Contains("восстановлено", StringComparison.OrdinalIgnoreCase))
            {
                CancelQuickActions();
                foreach (var action in QuickActions) action.InvalidateState();
                ResetProfilePreparation();
                StopRunForConnectionFailure();
            }
            if (IsStopping)
            {
                CloseReconnectDialog();
                return;
            }
            StatusMessage = message;

            if (message.Contains("восстановлено", StringComparison.OrdinalIgnoreCase))
            {
                IsConnected = true;
                TestingLogger.Info(message);
                CloseReconnectDialog();
            }
            else if (message.Contains("60 секунд", StringComparison.OrdinalIgnoreCase))
            {
                IsConnected = false;
                TestingLogger.Error(message);
                ShowReconnectDialog(message, canClose: true);
            }
            else
            {
                TestingLogger.Warning(message);
                ShowReconnectDialog(message, canClose: false);
            }
        });
    }

    private void StopRunForConnectionFailure()
    {
        if (!IsTestRunning || IsStopping) return;
        _runConnectionFailure = "Потеряна связь со стендом. Проверка остановлена для снятия питания.";
        TestingLogger.Error(_runConnectionFailure);
        StopTestCommand.Execute(null);
    }

    private void ShowReconnectDialog(string message, bool canClose)
    {
        if (_modbusReconnectDialog == null)
        {
            _modbusReconnectDialog = new Views.ModbusReconnectDialog();
            _modbusReconnectDialog.Closed += (_, _) => _modbusReconnectDialog = null;

            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

            if (mainWindow != null)
                _modbusReconnectDialog.Show(mainWindow);
            else
                _modbusReconnectDialog.Show();
        }

        _modbusReconnectDialog.SetMessage(message, canClose);
    }

    private void CloseReconnectDialog()
    {
        _modbusReconnectDialog?.Close();
        _modbusReconnectDialog = null;
    }

    private async Task DisconnectAsync()
    {
        ResetProfilePreparation();
        if (_registerMonitor != null) await _registerMonitor.StopAsync();
        DisposeRegisterMonitor();

        await _modbusService.DisconnectAsync();

        _registerState.Clear();
        IsConnected = false;
        IsMonitoringActive = false;
        StatusMessage = "Отключено.";
        CloseReconnectDialog();
        TestingLogger.Info("Отключено от стенда.");
        SlaveRegistry.Instance.NotifyConnected(false);

    }

    private async Task RunGraphAsync()
    {
        if (!CanStartRun) return;
        await ExecuteGraphAsync();
    }

    private async Task ExecuteGraphAsync(ProfilePreparationPlan? preparation = null)
    {
        if (preparation == null ? !CanStartRun : !CanPrepareProfile) return;
        var preparing = preparation != null;
        if (IsTraining && !IsSafeTrainingGraph())
        {
            StatusMessage = "В обучении доступны только старт, конец, задержка и подтверждение оператора.";
            return;
        }
        if (IsTestRunning)
        {
            StatusMessage = "Тест уже выполняется.";
            return;
        }

        if (IsOperatorView) ResetPartialRun();
        if (!preparing && !IsRunEntryAttached())
        {
            ResetPartialRun();
            StatusMessage = "Стартовая нода удалена. Выберите ноду заново.";
            return;
        }
        var partialRun = !preparing && HasPartialRun;
        var runWorkspace = partialRun ? _runWorkspace ?? RootGraph : RootGraph;
        var runEntry = partialRun ? _runEntry : null;
        var singleNode = partialRun && _runSingleNode;
        var runCaption = PartialRunCaption;
        var slaveId = RunSlaveId;
        if (partialRun && slaveId.HasValue &&
            (slaveId < 1 || slaveId > 247 || slaveId != decimal.Truncate(slaveId.Value)))
        {
            StatusMessage = "Адрес устройства должен быть целым числом от 1 до 247 или оставаться пустым.";
            return;
        }
        var requiresStandConnection = preparing ? preparation!.Nodes.Any(GraphConnectionRequirements.RequiresStandConnection) :
            partialRun ? PartialRunRequiresStand() : GraphConnectionRequirements.RequiresStandConnection(RootGraph);
        if (!IsConnected && requiresStandConnection)
        {
            StatusMessage = "Этот граф содержит Modbus-ноды. Перед запуском необходимо подключиться к стенду.";
            return;
        }

        var profileName = CurrentProfileName;
        // Monotonic elapsed time includes pauses, operator actions and failure cleanup.
        var runTimer = System.Diagnostics.Stopwatch.StartNew();
        var logFilePath = IsTraining ? null : LoggingService.Instance.StartFileLogForRun(profileName);

        TestingLogger.Info(preparing ? $"Подготовка стенда при открытии профиля: {profileName}" : $"Запуск теста: {profileName}");
        if (partialRun) TestingLogger.Info($"Частичный запуск: {runCaption}. Новый контекст; cleanup при ошибке сохранён.");
        if (!string.IsNullOrWhiteSpace(logFilePath))
            TestingLogger.Info($"Логи записываются в файл: {logFilePath}");
        if (!IsConnected)
        {
            TestingLogger.Info("Автономный запуск: выбранные действия не требуют подключения к Modbus-стенду.");
        }

        _testRunCts?.Dispose();
        _testRunCts = new CancellationTokenSource();
        _runConnectionFailure = null;
        _pauseCompletion = CreateCompletedPauseCompletion();
        IsTestRunning = true;
        IsStopping = false;
        _boardNodeStates.Clear();
        BoardChanged?.Invoke();
        Station.BeginRun(RootGraph, preparing);
        IsTestPaused = false;
        IsPauseEffective = false;
        SelfTestPageState.Reset();
        TestContext? context = null;
        var runCleanup = false;
        var executionStatus = ExecutionStatus.Failed;
        ExecutionFailure? runFailure = null;
        Dictionary<string, object>? preparedVariables = null;

        try
        {
            if (partialRun) NavigateToGraph(runWorkspace);
            else ResetToRootGraph();

            ClearExecutionHighlightsRecursive(RootGraph, clearResults: true);

            using var compiler = new GraphCompiler(_modbusService, TestingLogger) { ProfileHardware = RootGraph.Hardware, ConnectedStandPort = IsConnected ? SelectedPort : null };
            // Compile every preparation operation before performing the first hardware action.
            var graphs = preparing ? preparation!.Nodes.Select(node =>
            {
                GraphCompiler.ValidateRunConnections(RootGraph, RootGraph.Hardware, SelectedPort, node, true);
                return compiler.Compile(RootGraph, node, singleNode: true);
            }).ToArray() : CompileRun();
            CompiledGraph[] CompileRun()
            {
                GraphCompiler.ValidateRunConnections(runWorkspace, RootGraph.Hardware, SelectedPort, runEntry, singleNode);
                return new[] { compiler.Compile(runWorkspace, runEntry, singleNode) };
            }

            var mainWindow = IsTraining ? TrainingDialogOwner?.Invoke() : Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktopLife
                    ? desktopLife.MainWindow
                    : null;

            context = new TestContext(_registerState)
            {
                SkipFirstRunNodes = !preparing && !partialRun,
                RequireFreshMonitoring = RootGraph.Hardware != null,
                MaxRegisterAgeMs = Math.Max(10000, (RootGraph.Hardware?.PollIntervalMs ?? 1000) * 3),
                ModbusRegisters = new ModbusRegisterCatalog(_slaveManager.GetSlavesSnapshot()),
                CancellationToken = _testRunCts.Token,
                IsConnected = IsConnected,
                ProfileName = profileName,
                ExecutionObserver = this,
                WaitIfPausedAsync = WaitIfPausedAsync,
                SelfTestPageState = SelfTestPageState,
                OperatorInput = async (message, token) => await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (mainWindow == null) throw new InvalidOperationException("Не найдено окно ввода.");
                    var dialog = new Views.OperatorActionDialog(message, requestInput: true);
                    using var registration = token.Register(() => Dispatcher.UIThread.Post(() => dialog.Close()));
                    await dialog.ShowDialog(mainWindow); token.ThrowIfCancellationRequested();
                    return dialog.Confirmed ? dialog.InputText : null;
                }),
                OperatorPrompt = async message =>
                {
                    return await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        if (IsOperatorView)
                            return await Station.RequestActionAsync(message, context!.CancellationToken);
                        if (mainWindow == null)
                            throw new InvalidOperationException("Не найдено окно для инструкции оператору.");
                        var dialog = new Views.OperatorActionDialog(message);
                        using var registration = context!.CancellationToken.Register(() =>
                            Dispatcher.UIThread.Post(() => dialog.Close()));
                        await dialog.ShowDialog(mainWindow);
                        context.CancellationToken.ThrowIfCancellationRequested();
                        return dialog.Confirmed;
                    });
                }
            };

            if (!preparing && !partialRun)
                foreach (var pair in _profilePreparationVariables)
                    context.SetVariable(pair.Key, ParallelVariableSnapshot.Clone(pair.Value));
            if (partialRun && slaveId is >= 1 and <= 247)
            {
                context.CurrentSlaveId = (byte)slaveId.Value;
                context.SetVariable("slaveId", (byte)slaveId.Value);
            }
            if (context.RequireFreshMonitoring)
                foreach (var slave in context.ModbusRegisters.SlaveIds) context.RequiredPollAfter[slave] = System.Diagnostics.Stopwatch.GetTimestamp();
            context.SetVariable("OperatorName", App.StartupUserName);
            var result = ExecutionStatus.Completed;
            foreach (var graph in graphs)
            {
                result = await new TestExecutor().ExecuteAsync(graph.StartNode, context, _testRunCts.Token);
                if (result != ExecutionStatus.Completed || context.HasCriticalError || context.ReportEntries.Any(e => !e.IsSuccess)) break;
            }
            executionStatus = result;
            runFailure = context.CriticalFailure ?? context.Failure;
            if (runFailure == null && context.ReportEntries.FirstOrDefault(entry => !entry.IsSuccess) is { } failedCheck)
                runFailure = new ExecutionFailure(failedCheck.Name, failedCheck.Value);

            if (result != ExecutionStatus.Completed || context.HasCriticalError || context.ReportEntries.Any(e => !e.IsSuccess))
            {
                runCleanup = true;
                executionStatus = ExecutionStatus.Failed;
                context.HasCriticalError = true;
                context.SetVariable("Execution.Status", executionStatus.ToString());
                runFailure ??= new ExecutionFailure("Выполнение графа", $"Граф завершён с результатом {result}.");
                context.SetVariable("Execution.Error", runFailure.Summary);
                TestingLogger.Error($"[ОШИБКА] Тест завершён: {runFailure.Summary}");
            }
            else
            {
                if (preparing)
                    preparedVariables = context.Variables.ToDictionary(pair => pair.Key, pair => ParallelVariableSnapshot.Clone(pair.Value));
                context.SetVariable("Execution.Status", result.ToString());
            }
        }
        catch (OperationCanceledException) when (_testRunCts?.IsCancellationRequested == true)
        {
            executionStatus = ExecutionStatus.Cancelled;
            runCleanup = true;
            if (context != null)
            {
                context.HasCriticalError = true;
                context.SetVariable("Execution.Status", ExecutionStatus.Cancelled.ToString());
            }

            if (_runConnectionFailure != null)
            {
                executionStatus = ExecutionStatus.Failed;
                runFailure = new ExecutionFailure("Связь со стендом", _runConnectionFailure);
                context?.SetVariable("Execution.Status", executionStatus.ToString());
            }
            TestingLogger.Warning(_runConnectionFailure ?? "[ОСТАНОВ] Выполнение теста остановлено пользователем.");
            StatusMessage = "Выполнение теста остановлено.";
        }
        catch (Exception ex)
        {
            runCleanup = true;
            runFailure = context?.CriticalFailure ?? context?.Failure ??
                new ExecutionFailure(context == null ? "Подготовка графа" : "Выполнение графа", ex.Message);
            if (context != null)
            {
                context.HasCriticalError = true;
                context.SetVariable("Execution.Status", ExecutionStatus.Failed.ToString());
                context.SetVariable("Execution.Error", runFailure.Summary);
            }

            DiagnosticLog.WriteException("Выполнение теста", ex);
            TestingLogger.Error($"[ОШИБКА] {runFailure.Summary}");
        }
        finally
        {
            if (runCleanup)
            {
                context ??= new TestContext(_registerState)
                {
                    IsConnected = IsConnected, ProfileName = profileName, HasCriticalError = true,
                    ModbusRegisters = new ModbusRegisterCatalog(_slaveManager.GetSlavesSnapshot()),
                    ExecutionObserver = this
                };
                IsStopping = true;
                IsTestPaused = false;
                IsPauseEffective = false;
                _pauseCompletion.TrySetResult(true);
                try
                {
                    try
                    {
                        await DrainQuickActionsAsync();
                        if (_registerMonitor != null) await _registerMonitor.StopAsync();
                    }
                    catch (Exception ex)
                    {
                        DiagnosticLog.WriteException("Остановка мониторинга перед завершением", ex);
                        TestingLogger.Warning("Мониторинг завершился с ошибкой. Выполняется отключение выходов стенда.");
                    }
                    IsMonitoringActive = false;
                    NeedsStandInspection = !await RunFailureCleanupAsync(context);
                }
                catch (Exception ex)
                {
                    NeedsStandInspection = true;
                    context.SetVariable("Cleanup.Succeeded", false);
                    DiagnosticLog.WriteException("Аварийное завершение стенда", ex);
                    TestingLogger.Error("Завершение стенда не подтверждено. Отключите питание стенда и обратитесь к инженеру.");
                }
                finally
                {
                    CloseReconnectDialog();
                    if (!NeedsStandInspection && IsConnected && _registerMonitor != null)
                    {
                        try { _registerMonitor.Start(); IsMonitoringActive = true; }
                        catch (Exception ex)
                        {
                            DiagnosticLog.WriteException("Возобновление мониторинга", ex);
                            IsMonitoringActive = false;
                            TestingLogger.Warning("Мониторинг не возобновлён. Переподключитесь к стенду перед запуском.");
                        }
                    }
                }
            }

            runTimer.Stop();
            if (preparing)
            {
                var succeeded = executionStatus == ExecutionStatus.Completed && !runCleanup && preparedVariables != null;
                if (succeeded)
                {
                    _preparedProfileKey = CurrentPreparationKey();
                    _profilePreparationVariables = preparedVariables!;
                }
                else ResetProfilePreparation();
                Station.CompletePreparation(succeeded, NeedsStandInspection);
            }
            else Station.CompleteRun(executionStatus, context, partialRun, runTimer.Elapsed);
            StatusMessage = Station.Headline + ".";
            IsTestRunning = false;
            IsStopping = false;
            IsTestPaused = false;
            IsPauseEffective = false;
            _pauseCompletion.TrySetResult(true);
            _testRunCts?.Dispose();
            _testRunCts = null;
            ClearExecutionHighlightsRecursive(RootGraph, clearResults: false);
            ResetConnectorsStateRecursive(RootGraph);
            if (!NeedsStandInspection && context != null && context.Variables.ContainsKey("ReportDelivery.Status"))
            {
                var delivery = context.GetVariable<string>("ReportDelivery.Status");
                var devicePassed = !context.HasCriticalError && context.GetVariable<bool>("BuildReport.DevicePassed");
                StatusMessage = (devicePassed ? "Проверки изделия пройдены. " : "Проверки изделия не пройдены. ") +
                    context.GetVariable<string>("ReportDelivery.Message");
                if (delivery == "Sent") TestingLogger.Info(StatusMessage);
                else TestingLogger.Warning(StatusMessage);
            }
            if (partialRun && !NeedsStandInspection)
            {
                var resultText = executionStatus switch
                {
                    ExecutionStatus.Completed => "завершён",
                    ExecutionStatus.Cancelled => "остановлен",
                    _ => "завершён с ошибкой"
                };
                StatusMessage = $"Частичный запуск {resultText}. {runCaption}.";
                TestingLogger.Info(StatusMessage);
                ResetPartialRun();
            }
            var durationText = preparing ? "Время настройки: " + StationDashboardViewModel.FormatDuration(runTimer.Elapsed) : Station.DurationText;
            StatusMessage += " " + durationText;
            TestingLogger.Info(durationText);
            // Cleanup uses the same context and may fail itself; keep the main run's original cause last.
            if (runFailure != null)
                TestingLogger.Error(runFailure.LogMessage);
            if (!IsTraining) LoggingService.Instance.StopFileLogForRun();
        }
    }

    internal async Task<bool> RunFailureCleanupAsync(TestContext context)
    {
        var cleanupNodes = RootGraph.Nodes
            .OfType<SubtestNodeViewModel>()
            .Where(node => node.RunOnFailure && node.IsEnabled)
            .ToList();

        if (cleanupNodes.Count == 0)
        {
            var noHardware = !GraphConnectionRequirements.RequiresStandConnection(RootGraph);
            context.SetVariable("Cleanup.Succeeded", noHardware);
            return noHardware;
        }

        TestingLogger.Warning($"[ШАГ] Запуск cleanup-подтестов: {cleanupNodes.Count}.");

        var previousToken = context.CancellationToken;
        var previousPause = context.WaitIfPausedAsync;
        var previousSkipFirstRunNodes = context.SkipFirstRunNodes;
        var previousCleanup = context.IsCleanup;
        var allSucceeded = true;
        context.IsCleanup = true;
        context.SkipFirstRunNodes = false;

        context.WaitIfPausedAsync = _ => Task.CompletedTask;

        try
        {
            using var compiler = new GraphCompiler(_modbusService, TestingLogger) { ProfileHardware = RootGraph.Hardware, ConnectedStandPort = IsConnected ? SelectedPort : null };

            foreach (var cleanupNode in cleanupNodes)
            {
                Station.BeginCleanup(cleanupNode);
                var cleanupSucceeded = false;
                using var cleanupCts = new CancellationTokenSource(
                    cleanupNode.BodyGraph.Nodes.OfType<OperatorActionNodeViewModel>().Any()
                        ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(Math.Clamp(
                            BoardNodes(cleanupNode.BodyGraph).OfType<ModbusWriteNodeViewModel>().Count() * 20, 60, 300)));
                context.CancellationToken = cleanupCts.Token;
                try
                {
                    TestingLogger.Warning($"[ШАГ] Cleanup '{cleanupNode.Name}' начат.");
                    var cleanupGraph = compiler.Compile(cleanupNode.BodyGraph);
                    var result = await new TestExecutor().ExecuteAsync(
                        cleanupGraph.StartNode,
                        context,
                        cleanupCts.Token);

                    if (result == ExecutionStatus.Completed)
                    {
                        cleanupSucceeded = true;
                        TestingLogger.Info($"[OK] Cleanup '{cleanupNode.Name}' завершён.");
                    }
                    else
                    {
                        TestingLogger.Warning($"[ОШИБКА] Cleanup '{cleanupNode.Name}' завершился с результатом {result}.");
                    }
                }
                catch (OperationCanceledException)
                {
                    TestingLogger.Warning($"[ОШИБКА] Cleanup '{cleanupNode.Name}' прерван по таймауту.");
                    continue;
                }
                catch (Exception ex)
                {
                    DiagnosticLog.WriteException($"Завершение: {cleanupNode.Name}", ex);
                    TestingLogger.Error($"Не удалось выполнить «{cleanupNode.Name}». Проверьте состояние стенда.");
                }
                finally { allSucceeded &= cleanupSucceeded; Station.FinishCleanup(cleanupNode, cleanupSucceeded); }
            }
        }
        finally
        {
            context.CancellationToken = previousToken;
            context.WaitIfPausedAsync = previousPause;
            context.SkipFirstRunNodes = previousSkipFirstRunNodes;
            context.IsCleanup = previousCleanup;
        }
        context.SetVariable("Cleanup.Succeeded", allSucceeded);
        return allSucceeded;
    }

    private static TaskCompletionSource<bool> CreateCompletedPauseCompletion()
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult(true);
        return completion;
    }

    internal async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        // Capture this gate: Resume must release exactly the wait that observed Pause.
        var gate = _pauseCompletion;
        if (!gate.Task.IsCompleted)
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ReferenceEquals(gate, _pauseCompletion)) RefreshEffectivePause();
            });
        await gate.Task.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void PauseTest()
    {
        if (!CanPauseTest)
            return;

        _pauseCompletion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        IsTestPaused = true;
        RefreshEffectivePause();
        StatusMessage = "Ожидание завершения текущих операций перед паузой.";
        TestingLogger.Info("[ПАУЗА] Запрошена пауза перед следующей операцией.");
    }

    private void ResumeTest()
    {
        if (!CanResumeTest)
            return;

        IsTestPaused = false;
        IsPauseEffective = false;
        _pauseCompletion.TrySetResult(true);
        StatusMessage = "Выполнение теста продолжено.";
        TestingLogger.Info("[ПАУЗА] Выполнение теста продолжено.");
    }

    private void StopTest()
    {
        if (!IsTestRunning || IsStopping)
            return;

        IsStopping = true;
        Station.RequestStop();
        _testRunCts?.Cancel();
        _pauseCompletion.TrySetResult(true);
        IsTestPaused = false;
        IsPauseEffective = false;
        StatusMessage = "Остановка теста...";
        TestingLogger.Warning("[ОСТАНОВ] Запрошена остановка выполнения теста.");
    }

    public async Task NodeStartedAsync(
        TestNode node,
        TestContext context,
        CancellationToken cancellationToken)
    {
        if (node.Source is not NodeViewModel nodeViewModel)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _executionUi.NodeStarted(nodeViewModel, context.ExecutionScopeId, context.ParentExecutionScopeId);
            RefreshEffectivePause();
            UpdateBoardState(nodeViewModel, "Проверяется");
            Station.NodeStarted(nodeViewModel, context);
        });
    }

    public async Task NodeSkippedAsync(TestNode node, TestContext context, CancellationToken cancellationToken)
    {
        TestingLogger.Info($"[ПРОПУСК] {node.DisplayName}: выполнено при открытии профиля.");
        if (node.Source is NodeViewModel source)
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateBoardState(source, "Не проверен");
                Station.NodeSkipped(source);
            });
    }

    public async Task NodeCompletedAsync(TestNode node, StepResult result, TestContext context,
        CancellationToken cancellationToken)
    {
        if (node.Source is NodeViewModel source)
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _executionUi.NodeCompleted(source, result);
                UpdateBoardState(source, source is SubtestNodeViewModel { IsEnabled: false } ? "Не проверен" : result == StepResult.False ? "Ошибка" : "Годен");
                Station.NodeCompleted(source, result, context);
            });
    }

    public async Task NodeFinishedAsync(
        TestNode node,
        TestContext context,
        CancellationToken cancellationToken)
    {
        if (node.Source is not NodeViewModel nodeViewModel)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_boardNodeStates.GetValueOrDefault(nodeViewModel) == "Проверяется") UpdateBoardState(nodeViewModel, "Не проверен");
            _executionUi.NodeFinished(nodeViewModel, context.ExecutionScopeId);
            Station.NodeFinished(nodeViewModel, context);
            RefreshEffectivePause();
        });
    }

    public async Task NodeFailedAsync(
        TestNode node,
        TestContext context,
        CancellationToken cancellationToken)
    {
        if (node.Source is not NodeViewModel nodeViewModel)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            nodeViewModel.HasExecutionSucceeded = false;
            nodeViewModel.HasExecutionError = true;
            UpdateBoardState(nodeViewModel, "Ошибка");
            Station.NodeFailed(nodeViewModel, context);
        });
    }

    public async Task ParallelBranchFinishedAsync(TestContext context, CancellationToken cancellationToken)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _executionUi.FinishScope(context.ExecutionScopeId);
            Station.FinishScope(context);
            RefreshEffectivePause();
        });
    }

    public async Task PauseWaitingChangedAsync(TestContext context, bool isWaiting, CancellationToken cancellationToken)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _executionUi.SetScopeWaiting(context.ExecutionScopeId, isWaiting);
            RefreshEffectivePause();
        });
    }

    private void RefreshEffectivePause() => IsPauseEffective =
        IsTestPaused && !IsStopping && _executionUi.CanPause(Station.IsAwaitingAction);

    private void ClearExecutionHighlightsRecursive(GraphWorkspaceViewModel graph, bool clearResults)
    {
        _executionUi.Clear();

        foreach (var node in graph.Nodes)
        {
            node.IsExecuting = false;

            if (clearResults)
            {
                node.HasExecutionError = false;
                node.HasExecutionSucceeded = false;
            }

            if (node is ICompositeNodeViewModel composite)
            {
                ClearExecutionHighlightsRecursive(composite.BodyGraph, clearResults);
            }

            if (node is SubtestNodeViewModel subtest)
            {
                subtest.EndProgress();
            }
        }
    }

    private void ResetConnectorsState()
    {
        ResetConnectorsState(CurrentGraph);
    }

    private static void ResetConnectorsState(GraphWorkspaceViewModel graph)
    {
        foreach (var node in graph.Nodes)
        {
            foreach (var connector in node.Input.Concat(node.Output))
            {
                connector.IsConnected = graph.Connections.Any(c =>
                    c.Source == connector ||
                    c.Target == connector);
            }
        }
    }

    private static void ResetConnectorsStateRecursive(GraphWorkspaceViewModel graph)
    {
        ResetConnectorsState(graph);

        foreach (var composite in graph.Nodes.OfType<ICompositeNodeViewModel>())
        {
            ResetConnectorsStateRecursive(composite.BodyGraph);
        }
    }

    public void RefreshProfiles()
    {
        if (!CanChooseProfile) return;
        var folder = ProfilesFolder;
        LoadEquipmentCatalog(folder);
        var previous = _profileCatalog.ToDictionary(p => p.FilePath);
        _refreshingProfiles = true;
        try
        {
            _profileCatalog.Clear();
            Profiles.Clear();
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            {
                foreach (var file in Directory.GetFiles(folder, "*.json").OrderBy(p => p))
                {
                    var info = GraphSerializer.ReadProfile(file);
                    var model = _equipmentCatalog.Resolve(info.DeviceModelId, info.DeviceModel, info.Name + " " + Path.GetFileNameWithoutExtension(file));
                    if (model != null) info = new GraphProfile(info.FilePath, info.Name, model.Name, info.ConfigurationName, model.Id, info.ProductKind, info.UpdatedAt);
                    var profile = previous.TryGetValue(file, out var old) && old.Name == info.Name &&
                        old.DeviceModelId == info.DeviceModelId && old.DeviceModel == info.DeviceModel && old.ConfigurationName == info.ConfigurationName && old.ProductKind == info.ProductKind
                        ? old : info;
                    profile.RefreshUpdatedAt(info.UpdatedAt);
                    _profileCatalog.Add(profile);
                    if (MatchesProductFilter(profile) && (string.IsNullOrWhiteSpace(ProfileSearch) ||
                        EquipmentCatalog.Matches(profile.DisplayName, ProfileSearch) ||
                        EquipmentCatalog.Matches(profile.Name, ProfileSearch) ||
                        EquipmentCatalog.Matches(profile.ModelGroup, ProfileSearch)))
                        Profiles.Add(profile);
                }
            }
            RefreshNameSuggestions();
            RefreshStationProfiles();
            RefreshEngineerProfileGroups();
            OnPropertyChanged(nameof(SelectedProfile));
        }
        finally { _refreshingProfiles = false; }
    }

    public void ShowSelectedProfileGraph()
    {
        if (!CanChooseProfile || SelectedProfile == null) return;

        if (_profileLoadError.Length > 0)
        {
            _ = SelectProfileAsync(SelectedProfile, reload: true);
            return;
        }

        // Re-selecting a loaded profile is navigation, so keep unsaved edits.
        ResetToRootGraph();
        CurrentGraphOpened?.Invoke();
    }

    private void LoadProfile(string filePath)
    {
        if (!IsAllowedProfilePath(filePath)) { StatusMessage = "Учебный профиль должен находиться в папке обучения."; return; }
        ResetPartialRun();
        _profileLoadError = string.Empty;
        ResetProfilePreparation();
        try
        {
            var json = File.ReadAllText(filePath);
            var name = GraphSerializer.Deserialize(json, this);

            ResetToRootGraph();
            ClearExecutionHighlightsRecursive(RootGraph, clearResults: true);
            ResetConnectorsStateRecursive(RootGraph);
            _history.Clear();

            _currentProfilePath = filePath;
            _savedProfileName = name.Trim();
            RefreshSelectedEquipmentModel();
            OnPropertyChanged(nameof(CurrentProfileName));

            Station.Prepare(RootGraph);
            StatusMessage = string.Empty;
            CurrentGraphOpened?.Invoke();
        }
        catch (Exception ex)
        {
            LogUiException($"Загрузка профиля '{filePath}'", ex);
            _profileLoadError = ex.Message;
            _currentProfilePath = null;
            _savedProfileName = null;
            OnPropertyChanged(nameof(CurrentProfileName));
            Station.Prepare(RootGraph);
            Station.Stages.Clear();
            Station.Headline = "Не удалось загрузить программу";
            Station.Guidance = "Выберите другой профиль или обратитесь к инженеру.";
            StatusMessage = $"Ошибка загрузки: {ex.Message}";
        }
        RefreshStationAvailability();
    }

    private void LogUiException(string operation, Exception exception)
    {
        var logPath = DiagnosticLog.WriteException(operation, exception);
        TestingLogger.Error($"{operation}: не удалось выполнить операцию.");
        if (logPath != null)
            TestingLogger.Info($"Подробности ошибки сохранены: {logPath}");
    }

    internal void ReportPortSearchFailure(string port, Exception exception, bool automatic, string? diagnosticDirectory = null)
    {
        DiagnosticLog.WriteException($"Подключение к {port}", exception, diagnosticDirectory);
        if (automatic)
            TestingLogger.Info($"На порту {port} стенд не обнаружен. Поиск продолжается.");
        else
            TestingLogger.Warning($"Не удалось открыть или проверить порт {port}. Проверьте подключение стенда и доступность порта.");
    }

    private async Task ImportProfilesAsync()
    {
        if (IsTraining) return;
        if (!CanEditGraph) return;
        if (string.IsNullOrWhiteSpace(CurrentProfileName)) { StatusMessage = "Введите название профиля."; return; }
        var folder = ProfilesFolder;

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            StatusMessage = "Укажите папку для профилей в настройках.";
            return;
        }

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

        if (topLevel == null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Импорт профилей",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("JSON профиль") { Patterns = new[] { "*.json" } }
            }
        });

        if (files.Count == 0)
            return;

        int imported = 0;
        var errors = new List<string>();

        foreach (var file in files)
        {
            var destPath = Path.Combine(folder, file.Name);

            if (File.Exists(destPath))
            {
                errors.Add($"{file.Name}: файл уже существует");
                continue;
            }

            try
            {
                await using var source = await file.OpenReadAsync();
                await using var dest = File.Create(destPath);
                await source.CopyToAsync(dest);

                imported++;
            }
            catch (Exception ex)
            {
                errors.Add($"{file.Name}: {ex.Message}");
            }
        }

        RefreshProfiles();

        if (errors.Count == 0)
        {
            StatusMessage = imported == 1
                ? "Профиль импортирован."
                : $"Импортировано профилей: {imported}.";
        }
        else if (imported == 0)
        {
            StatusMessage = string.Join(" | ", errors);
        }
        else
        {
            StatusMessage = $"Импортировано: {imported}. " + string.Join(" | ", errors);
        }
    }

    private async Task SaveGraphAsync()
    {
        if (IsActionGraphEditor || !CanEditGraph) return;
        if (string.IsNullOrWhiteSpace(CurrentProfileName)) { StatusMessage = "Введите название профиля."; return; }
        var folder = ProfilesFolder;

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            StatusMessage = "Укажите папку для профилей в настройках.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(_currentProfilePath))
        {
            if (string.Equals(CurrentProfileName.Trim(), _savedProfileName, StringComparison.Ordinal))
                await SaveGraphToPathAsync(_currentProfilePath);
            else
            {
                try
                {
                    var copyPath = ProfileFileStorage.NewCopyPath(folder, CurrentProfileName);
                    await SaveGraphToPathAsync(copyPath, createNew: true, renameConfiguration: true);
                }
                catch (Exception ex) { StatusMessage = $"Ошибка сохранения: {ex.Message}"; }
            }
            return;
        }

        try
        {
            var filePath = ProfileFileStorage.NewCopyPath(folder, CurrentProfileName);
            await SaveGraphToPathAsync(filePath, createNew: true);
        }
        catch (Exception ex) { StatusMessage = $"Ошибка сохранения: {ex.Message}"; }
    }

    internal async Task SaveGraphToPathAsync(string filePath, bool createNew = false, bool renameConfiguration = false)
    {
        if (!IsAllowedProfilePath(filePath)) { StatusMessage = "Учебные профили сохраняются только в папке обучения."; return; }
        if (!CanEditGraph || string.IsNullOrWhiteSpace(CurrentProfileName)) { StatusMessage = "Введите название профиля."; return; }
        try
        {
            RefreshSelectedEquipmentModel();
            var profileName = CurrentProfileName.Trim();
            var updatedAt = DateTimeOffset.UtcNow;
            var configurationName = renameConfiguration ? ProfileDescription : RootGraph.ConfigurationName;
            var json = GraphSerializer.Serialize(this, profileName, updatedAt, configurationName);

            await ProfileFileStorage.WriteAsync(filePath, json, createNew);

            _currentProfilePath = filePath;
            _savedProfileName = profileName;
            RootGraph.UpdatedAt = updatedAt;
            RootGraph.ConfigurationName = configurationName;
            SetProfileTitle(profileName);
            RefreshSelectedEquipmentModel();
            OnPropertyChanged(nameof(CurrentProfileName));

            StatusMessage = createNew && renameConfiguration ? $"Создана копия профиля: {profileName}" : $"Профиль сохранён: {profileName}";

            if (createNew) { _profileSearch = string.Empty; OnPropertyChanged(nameof(ProfileSearch)); }
            RefreshProfiles();
            SelectProfileByPath(filePath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка сохранения: {ex.Message}";
        }
    }

    private void SelectProfileByPath(string filePath)
    {
        var profile = _profileCatalog.FirstOrDefault(item =>
            string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

        SetSelectedProfileWithoutLoading(profile);
    }

    private void SetSelectedProfileWithoutLoading(GraphProfile? profile)
    {
        _selectedProfile = profile;
        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(SelectedStationProfile));
        UpdateStationProfileSelection();
        RefreshStationAvailability();
    }

    private void AddNode(string? nodeType)
    {
        AddNodeAtLocation(nodeType, new Point(200, 200));
    }

    public void AddNodeAtLocation(string? nodeType, Point location)
    {
        if (!CanEditGraph) return;
        if (CurrentGraph.UsesBodyBoundaryNodes && (nodeType == "Старт" || nodeType == "Конец"))
        {
            StatusMessage = "Внутри тела цикла используются Body Start и Body End. Обычные Start/End сюда добавлять не нужно.";
            return;
        }

        NodeViewModel? node = nodeType switch
        {
            "Serial Console" => new SerialConsoleNodeViewModel { Location = location },
            "Run Program" => new RunProgramNodeViewModel { Location = location },
            "Parse Response" => new ParseResponseNodeViewModel { Location = location },
            "Check LED Color" => new CheckLedColorNodeViewModel { Location = location },
            "Старт" => new StartNodeViewModel { Location = location },
            "Конец" => new EndNodeViewModel { Location = location },
            "Запись регистра" => new ModbusWriteNodeViewModel { Location = location },
            "Проверка диапазона" => new CheckRegisterRangeNodeViewModel { Location = location },
            "Задержка" => new DelayNodeViewModel { Location = location },
            "Selftest Check" => new SelfTestCheckNodeViewModel { Location = location },
            "Check Variable Equality" => new CheckVariableEqualityNodeViewModel { Location = location },
            "Check Variable Range" => new CheckVariableRangeNodeViewModel { Location = location },
            "Clear ARP Cache" => new ClearArpCacheNodeViewModel { Location = location },
            "Get Serial Number" => new GetSerialNumberFromServerNodeViewModel { Location = location },
            "Set Pro MAC" => new SetProMacNodeViewModel { Location = location },
            "Set PSW MAC (UDP)" => new SetPswMacNodeViewModel { Location = location },
            "Update PSW Firmware" => new UpdatePswFirmwareNodeViewModel { Location = location },
            "Run Data Test" => new RunDataTestNodeViewModel { Location = location },
            "Read HTTP Variable" => new ReadHttpVariableNodeViewModel { Location = location },
            "Wait Variable Until" => new WaitVariableUntilNodeViewModel { Location = location },
            "Build MAC From Serial" => new BuildMacFromSerialNodeViewModel { Location = location },
            "Compare Variables" => new CompareVariablesNodeViewModel { Location = location },
            "Build Test Report" => new BuildTestReportNodeViewModel { Location = location },
            "Print Label" => new PrintLabelNodeViewModel { Location = location },
            "Send Test Report" => new SendTestReportNodeViewModel { Location = location },
            "Метка" => new LabelNodeViewModel { Location = location },
            "Подтест" => new SubtestNodeViewModel { Location = location },
            "Цикл For" => new ForEachSlaveNodeViewModel { Location = location },
            "Ожидание значения" => new WaitUntilNodeViewModel { Location = location },
            "Действие оператора" => new OperatorActionNodeViewModel { Location = location },
            _ => null
        };

        if (node != null)
            _history.Execute(CurrentGraph, new AddNodeCommand(Nodes, node));
    }

    private void EnsureBodyBoundaryNodesIfNeeded()
    {
        if (!CurrentGraph.UsesBodyBoundaryNodes)
            return;

        if (!Nodes.Any(n => n is BodyStartNodeViewModel))
        {
            Nodes.Add(new BodyStartNodeViewModel
            {
                Location = new Point(100, 120)
            });
        }

        if (!Nodes.Any(n => n is BodyEndNodeViewModel))
        {
            Nodes.Add(new BodyEndNodeViewModel
            {
                Location = new Point(560, 120)
            });
        }
    }

    private void DisposeRegisterMonitor()
    {
        if (_registerMonitor == null)
            return;

        _registerMonitor.ConnectionLost -= OnConnectionLost;
        _registerMonitor.Dispose();
        _registerMonitor = null;
    }

    public void Dispose()
    {
        _indicatorsDisposed = true;
        _indicatorTimer?.Stop();
        CancelQuickActions();
        _slaveManager.Slaves.CollectionChanged -= OnStandDevicesChanged;
        if (_standSerialRegister != null) _standSerialRegister.PropertyChanged -= OnStandSerialChanged;
        _modbusService.ReconnectStatusChanged -= OnReconnectStatusChanged;
        CloseReconnectDialog();

        if (_currentUndoRedo != null)
            _currentUndoRedo.StateChanged -= OnUndoRedoStateChanged;
        _history.Dispose();
        DisposeRegisterMonitor();

        foreach (var node in AvailableNodes)
            node.Dispose();

        DisposeGraphNodes(RootGraph);
    }

    private static void DisposeGraphNodes(GraphWorkspaceViewModel graph)
    {
        foreach (var action in graph.QuickActions) { DisposeGraphNodes(action.Graph); DisposeGraphNodes(action.OffGraph); }
        foreach (var node in graph.Nodes)
        {
            if (node is ICompositeNodeViewModel composite)
                DisposeGraphNodes(composite.BodyGraph);

            node.Dispose();
        }
    }


}
