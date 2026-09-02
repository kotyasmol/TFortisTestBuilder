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
    private readonly Stack<SubtestNodeViewModel> _activeSubtests = new();

    private string? _currentProfilePath;
    private RegisterMonitor? _registerMonitor;
    private UndoRedoManager? _currentUndoRedo;
    private CancellationTokenSource? _testRunCts;
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

    [ObservableProperty]
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
    public string CurrentGraphPath => string.Join(" / ", _graphPath);
    public string CurrentProfileName => string.IsNullOrWhiteSpace(_currentProfilePath)
        ? "Новый профиль"
        : Path.GetFileNameWithoutExtension(_currentProfilePath);

    public ICommand TogglePaletteCommand { get; }

    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }

    public bool CanUndo => CurrentGraph.UndoRedo.CanUndo;
    public bool CanRedo => CurrentGraph.UndoRedo.CanRedo;

    private List<ClipboardNode>? _clipboardNodes;
    private List<ClipboardConnection>? _clipboardConnections;

    public bool CanPaste => _clipboardNodes?.Count > 0;

    public GraphWorkspaceViewModel RootGraph { get; } = new()
    {
        Title = "Полный тест",
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
        new SendUdpSetMacPacketNodeViewModel(),
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
        new CheckRegisterEqualityNodeViewModel(),
        new WaitUntilNodeViewModel(),
        new PollRegisterNodeViewModel(),
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
        set
        {
            if (ReferenceEquals(_selectedProfile, value))
                return;

            _selectedProfile = value;
            OnPropertyChanged(nameof(SelectedProfile));

            if (value != null)
                LoadProfile(value.FilePath);
        }
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

    public TestViewModel(ModbusService modbusService, SlaveManager slaveManager)
    {
        _modbusService = modbusService ?? throw new ArgumentNullException(nameof(modbusService));
        _slaveManager = slaveManager ?? throw new ArgumentNullException(nameof(slaveManager));

        _graphPath.Add(RootGraph.Title);
        CurrentGraph = RootGraph;

        TestingLogger = LoggingService.Instance.CreateLogger("Testing");
        _modbusService.ReconnectStatusChanged += OnReconnectStatusChanged;

        ToggleConnectionCommand = new AsyncRelayCommand(ToggleConnectionAsync);
        RunGraphCommand = new AsyncRelayCommand(RunGraphAsync);
        PauseTestCommand = new RelayCommand(PauseTest);
        ResumeTestCommand = new RelayCommand(ResumeTest);
        StopTestCommand = new RelayCommand(StopTest);

        PendingConnection = new PendingConnectionViewModel(this);

        AddNodeCommand = new RelayCommand<string?>(AddNode);
        DisconnectConnectorCommand = new RelayCommand<ConnectorViewModel?>(DisconnectConnector);
        DeleteSelectedNodesCommand = new RelayCommand(DeleteSelectedNodes);
        ClearGraphCommand = new RelayCommand(ClearGraph);
        GoBackGraphCommand = new RelayCommand(GoBackGraph);
        NewProfileCommand = new RelayCommand(CreateNewProfile);
        SaveGraphCommand = new AsyncRelayCommand(SaveGraphAsync);
        LoadProfileCommand = new AsyncRelayCommand(async () => RefreshProfiles());
        ImportProfilesCommand = new AsyncRelayCommand(ImportProfilesAsync);
        TogglePaletteCommand = new RelayCommand(() => IsPaletteCollapsed = !IsPaletteCollapsed);
        UndoCommand = new RelayCommand(UndoAction, () => CurrentGraph.UndoRedo.CanUndo);
        RedoCommand = new RelayCommand(RedoAction, () => CurrentGraph.UndoRedo.CanRedo);

        AttachUndoRedo(CurrentGraph.UndoRedo);

        BuildNodePaletteCategories();
        RefreshProfiles();

        StatusMessage = "Готов к подключению.";
    }

    private void BuildNodePaletteCategories()
    {
        NodeViewModel Find(string title) => AvailableNodes.First(n => n.Title == title);

        AvailableNodeCategories.Clear();

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
            Find("Проверка равенства"),
            Find("Ожидание значения"),
            Find("Опрос регистра")));

        AvailableNodeCategories.Add(new NodePaletteCategoryViewModel(
            "Проверки",
            Find("Selftest Check"),
            Find("Check Variable Equality"),
            Find("Check Variable Range")));

        AvailableNodeCategories.Add(new NodePaletteCategoryViewModel(
            "HTTP и сеть",
            Find("Clear ARP Cache"),
            Find("Get Serial Number"),
            Find("Send UDP Set MAC"),
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
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));

        if (UndoCommand is RelayCommand undoCommand)
            undoCommand.NotifyCanExecuteChanged();

        if (RedoCommand is RelayCommand redoCommand)
            redoCommand.NotifyCanExecuteChanged();
    }

    private void UndoAction()
    {
        CurrentGraph.UndoRedo.Undo();
        ResetConnectorsState();
    }

    private void RedoAction()
    {
        CurrentGraph.UndoRedo.Redo();
        ResetConnectorsState();
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
    }

    public void PasteNodes()
    {
        if (_clipboardNodes == null || _clipboardNodes.Count == 0) return;

        const double offset = 30;

        var pastedNodes = new List<NodeViewModel>();

        foreach (var entry in _clipboardNodes)
        {
            var clone = CloneNodeDeep(entry.Node);
            clone.Location = new Point(entry.Location.X + offset, entry.Location.Y + offset);
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

        CurrentGraph.UndoRedo.Execute(new PasteNodesCommand(Nodes, Connections, pastedNodes, newConnections));

        // Select only pasted nodes
        SelectedNodes.Clear();
        foreach (var node in pastedNodes)
        {
            node.IsSelected = true;
            SelectedNodes.Add(node);
        }

        ResetConnectorsState();
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

        AttachUndoRedo(value.UndoRedo);
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
        if (connection == null) return;
        if (!Connections.Contains(connection)) return;

        if (ReferenceEquals(SelectedConnection, connection))
            SelectedConnection = null;

        CurrentGraph.UndoRedo.Execute(new DeleteConnectionCommand(Connections, connection));

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
        _graphPath.Add(node.Title);
        CanGoBackGraph = true;

        StatusMessage = $"Открыто тело ноды: {node.Title}.";
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

    public void ClearGraph()
    {
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
        ResetToRootGraph();

        RootGraph.Clear();
        RootGraph.Title = "Полный тест";
        RootGraph.IsBodyGraph = false;
        RootGraph.UsesBodyBoundaryNodes = false;
        _graphPath.Clear();
        _graphPath.Add(RootGraph.Title);
        OnPropertyChanged(nameof(CurrentGraphPath));

        ClearExecutionHighlightsRecursive(RootGraph, clearErrors: true);
        ClearUndoRedoRecursive(RootGraph);
        ResetConnectorsStateRecursive(RootGraph);

        _currentProfilePath = null;
        OnPropertyChanged(nameof(CurrentProfileName));
        SetSelectedProfileWithoutLoading(null);

        StatusMessage = "Создан новый пустой профиль. Нажмите «Сохранить», чтобы выбрать имя файла.";
    }

    public void DeleteSelectedNodes()
    {
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

        CurrentGraph.UndoRedo.Execute(new DeleteNodesCommand(Nodes, Connections, selected, removedConnections));

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
        SelectedConnection = null;

        CurrentGraph.UndoRedo.Execute(new AddConnectionCommand(Connections, new ConnectionViewModel(source, target)));
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
        if (IsConnected)
            await DisconnectAsync();
        else
            await ConnectAsync();

    }

    private async Task ConnectAsync()
    {
        StatusMessage = "Поиск COM-портов...";

        var ports = SerialPort.GetPortNames().OrderBy(p => p);

        foreach (var port in ports)
        {
            try
            {
                var connected = await _modbusService.ConnectAsync(
                    port,
                    9600,
                    Parity.None,
                    8,
                    StopBits.One);

                if (!connected)
                    continue;

                if (!await _modbusService.CheckPortAsync())
                {
                    await _modbusService.DisconnectAsync();
                    continue;
                }

                SelectedPort = port;
                IsConnected = true;
                StatusMessage = $"Подключено к {port}";
                TestingLogger.Info($"Подключено к {port}.");

                await StartMonitoringAsync();
                SlaveRegistry.Instance.SyncSlaves(_slaveManager.Slaves);
                SlaveRegistry.Instance.NotifyConnected(true);

                return;
            }
            catch
            {
                await _modbusService.DisconnectAsync();
            }
        }

        StatusMessage = "Не удалось подключиться.";
        TestingLogger.Error("Не удалось подключиться. Проверьте кабель и порт.");
    }

    private async Task StartMonitoringAsync()
    {
        var count = await _slaveManager.ScanAsync();

        if (count == 0)
        {
            StatusMessage = "Слейвы не найдены.";
            TestingLogger.Warning("Устройства не найдены. Проверьте подключение.");
            return;
        }

        DisposeRegisterMonitor();

        _registerMonitor = new RegisterMonitor(
            _slaveManager,
            _registerState,
            TestingLogger);

        _registerMonitor.ConnectionLost += OnConnectionLost;
        _registerMonitor.Start();

        IsMonitoringActive = true;
        StatusMessage = $"Найдено устройств: {count}";
        TestingLogger.Info($"Найдено устройств: {count}. Можно запускать тест.");
    }

    private void OnConnectionLost(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsMonitoringActive = false;
            StatusMessage = "Связь Modbus потеряна. Выполняется автоматическое восстановление.";
            TestingLogger.Warning("[MODBUS] Мониторинг обнаружил потерю связи. Автоматическое восстановление выполнится при следующей Modbus-операции.");
        });
    }

    private void OnReconnectStatusChanged(object? sender, string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
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
        DisposeRegisterMonitor();

        await _modbusService.DisconnectAsync();

        IsConnected = false;
        IsMonitoringActive = false;
        StatusMessage = "Отключено.";
        CloseReconnectDialog();
        TestingLogger.Info("Отключено от стенда.");
        SlaveRegistry.Instance.NotifyConnected(false);

    }

    private async Task RunGraphAsync()
    {
        if (IsTestRunning)
        {
            StatusMessage = "Тест уже выполняется.";
            return;
        }

        if (!IsConnected)
        {
            StatusMessage = "Перед запуском графа необходимо подключиться к стенду.";
            return;
        }

        var profileName = SelectedProfile?.Name ?? "без профиля";

        TestingLogger.Info($"Запуск теста: {profileName}");

        _testRunCts?.Dispose();
        _testRunCts = new CancellationTokenSource();
        _pauseCompletion = CreateCompletedPauseCompletion();
        IsTestRunning = true;
        IsTestPaused = false;
        SelfTestPageState.Reset();
        TestContext? context = null;
        var runCleanup = false;

        try
        {
            ResetToRootGraph();

            ClearExecutionHighlightsRecursive(RootGraph, clearErrors: true);

            var compiler = new GraphCompiler(_modbusService, TestingLogger);
            var graph = compiler.Compile(RootGraph);

            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktopLife
                    ? desktopLife.MainWindow
                    : null;

            context = new TestContext(_registerState)
            {
                CancellationToken = _testRunCts.Token,
                IsConnected = IsConnected,
                ProfileName = profileName,
                ExecutionObserver = this,
                WaitIfPausedAsync = WaitIfPausedAsync,
                SelfTestPageState = SelfTestPageState,
                OperatorPrompt = async message =>
                {
                    return await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        var dialog = new Views.OperatorActionDialog(message);
                        await dialog.ShowDialog(mainWindow);
                        return dialog.Confirmed;
                    });
                }
            };

            var result = await new TestExecutor().ExecuteAsync(
                graph.StartNode,
                context,
                _testRunCts.Token);

            if (result != ExecutionStatus.Completed)
            {
                runCleanup = true;
                context.HasCriticalError = true;
                context.SetVariable("Execution.Status", result.ToString());
                TestingLogger.Warning($"[ОШИБКА] Тест завершён с ошибкой. Результат: {result}.");
            }
            else
            {
                context.SetVariable("Execution.Status", result.ToString());
            }
        }
        catch (OperationCanceledException)
        {
            runCleanup = true;
            if (context != null)
            {
                context.HasCriticalError = true;
                context.SetVariable("Execution.Status", ExecutionStatus.Cancelled.ToString());
            }

            TestingLogger.Warning("[ОСТАНОВ] Выполнение теста остановлено пользователем.");
            StatusMessage = "Выполнение теста остановлено.";
        }
        catch (Exception ex)
        {
            runCleanup = true;
            if (context != null)
            {
                context.HasCriticalError = true;
                context.SetVariable("Execution.Status", ExecutionStatus.Failed.ToString());
                context.SetVariable("Execution.Error", ex.Message);
            }

            TestingLogger.Error(ex.ToString());
        }
        finally
        {
            if (runCleanup && context != null)
            {
                await RunFailureCleanupAsync(context);
            }

            IsTestRunning = false;
            IsTestPaused = false;
            _pauseCompletion.TrySetResult(true);
            _testRunCts?.Dispose();
            _testRunCts = null;
            ClearExecutionHighlightsRecursive(RootGraph, clearErrors: false);
            ResetConnectorsStateRecursive(RootGraph);
        }
    }

    private async Task RunFailureCleanupAsync(TestContext context)
    {
        var cleanupNodes = RootGraph.Nodes
            .OfType<SubtestNodeViewModel>()
            .Where(node => node.RunOnFailure && node.IsEnabled)
            .ToList();

        if (cleanupNodes.Count == 0)
            return;

        TestingLogger.Warning($"[ШАГ] Запуск cleanup-подтестов: {cleanupNodes.Count}.");

        using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var previousToken = context.CancellationToken;
        var previousPause = context.WaitIfPausedAsync;

        context.CancellationToken = cleanupCts.Token;
        context.WaitIfPausedAsync = _ => Task.CompletedTask;

        try
        {
            var compiler = new GraphCompiler(_modbusService, TestingLogger);

            foreach (var cleanupNode in cleanupNodes)
            {
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
                    break;
                }
                catch (Exception ex)
                {
                    TestingLogger.Error($"Cleanup '{cleanupNode.Name}' не выполнен: {ex}");
                }
            }
        }
        finally
        {
            context.CancellationToken = previousToken;
            context.WaitIfPausedAsync = previousPause;
        }
    }

    private static TaskCompletionSource<bool> CreateCompletedPauseCompletion()
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult(true);
        return completion;
    }

    private Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        return _pauseCompletion.Task.WaitAsync(cancellationToken);
    }

    private void PauseTest()
    {
        if (!IsTestRunning || IsTestPaused)
            return;

        _pauseCompletion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        IsTestPaused = true;
        StatusMessage = "Выполнение теста на паузе.";
        TestingLogger.Info("[ПАУЗА] Выполнение теста поставлено на паузу.");
    }

    private void ResumeTest()
    {
        if (!IsTestRunning || !IsTestPaused)
            return;

        IsTestPaused = false;
        _pauseCompletion.TrySetResult(true);
        StatusMessage = "Выполнение теста продолжено.";
        TestingLogger.Info("[ПАУЗА] Выполнение теста продолжено.");
    }

    private void StopTest()
    {
        if (!IsTestRunning)
            return;

        _testRunCts?.Cancel();
        _pauseCompletion.TrySetResult(true);
        IsTestPaused = false;
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
            if (_activeSubtests.TryPeek(out var activeSubtest) &&
                !ReferenceEquals(activeSubtest, nodeViewModel))
            {
                activeSubtest.UpdateProgress(nodeViewModel);
            }

            if (nodeViewModel is SubtestNodeViewModel subtest)
            {
                subtest.BeginProgress();
                _activeSubtests.Push(subtest);
            }

            nodeViewModel.IsExecuting = true;
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
            nodeViewModel.IsExecuting = false;

            if (nodeViewModel is SubtestNodeViewModel subtest)
            {
                if (_activeSubtests.TryPeek(out var activeSubtest) &&
                    ReferenceEquals(activeSubtest, subtest))
                {
                    _activeSubtests.Pop();
                }

                subtest.EndProgress();
            }
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
            nodeViewModel.HasExecutionError = true;
        });
    }

    private void ClearExecutionHighlightsRecursive(GraphWorkspaceViewModel graph, bool clearErrors)
    {
        _activeSubtests.Clear();

        foreach (var node in graph.Nodes)
        {
            node.IsExecuting = false;

            if (clearErrors)
                node.HasExecutionError = false;

            if (node is ICompositeNodeViewModel composite)
            {
                ClearExecutionHighlightsRecursive(composite.BodyGraph, clearErrors);
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
        var folder = AppSettings.Instance.GraphsFolder;

        Profiles.Clear();

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        foreach (var file in Directory.GetFiles(folder, "*.json"))
        {
            var name = GraphSerializer.ReadProfileName(file) ?? Path.GetFileNameWithoutExtension(file);

            if (!string.IsNullOrWhiteSpace(ProfileSearch) &&
                !name.Contains(ProfileSearch, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Profiles.Add(new GraphProfile(file, name));
        }
    }

    private void LoadProfile(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var name = GraphSerializer.Deserialize(json, this);

            ResetToRootGraph();
            ClearExecutionHighlightsRecursive(RootGraph, clearErrors: true);
            ResetConnectorsStateRecursive(RootGraph);
            ClearUndoRedoRecursive(RootGraph);

            _currentProfilePath = filePath;
            OnPropertyChanged(nameof(CurrentProfileName));

            StatusMessage = $"Загружен профиль: {name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка загрузки: {ex.Message}";
        }
    }

    private async Task ImportProfilesAsync()
    {
        var folder = AppSettings.Instance.GraphsFolder;

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
        var folder = AppSettings.Instance.GraphsFolder;

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            StatusMessage = "Укажите папку для профилей в настройках.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(_currentProfilePath))
        {
            await SaveGraphToPathAsync(_currentProfilePath);
            return;
        }

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

        if (topLevel == null)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить профиль",
            DefaultExtension = "json",
            SuggestedFileName = CurrentProfileName == "Новый профиль" ? "profile" : CurrentProfileName,
            SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(folder),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("JSON профиль")
                {
                    Patterns = new[] { "*.json" }
                }
            }
        });

        if (file == null)
            return;

        if (file.Path.IsFile)
        {
            await SaveGraphToPathAsync(file.Path.LocalPath);
            return;
        }

        try
        {
            var profileName = Path.GetFileNameWithoutExtension(file.Name);
            var json = GraphSerializer.Serialize(this, profileName);

            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek)
                stream.SetLength(0);

            await using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);

            await writer.WriteAsync(json);

            StatusMessage = $"Профиль сохранён: {profileName}";

            RefreshProfiles();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка сохранения: {ex.Message}";
        }
    }

    private async Task SaveGraphToPathAsync(string filePath)
    {
        try
        {
            var profileName = Path.GetFileNameWithoutExtension(filePath);
            var json = GraphSerializer.Serialize(this, profileName);

            await File.WriteAllTextAsync(filePath, json, System.Text.Encoding.UTF8);

            _currentProfilePath = filePath;
            OnPropertyChanged(nameof(CurrentProfileName));

            StatusMessage = $"Профиль сохранён: {profileName}";

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
        var profile = Profiles.FirstOrDefault(item =>
            string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

        SetSelectedProfileWithoutLoading(profile);
    }

    private void SetSelectedProfileWithoutLoading(GraphProfile? profile)
    {
        _selectedProfile = profile;
        OnPropertyChanged(nameof(SelectedProfile));
    }

    private void AddNode(string? nodeType)
    {
        AddNodeAtLocation(nodeType, new Point(200, 200));
    }

    public void AddNodeAtLocation(string? nodeType, Point location)
    {
        if (CurrentGraph.UsesBodyBoundaryNodes && (nodeType == "Старт" || nodeType == "Конец"))
        {
            StatusMessage = "Внутри тела цикла используются Body Start и Body End. Обычные Start/End сюда добавлять не нужно.";
            return;
        }

        NodeViewModel? node = nodeType switch
        {
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
            "Send UDP Set MAC" => new SendUdpSetMacPacketNodeViewModel { Location = location },
            "Run Data Test" => new RunDataTestNodeViewModel { Location = location },
            "Get UPS Status" => new GetUpsStatusNodeViewModel { Location = location },
            "Get UPS Voltage" => new GetUpsVoltageNodeViewModel { Location = location },
            "Get IRP Status" => new GetIrpStatusNodeViewModel { Location = location },
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
            "Проверка равенства" => new CheckRegisterEqualityNodeViewModel { Location = location },
            "Ожидание значения" => new WaitUntilNodeViewModel { Location = location },
            "Опрос регистра" => new PollRegisterNodeViewModel { Location = location },
            "Действие оператора" => new OperatorActionNodeViewModel { Location = location },
            _ => null
        };

        if (node != null)
            CurrentGraph.UndoRedo.Execute(new AddNodeCommand(Nodes, node));
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
        _modbusService.ReconnectStatusChanged -= OnReconnectStatusChanged;
        CloseReconnectDialog();

        if (_currentUndoRedo != null)
            _currentUndoRedo.StateChanged -= OnUndoRedoStateChanged;
        DisposeRegisterMonitor();

        foreach (var node in AvailableNodes)
            node.Dispose();

        DisposeGraphNodes(RootGraph);
    }

    private static void DisposeGraphNodes(GraphWorkspaceViewModel graph)
    {
        foreach (var node in graph.Nodes)
        {
            if (node is ICompositeNodeViewModel composite)
                DisposeGraphNodes(composite.BodyGraph);

            node.Dispose();
        }
    }

    private static void ClearUndoRedoRecursive(GraphWorkspaceViewModel graph)
    {
        graph.UndoRedo.Clear();

        foreach (var composite in graph.Nodes.OfType<ICompositeNodeViewModel>())
            ClearUndoRedoRecursive(composite.BodyGraph);
    }
}
