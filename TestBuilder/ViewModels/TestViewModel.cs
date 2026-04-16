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

namespace TestBuilder.ViewModels;

public partial class TestViewModel : ViewModelBase, IGraphEditor
{
    private readonly ModbusService _modbusService;
    private readonly SlaveManager _slaveManager;
    private readonly RegisterState _registerState = new();
    private readonly Action? _onSlavesFound;
    private readonly Action? _onSlavesLost;

    private RegisterMonitor? _registerMonitor;
    private CancellationTokenSource? _monitorCts;

    public ILogger TestingLogger { get; }

    private const byte TEST_SLAVE_ID = 1;
    private const ushort LOAD_SET_A_ADDRESS = 1412;

    [ObservableProperty]
    private bool isConnected;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? selectedPort;

    public string ConnectionButtonText =>
        IsConnected ? "Отключиться" : "Подключиться";

    public IAsyncRelayCommand ToggleConnectionCommand { get; }
    public IAsyncRelayCommand Test1Command { get; }
    public IAsyncRelayCommand RunGraphCommand { get; }

    // NODIFY
    public ObservableCollection<NodeViewModel> Nodes { get; } = new();
    public ObservableCollection<ConnectionViewModel> Connections { get; } = new();
    public PendingConnectionViewModel PendingConnection { get; }
    public ICommand DisconnectConnectorCommand { get; }

    [ObservableProperty]
    private Avalonia.Point location;

    public TestViewModel(ModbusService modbusService, SlaveManager slaveManager, Action? onSlavesFound = null, Action? onSlavesLost = null)
    {
        _modbusService = modbusService;
        _slaveManager = slaveManager;
        _onSlavesFound = onSlavesFound;
        _onSlavesLost = onSlavesLost;

        TestingLogger = LoggingService.Instance.CreateLogger("Testing");

        ToggleConnectionCommand = new AsyncRelayCommand(ToggleConnectionAsync);
        Test1Command = new AsyncRelayCommand(PulseLoadSetAAsync);
        RunGraphCommand = new AsyncRelayCommand(RunGraphAsync);

        PendingConnection = new PendingConnectionViewModel(this);

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

        InitGraph();
        StatusMessage = "Готов к подключению.";
    }

    private void InitGraph()
    {
        var start = CreateNode("Start", 100, 100, hasOutput: true);

        var step = new ModbusWriteNodeViewModel
        {
            Location = new Point(400, 150),
            SlaveId = 1,
            Address = 1412,
            Value = 1
        };

        var end = CreateNode("End", 700, 200, hasInput: true);

        Nodes.Add(start);
        Nodes.Add(step);
        Nodes.Add(end);

        Connections.Add(new ConnectionViewModel(start.Output.First(), step.Input.First()));
        Connections.Add(new ConnectionViewModel(step.Output.First(), end.Input.First()));
    }

    private NodeViewModel CreateNode(string title, double x, double y, bool hasInput = false, bool hasOutput = false)
    {
        var node = new NodeViewModel
        {
            Title = title,
            Location = new Point(x, y)
        };

        if (hasInput)
            node.Input.Add(new ConnectorViewModel { Title = "In" });

        if (hasOutput)
            node.Output.Add(new ConnectorViewModel { Title = "Out" });

        return node;
    }

    public void Connect(ConnectorViewModel source, ConnectorViewModel target)
    {
        Connections.Add(new ConnectionViewModel(source, target));
    }

    private async Task ToggleConnectionAsync()
    {
        try
        {
            if (IsConnected)
                await DisconnectAsync();
            else
                await ConnectAsync();
        }
        catch (Exception ex)
        {
            TestingLogger.Error(ex.ToString());
            StatusMessage = ex.Message;
        }

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
            catch (Exception ex)
            {
                TestingLogger.Error(ex.Message);
                await _modbusService.DisconnectAsync();
            }
        }

        StatusMessage = "Не удалось подключиться.";
    }

    private async Task DisconnectAsync()
    {
        StatusMessage = "Отключение...";

        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _monitorCts = null;

        _registerMonitor?.Stop();
        _registerMonitor = null;

        await _modbusService.DisconnectAsync();

        IsConnected = false;
        StatusMessage = "Отключено.";
        _onSlavesLost?.Invoke();
    }

    private async Task StartMonitoringAsync()
    {
        int count = await _slaveManager.ScanAsync();

        if (count == 0)
        {
            StatusMessage = "Слейвы не найдены.";
            TestingLogger.Warning("Слейвы не найдены.");
            return;
        }

        StatusMessage = $"Найдено устройств: {count}. Можно начинать тестирование.";
        TestingLogger.Info($"Найдено {count} устройств.");
        _onSlavesFound?.Invoke();

        _registerMonitor = new RegisterMonitor(_slaveManager, _registerState, TestingLogger)
        {
            PollInterval = 1000
        };

        _registerMonitor.Start();
    }

    private async Task RunGraphAsync()
    {
        if (!IsConnected)
        {
            TestingLogger.Warning("Нет подключения.");
            return;
        }

        try
        {
            var startNodeVm = Nodes.FirstOrDefault(n =>
                !Connections.Any(c => c.Target == n.Input.FirstOrDefault()));

            if (startNodeVm == null)
            {
                TestingLogger.Error("Стартовая нода не найдена.");
                return;
            }

            var map = new Dictionary<NodeViewModel, TestNode>();

            foreach (var node in Nodes)
            {
                map[node] = node switch
                {
                    ModbusWriteNodeViewModel writeNode => new TestNode(writeNode.CreateStep(_modbusService)),
                    _ => new TestNode(null)
                };
            }

            foreach (var node in Nodes)
            {
                var testNode = map[node];

                var connection = Connections.FirstOrDefault(c =>
                    c.Source == node.Output.FirstOrDefault());

                if (connection != null)
                {
                    var nextNodeVm = Nodes.FirstOrDefault(n =>
                        n.Input.Contains(connection.Target));

                    if (nextNodeVm != null)
                        testNode.Next = map[nextNodeVm];
                }
            }

            var executor = new TestExecutor();

            await executor.ExecuteAsync(
                map[startNodeVm],
                new TestContext { CancellationToken = CancellationToken.None },
                CancellationToken.None);

            TestingLogger.Info("Граф выполнен.");
        }
        catch (Exception ex)
        {
            TestingLogger.Error(ex.ToString());
        }
    }

    private async Task PulseLoadSetAAsync()
    {
        if (!IsConnected)
        {
            TestingLogger.Warning("Нет подключения.");
            return;
        }

        try
        {
            await _modbusService.WriteRegisterAsync(TEST_SLAVE_ID, LOAD_SET_A_ADDRESS, 1);
            await Task.Delay(3000);
            await _modbusService.WriteRegisterAsync(TEST_SLAVE_ID, LOAD_SET_A_ADDRESS, 0);

            TestingLogger.Info("Импульс завершён.");
        }
        catch (Exception ex)
        {
            TestingLogger.Error(ex.ToString());
        }
    }
}