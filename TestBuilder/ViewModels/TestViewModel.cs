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
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.ViewModels;

public partial class TestViewModel : ViewModelBase, IGraphEditor, IExecutionObserver
{
    private readonly ModbusService _modbusService;
    private readonly SlaveManager _slaveManager;
    private readonly RegisterState _registerState = new();
    private readonly Stack<GraphWorkspaceViewModel> _graphStack = new();

    private RegisterMonitor? _registerMonitor;
    private CancellationTokenSource? _monitorCts;

    public ILogger TestingLogger { get; }

    [ObservableProperty]
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

    public ObservableCollection<NodeViewModel> AvailableNodes { get; } = new()
    {
        new StartNodeViewModel(),
        new EndNodeViewModel(),
        new ModbusWriteNodeViewModel(),
        new CheckRegisterRangeNodeViewModel(),
        new DelayNodeViewModel(),
        new HttpRequestNodeViewModel(),
        new LabelNodeViewModel(),
        new ForEachSlaveNodeViewModel()
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
        _modbusService = modbusService;
        _slaveManager = slaveManager;

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

        RefreshProfiles();

        StatusMessage = "Готов к подключению.";
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
        if (connection == null)
            return;

        if (!Connections.Contains(connection))
            return;

        RemoveConnection(connection);

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

        foreach (var node in selected)
        {
            var toRemove = Connections
                .Where(c => c.Source.Parent == node || c.Target.Parent == node)
                .ToList();

            foreach (var conn in toRemove)
            {
                RemoveConnection(conn);
            }

            Nodes.Remove(node);
        }

        SelectedNodes.Clear();

        ResetConnectorsState();

        EnsureBodyBoundaryNodesIfNeeded();
    }

    public void Connect(ConnectorViewModel source, ConnectorViewModel target)
    {
        SelectedConnection = null;

        Connections.Add(new ConnectionViewModel(source, target));
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

        OnPropertyChanged(nameof(ConnectionButtonText));
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

        _registerMonitor = new RegisterMonitor(
            _slaveManager,
            _registerState,
            TestingLogger);

        _registerMonitor.Start();

        IsMonitoringActive = true;
        StatusMessage = $"Найдено устройств: {count}";
        TestingLogger.Info($"Найдено устройств: {count}. Можно запускать тест.");
    }

    private async Task DisconnectAsync()
    {
        _monitorCts?.Cancel();

        _registerMonitor?.Stop();

        await _modbusService.DisconnectAsync();

        IsConnected = false;
        IsMonitoringActive = false;
        StatusMessage = "Отключено.";
        TestingLogger.Info("Отключено от стенда.");
        SlaveRegistry.Instance.NotifyConnected(false);

        OnPropertyChanged(nameof(ConnectionButtonText));
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

            ClearExecutionHighlightsRecursive(RootGraph);

            var compiler = new GraphCompiler(_modbusService, TestingLogger);
            var graph = compiler.Compile(RootGraph);

            var context = new TestContext(_registerState)
            {
                CancellationToken = CancellationToken.None,
                IsConnected = IsConnected,
                ProfileName = profileName,
                ExecutionObserver = this
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
            ClearExecutionHighlightsRecursive(RootGraph);
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

    private static void ClearExecutionHighlightsRecursive(GraphWorkspaceViewModel graph)
    {
        foreach (var node in graph.Nodes)
        {
            node.IsExecuting = false;

            if (node is ICompositeNodeViewModel composite)
            {
                ClearExecutionHighlightsRecursive(composite.BodyGraph);
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
            ClearExecutionHighlightsRecursive(RootGraph);
            ResetConnectorsStateRecursive(RootGraph);

            StatusMessage = $"Загружен профиль: {name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка загрузки: {ex.Message}";
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
            "HTTP Request" => new HttpRequestNodeViewModel { Location = location },
            "Метка" => new LabelNodeViewModel { Location = location },
            "Цикл For" => new ForEachSlaveNodeViewModel { Location = location },
            _ => null
        };

        if (node != null)
            Nodes.Add(node);
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
}