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
    private readonly ModbusService _modbusService;
    private readonly SlaveManager _slaveManager;
    private readonly RegisterState _registerState = new();
    private readonly Stack<GraphWorkspaceViewModel> _graphStack = new();

    private RegisterMonitor? _registerMonitor;
    private readonly UndoRedoManager _undoRedo = new();

    public ILogger TestingLogger { get; }

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

    public string PaletteToggleIcon => IsPaletteCollapsed ? "‹" : "›";
    public string PaletteToggleTip => IsPaletteCollapsed ? "Развернуть палитру" : "Свернуть палитру";

    public ICommand TogglePaletteCommand { get; }

    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }

    public bool CanUndo => _undoRedo.CanUndo;
    public bool CanRedo => _undoRedo.CanRedo;

    private List<NodeViewModel>? _clipboardNodes;
    private List<ConnectionViewModel>? _clipboardConnections;

    public bool CanPaste => _clipboardNodes?.Count > 0;

    public GraphWorkspaceViewModel RootGraph { get; } = new()
    {
        Title = "Основной граф",
        IsBodyGraph = false
    };

    public string ConnectionButtonText => IsConnected ? "Отключиться" : "Подключиться";

    public IAsyncRelayCommand ToggleConnectionCommand { get; }

    public IAsyncRelayCommand RunGraphCommand { get; }

    public ObservableCollection<NodeViewModel> Nodes => CurrentGraph.Nodes;

    public ObservableCollection<ConnectionViewModel> Connections => CurrentGraph.Connections;

    public ObservableCollection<NodeViewModel> SelectedNodes => CurrentGraph.SelectedNodes;

    public PendingConnectionViewModel PendingConnection { get; }

    public ICommand DisconnectConnectorCommand { get; }

    public ICommand DeleteSelectedNodesCommand { get; }

    public ICommand ClearGraphCommand { get; }

    public ICommand AddNodeCommand { get; }

    public ICommand GoBackGraphCommand { get; }

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
        new GetUpsStatusNodeViewModel(),
        new GetUpsVoltageNodeViewModel(),
        new PrintLabelNodeViewModel(),
        new SendTestReportNodeViewModel(),
        new LabelNodeViewModel(),
        new ForEachSlaveNodeViewModel(),
        new CheckRegisterEqualityNodeViewModel(),
        new WaitUntilNodeViewModel(),
        new PollRegisterNodeViewModel(),
        new OperatorActionNodeViewModel()
    };

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

        CurrentGraph = RootGraph;

        TestingLogger = LoggingService.Instance.CreateLogger("Testing");

        ToggleConnectionCommand = new AsyncRelayCommand(ToggleConnectionAsync);
        RunGraphCommand = new AsyncRelayCommand(RunGraphAsync);

        PendingConnection = new PendingConnectionViewModel(this);

        AddNodeCommand = new RelayCommand<string?>(AddNode);
        DisconnectConnectorCommand = new RelayCommand<ConnectorViewModel?>(DisconnectConnector);
        DeleteSelectedNodesCommand = new RelayCommand(DeleteSelectedNodes);
        ClearGraphCommand = new RelayCommand(ClearGraph);
        GoBackGraphCommand = new RelayCommand(GoBackGraph);
        SaveGraphCommand = new AsyncRelayCommand(SaveGraphAsync);
        LoadProfileCommand = new AsyncRelayCommand(async () => RefreshProfiles());
        ImportProfilesCommand = new AsyncRelayCommand(ImportProfilesAsync);
        TogglePaletteCommand = new RelayCommand(() => IsPaletteCollapsed = !IsPaletteCollapsed);
        UndoCommand = new RelayCommand(UndoAction, () => _undoRedo.CanUndo);
        RedoCommand = new RelayCommand(RedoAction, () => _undoRedo.CanRedo);

        _undoRedo.StateChanged += OnUndoRedoStateChanged;

        RefreshProfiles();

        StatusMessage = "Готов к подключению.";
    }

    private void OnUndoRedoStateChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        ((RelayCommand)UndoCommand).NotifyCanExecuteChanged();
        ((RelayCommand)RedoCommand).NotifyCanExecuteChanged();
    }

    private void UndoAction()
    {
        _undoRedo.Undo();
        ResetConnectorsState();
    }

    private void RedoAction()
    {
        _undoRedo.Redo();
        ResetConnectorsState();
    }

    public void CopyNodes()
    {
        var selected = SelectedNodes
            .Where(n => n is not BodyStartNodeViewModel && n is not BodyEndNodeViewModel)
            .ToList();

        if (selected.Count == 0) return;

        var selectedSet = new HashSet<NodeViewModel>(selected);

        _clipboardNodes = selected;
        _clipboardConnections = Connections
            .Where(c => selectedSet.Contains(c.Source.Parent!) && selectedSet.Contains(c.Target.Parent!))
            .ToList();

        OnPropertyChanged(nameof(CanPaste));
    }

    public void PasteNodes()
    {
        if (_clipboardNodes == null || _clipboardNodes.Count == 0) return;

        const double offset = 30;

        // Map original node → clone
        var nodeMap = new Dictionary<NodeViewModel, NodeViewModel>();
        foreach (var original in _clipboardNodes)
        {
            var clone = original.Clone();
            clone.Location = new Avalonia.Point(original.Location.X + offset, original.Location.Y + offset);
            nodeMap[original] = clone;
        }

        // Rebuild connections between cloned nodes
        var newConnections = new List<ConnectionViewModel>();
        if (_clipboardConnections != null)
        {
            foreach (var conn in _clipboardConnections)
            {
                if (!nodeMap.TryGetValue(conn.Source.Parent!, out var srcNode)) continue;
                if (!nodeMap.TryGetValue(conn.Target.Parent!, out var tgtNode)) continue;

                var srcConnector = srcNode.Output.ElementAtOrDefault(conn.Source.Parent!.Output.IndexOf(conn.Source));
                var tgtConnector = tgtNode.Input.ElementAtOrDefault(conn.Target.Parent!.Input.IndexOf(conn.Target));

                if (srcConnector != null && tgtConnector != null)
                    newConnections.Add(new ConnectionViewModel(srcConnector, tgtConnector));
            }
        }

        var pastedNodes = nodeMap.Values.ToList();

        _undoRedo.Execute(new PasteNodesCommand(Nodes, Connections, pastedNodes, newConnections));

        // Select only pasted nodes
        SelectedNodes.Clear();
        foreach (var node in pastedNodes)
        {
            node.IsSelected = true;
            SelectedNodes.Add(node);
        }

        ResetConnectorsState();
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

        CanGoBackGraph = _graphStack.Count > 0;

        PendingConnection?.Reset();
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

        _undoRedo.Execute(new DeleteConnectionCommand(Connections, connection));

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
        CurrentGraph = RootGraph;
        CanGoBackGraph = false;
        PendingConnection.Reset();
    }

    [RelayCommand]
    private void OpenCompositeNodeBody(NodeViewModel? node)
    {
        if (node is not ICompositeNodeViewModel composite)
            return;

        _graphStack.Push(CurrentGraph);
        CurrentGraph = composite.BodyGraph;
        CanGoBackGraph = true;

        StatusMessage = $"Открыто тело ноды: {node.Title}.";
    }

    private void GoBackGraph()
    {
        if (_graphStack.Count == 0)
            return;

        CurrentGraph = _graphStack.Pop();
        CanGoBackGraph = _graphStack.Count > 0;

        StatusMessage = $"Открыт граф: {CurrentGraph.Title}.";
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

    public void DeleteSelectedNodes()
    {
        SelectedConnection = null;

        var selected = SelectedNodes
            .Where(node => node is not BodyStartNodeViewModel && node is not BodyEndNodeViewModel)
            .ToList();

        if (selected.Count == 0) return;

        var removedConnections = selected
            .SelectMany(node => Connections
                .Where(c => c.Source.Parent == node || c.Target.Parent == node))
            .Distinct()
            .ToList();

        _undoRedo.Execute(new DeleteNodesCommand(Nodes, Connections, selected, removedConnections));

        SelectedNodes.Clear();
        ResetConnectorsState();
        EnsureBodyBoundaryNodesIfNeeded();
    }

    public void Connect(ConnectorViewModel source, ConnectorViewModel target)
    {
        SelectedConnection = null;

        _undoRedo.Execute(new AddConnectionCommand(Connections, new ConnectionViewModel(source, target)));
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
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            // Останавливаем тест если запущен
            IsMonitoringActive = false;
            TestingLogger.Error("[ОШИБКА] Связь потеряна. Тест остановлен.");

            // Показываем диалог
            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            var dialog = new ConnectionLostDialog();
            await dialog.ShowDialog(mainWindow);

            if (dialog.ShouldReconnect)
            {
                TestingLogger.Info("Попытка переподключения...");
                await DisconnectAsync();
                await ConnectAsync();
            }
            else
            {
                TestingLogger.Info("[ОШИБКА] Подключение разорвано.");
                await DisconnectAsync();
            }
        });
    }

    private async Task DisconnectAsync()
    {
        DisposeRegisterMonitor();

        await _modbusService.DisconnectAsync();

        IsConnected = false;
        IsMonitoringActive = false;
        StatusMessage = "Отключено.";
        TestingLogger.Info("Отключено от стенда.");
        SlaveRegistry.Instance.NotifyConnected(false);

    }

    private async Task RunGraphAsync()
    {
        if (!IsConnected)
        {
            StatusMessage = "Перед запуском графа необходимо подключиться к стенду.";
            return;
        }

        var profileName = SelectedProfile?.Name ?? "без профиля";

        TestingLogger.Info($"Запуск теста: {profileName}");

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

            var context = new TestContext(_registerState)
            {
                CancellationToken = CancellationToken.None,
                IsConnected = IsConnected,
                ProfileName = profileName,
                ExecutionObserver = this,
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
                CancellationToken.None);

            if (result != ExecutionStatus.Completed)
                TestingLogger.Warning($"[ОШИБКА] Тест завершён с ошибкой. Результат: {result}.");
        }
        catch (Exception ex)
        {
            TestingLogger.Error(ex.ToString());
        }
        finally
        {
            ClearExecutionHighlightsRecursive(RootGraph, clearErrors: false);
            ResetConnectorsStateRecursive(RootGraph);
        }
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

    private static void ClearExecutionHighlightsRecursive(GraphWorkspaceViewModel graph, bool clearErrors)
    {
        foreach (var node in graph.Nodes)
        {
            node.IsExecuting = false;

            if (clearErrors)
                node.HasExecutionError = false;

            if (node is ICompositeNodeViewModel composite)
            {
                ClearExecutionHighlightsRecursive(composite.BodyGraph, clearErrors);
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
            _undoRedo.Clear();

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
            SuggestedFileName = "profile",
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

        try
        {
            var profileName = Path.GetFileNameWithoutExtension(file.Name);
            var json = GraphSerializer.Serialize(this, profileName);

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);

            await writer.WriteAsync(json);

            StatusMessage = $"Профиль сохранён: {profileName}";

            RefreshProfiles();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка сохранения: {ex.Message}";
        }
    }

    private void AddNode(string? nodeType)
    {
        AddNodeAtLocation(nodeType, new Point(200, 200));
    }

    public void AddNodeAtLocation(string? nodeType, Point location)
    {
        if (CurrentGraph.IsBodyGraph && (nodeType == "Старт" || nodeType == "Конец"))
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
            "Print Label" => new PrintLabelNodeViewModel { Location = location },
            "Send Test Report" => new SendTestReportNodeViewModel { Location = location },
            "Метка" => new LabelNodeViewModel { Location = location },
            "Цикл For" => new ForEachSlaveNodeViewModel { Location = location },
            "Проверка равенства" => new CheckRegisterEqualityNodeViewModel { Location = location },
            "Ожидание значения" => new WaitUntilNodeViewModel { Location = location },
            "Опрос регистра" => new PollRegisterNodeViewModel { Location = location },
            "Действие оператора" => new OperatorActionNodeViewModel { Location = location },
            _ => null
        };

        if (node != null)
            _undoRedo.Execute(new AddNodeCommand(Nodes, node));
    }

    private void EnsureBodyBoundaryNodesIfNeeded()
    {
        if (!CurrentGraph.IsBodyGraph)
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
        _undoRedo.StateChanged -= OnUndoRedoStateChanged;
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
}
