using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Services;
using TestBuilder.Serialization;
using Avalonia.Platform.Storage;
using System.IO;
using System.Text.Json;

namespace TestBuilder.ViewModels;

public partial class TestViewModel : ViewModelBase, IGraphEditor
{
    private readonly ModbusService _modbusService;
    private readonly SlaveManager _slaveManager;
    private readonly RegisterState _registerState = new();

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

    public string ConnectionButtonText =>
        IsConnected ? "Отключиться" : "Подключиться";

    public IAsyncRelayCommand ToggleConnectionCommand { get; }
    public IAsyncRelayCommand RunGraphCommand { get; }

    // Nodify
    public ObservableCollection<NodeViewModel> Nodes { get; } = new();
    public ObservableCollection<ConnectionViewModel> Connections { get; } = new();
    public ObservableCollection<NodeViewModel> SelectedNodes { get; } = new();
    public PendingConnectionViewModel PendingConnection { get; }
    public ICommand DisconnectConnectorCommand { get; }
    public ICommand DeleteSelectedNodesCommand { get; }
    public ICommand ClearGraphCommand { get; }

    // Доступные ноды для панели drag-and-drop
    public ObservableCollection<NodeViewModel> AvailableNodes { get; } = new()
    {
        new StartNodeViewModel(),
        new EndNodeViewModel(),
        new ModbusWriteNodeViewModel(),
        new CheckRegisterRangeNodeViewModel(),
        new DelayNodeViewModel(),
        new LabelNodeViewModel()
    };

    public ICommand AddNodeCommand { get; }
    public IAsyncRelayCommand SaveGraphCommand { get; }
    public IAsyncRelayCommand LoadProfileCommand { get; }

    // Список профилей
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

    public TestViewModel(ModbusService modbusService, SlaveManager slaveManager)
    {
        _modbusService = modbusService;
        _slaveManager = slaveManager;

        TestingLogger = LoggingService.Instance.CreateLogger("Testing");

        ToggleConnectionCommand = new AsyncRelayCommand(ToggleConnectionAsync);
        RunGraphCommand = new AsyncRelayCommand(RunGraphAsync);

        PendingConnection = new PendingConnectionViewModel(this);
        AddNodeCommand = new RelayCommand<string>(AddNode);

        DisconnectConnectorCommand = new RelayCommand<ConnectorViewModel>(connector =>
        {
            var connection = Connections.FirstOrDefault(x =>
                x.Source == connector || x.Target == connector);

            if (connection != null)
            {
                connection.Source.IsConnected = false;
                connection.Target.IsConnected = false;
                Connections.Remove(connection);
            }
        });

        DeleteSelectedNodesCommand = new RelayCommand(DeleteSelectedNodes);
        ClearGraphCommand = new RelayCommand(ClearGraph);
        SaveGraphCommand = new AsyncRelayCommand(SaveGraphAsync);
        LoadProfileCommand = new AsyncRelayCommand(async () => RefreshProfiles());

        RefreshProfiles();

        StatusMessage = "Готов к подключению.";
    }

    // Полная очистка холста — выделяем все ноды и удаляем через DeleteSelectedNodes
    // чтобы Nodify корректно обновил UI (прямая очистка коллекций ломает состояние)
    public void ClearGraph()
    {
        // Выделяем все ноды
        foreach (var node in Nodes)
        {
            if (!SelectedNodes.Contains(node))
                SelectedNodes.Add(node);
        }

        // Удаляем через стандартный механизм
        DeleteSelectedNodes();

        // Сбрасываем незавершённое соединение если было
        PendingConnection.Reset();
    }

    // Удаление выделенных нод с правильным сбросом IsConnected на оставшихся
    public void DeleteSelectedNodes()
    {
        var selected = SelectedNodes.ToList();

        foreach (var node in selected)
        {
            // Удаляем все соединения связанные с этой нодой
            var toRemove = Connections
                .Where(c => c.Source.Parent == node || c.Target.Parent == node)
                .ToList();

            foreach (var conn in toRemove)
            {
                conn.Source.IsConnected = false;
                conn.Target.IsConnected = false;
                Connections.Remove(conn);
            }

            Nodes.Remove(node);
        }

        SelectedNodes.Clear();

        // Пересчитываем IsConnected для оставшихся нод
        // чтобы освободить коннекторы у которых больше нет соединений
        foreach (var node in Nodes)
        {
            foreach (var connector in node.Input.Concat(node.Output))
            {
                connector.IsConnected = Connections.Any(c =>
                    c.Source == connector || c.Target == connector);
            }
        }
    }

    public void Connect(ConnectorViewModel source, ConnectorViewModel target)
    {
        Connections.Add(new ConnectionViewModel(source, target));
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
                var connected = await _modbusService
                    .ConnectAsync(port, 9600, Parity.None, 8, StopBits.One);

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

                await StartMonitoringAsync();
                return;
            }
            catch
            {
                await _modbusService.DisconnectAsync();
            }
        }

        StatusMessage = "Не удалось подключиться.";
    }

    private async Task StartMonitoringAsync()
    {
        int count = await _slaveManager.ScanAsync();

        if (count == 0)
        {
            StatusMessage = "Слейвы не найдены.";
            return;
        }

        _registerMonitor = new RegisterMonitor(_slaveManager, _registerState, TestingLogger);
        _registerMonitor.Start();

        IsMonitoringActive = true;

        StatusMessage = $"Найдено устройств: {count}";
    }

    private async Task DisconnectAsync()
    {
        _monitorCts?.Cancel();
        _registerMonitor?.Stop();

        await _modbusService.DisconnectAsync();

        IsConnected = false;
        IsMonitoringActive = false;

        StatusMessage = "Отключено.";
    }

    private async Task RunGraphAsync()
    {
        if (!IsConnected)
            return;

        var profileName = SelectedProfile?.Name ?? "без профиля";
        TestingLogger.Info($"Запуск графа: {profileName}");
        try
        {
            var startNodeVm = Nodes.FirstOrDefault(n => n is StartNodeViewModel);

            if (startNodeVm == null)
            {
                TestingLogger.Warning("Нет ноды Start на холсте!");
                return;
            }

            // Шаг 1: создаём TestNode для каждой ноды на холсте
            var map = new Dictionary<NodeViewModel, TestNode>();

            foreach (var node in Nodes)
            {
                map[node] = node switch
                {
                    StartNodeViewModel start =>
                        new TestNode(start.CreateStep(TestingLogger)),

                    EndNodeViewModel end =>
                        new TestNode(end.CreateStep(TestingLogger)),

                    DelayNodeViewModel delay =>
                        new TestNode(delay.CreateStep(TestingLogger)),

                    LabelNodeViewModel label =>
                        new TestNode(label.CreateStep(TestingLogger)),

                    ModbusWriteNodeViewModel write =>
                        new TestNode(write.CreateStep(_modbusService, TestingLogger)),

                    CheckRegisterRangeNodeViewModel check =>
                        new TestNode(check.CreateStep(TestingLogger)),

                    _ => new TestNode(new PassThroughStep())
                };
            }

            // Шаг 2: строим переходы между нодами по соединениям
            foreach (var connection in Connections)
            {
                var sourceVm = connection.Source.Parent;
                var targetVm = connection.Target.Parent;

                if (sourceVm == null || targetVm == null)
                    continue;

                var src = map[sourceVm];
                var dst = map[targetVm];

                // Ветвящиеся ноды: смотрим на конкретный выходной коннектор (True/False)
                if (sourceVm is ModbusWriteNodeViewModel writeVm)
                {
                    if (connection.Source == writeVm.TrueOut)
                        src.OnTrue = dst;
                    else if (connection.Source == writeVm.FalseOut)
                        src.OnFalse = dst;
                }
                else if (sourceVm is CheckRegisterRangeNodeViewModel checkVm)
                {
                    if (connection.Source == checkVm.TrueOut)
                        src.OnTrue = dst;
                    else if (connection.Source == checkVm.FalseOut)
                        src.OnFalse = dst;
                }
                // Линейные ноды (Start, End, Delay, Label): всегда Next
                else
                {
                    src.Next = dst;
                }
            }

            // Шаг 3: запускаем граф начиная со Start
            var context = new TestContext(_registerState)
            {
                CancellationToken = CancellationToken.None,
                IsConnected = IsConnected
            };

            await new TestExecutor().ExecuteAsync(
                map[startNodeVm],
                context,
                CancellationToken.None);

            TestingLogger.Info("Граф выполнен.");
        }
        catch (Exception ex)
        {
            TestingLogger.Error(ex.ToString());
        }
        finally
        {
            // Сбрасываем состояние коннекторов после выполнения графа
            // чтобы можно было строить новый граф без перезапуска
            ResetConnectorsState();
        }
    }

    // Пересчитываем IsConnected на всех коннекторах по текущим соединениям
    private void ResetConnectorsState()
    {
        foreach (var node in Nodes)
        {
            foreach (var connector in node.Input.Concat(node.Output))
            {
                connector.IsConnected = Connections.Any(c =>
                    c.Source == connector || c.Target == connector);
            }
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

            // Фильтрация по поиску
            if (!string.IsNullOrWhiteSpace(ProfileSearch) &&
                !name.Contains(ProfileSearch, StringComparison.OrdinalIgnoreCase))
                continue;

            Profiles.Add(new GraphProfile(file, name));
        }
    }

    private void LoadProfile(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var name = GraphSerializer.Deserialize(json, this);
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

        // Запрашиваем имя профиля через простой диалог ввода
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (topLevel == null) return;

        // Показываем диалог сохранения только для имени файла
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить профиль",
            DefaultExtension = "json",
            SuggestedFileName = "profile",
            SuggestedStartLocation = await topLevel.StorageProvider
                .TryGetFolderFromPathAsync(folder),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("JSON профиль") { Patterns = new[] { "*.json" } }
            }
        });

        if (file == null) return;

        try
        {
            // Имя профиля = имя файла без расширения
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
        NodeViewModel? node = nodeType switch
        {
            "Start" => new StartNodeViewModel { Location = location },
            "End" => new EndNodeViewModel { Location = location },
            "Write Register" => new ModbusWriteNodeViewModel { Location = location },
            "Check Register Range" => new CheckRegisterRangeNodeViewModel { Location = location },
            "Delay" => new DelayNodeViewModel { Location = location },
            "Label" => new LabelNodeViewModel { Location = location },
            _ => null
        };

        if (node != null)
            Nodes.Add(node);
    }
}