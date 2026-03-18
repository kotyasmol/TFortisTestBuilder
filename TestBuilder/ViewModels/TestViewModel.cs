using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels.NodifyVM;
using Avalonia;

namespace TestBuilder.ViewModels;

public partial class TestViewModel : ViewModelBase, IGraphEditor
{
    private readonly ModbusService _modbusService;
    private readonly SlaveManager _slaveManager;
    private readonly RegisterState _registerState = new();

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


    // NODIFY

    public ObservableCollection<NodeViewModel> Nodes { get; } = new();
    public ObservableCollection<ConnectionViewModel> Connections { get; } = new();
    public PendingConnectionViewModel PendingConnection { get; }
    public ICommand DisconnectConnectorCommand { get; }



    public TestViewModel(ModbusService modbusService, SlaveManager slaveManager)
    {
        _modbusService = modbusService;
        _slaveManager = slaveManager;

        TestingLogger = LoggingService.Instance.CreateLogger("Testing");

        ToggleConnectionCommand = new AsyncRelayCommand(ToggleConnectionAsync);
        Test1Command = new AsyncRelayCommand(PulseLoadSetAAsync);

        // ===== Nodify init =====
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

        InitGraph(); // 👈 важно

        StatusMessage = "Готов к подключению.";
    }

    // GRAPH INIT
    // =========================
    private void InitGraph()
    {
        var start = CreateNode("Start", 100, 100, hasOutput: true);
        var step = CreateNode("Step", 400, 150, hasInput: true, hasOutput: true);
        var end = CreateNode("End", 700, 200, hasInput: true);

        Nodes.Add(start);
        Nodes.Add(step);
        Nodes.Add(end);

        Connections.Add(new ConnectionViewModel(
            start.Output.First(),
            step.Input.First()
        ));

        Connections.Add(new ConnectionViewModel(
            step.Output.First(),
            end.Input.First()
        ));
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

    // =========================
    // CONNECT (используется Nodify)
    // =========================
    public void Connect(ConnectorViewModel source, ConnectorViewModel target)
    {
        Connections.Add(new ConnectionViewModel(source, target));
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
            TestingLogger.Info("Запись 1 в LoadSetA (1412)");

            var ok1 = await _modbusService
                .WriteRegisterAsync(TEST_SLAVE_ID, LOAD_SET_A_ADDRESS, 1);

            if (!ok1)
            {
                TestingLogger.Error("Ошибка записи 1.");
                return;
            }

            await Task.Delay(3000);

            TestingLogger.Info("Запись 0 в LoadSetA (1412)");

            var ok2 = await _modbusService
                .WriteRegisterAsync(TEST_SLAVE_ID, LOAD_SET_A_ADDRESS, 0);

            if (!ok2)
            {
                TestingLogger.Error("Ошибка записи 0.");
                return;
            }

            TestingLogger.Info("Импульс завершён.");
        }
        catch (Exception ex)
        {
            TestingLogger.Error($"Ошибка Test1: {ex.Message}");
        }
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
            TestingLogger.Error($"Ошибка переключения: {ex}");
            StatusMessage = $"Ошибка: {ex.Message}";
        }

        OnPropertyChanged(nameof(ConnectionButtonText));
    }
    private async Task ConnectAsync()
    {
        StatusMessage = "Поиск COM-портов...";
        TestingLogger.Info("Поиск COM-портов.");

        var ports = SerialPort.GetPortNames()
                              .OrderBy(p => p)
                              .ToArray();

        if (ports.Length == 0)
        {
            StatusMessage = "COM-порты не найдены.";
            return;
        }

        foreach (var port in ports)
        {
            StatusMessage = $"Пробуем {port}...";
            TestingLogger.Info($"Проверка {port}");

            try
            {
                var connected = await _modbusService
                    .ConnectAsync(port, 9600, Parity.None, 8, StopBits.One);

                if (!connected)
                    continue;

                var test = await _modbusService.ReadRegistersAsync(1, 0, 1);

                if (test != null && test.Length > 0)
                {
                    SelectedPort = port;
                    IsConnected = true;

                    StatusMessage = $"Подключено к {port}";
                    TestingLogger.Info($"Успешное подключение к {port}");

                    await StartMonitoringAsync();
                    return;
                }

                await _modbusService.DisconnectAsync();
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
        await _slaveManager.ScanAsync();

        if (_slaveManager.Slaves.Count == 0)
        {
            StatusMessage = "Слейвы не найдены.";
            return;
        }

        _registerMonitor = new RegisterMonitor(
            _slaveManager,
            _registerState,
            TestingLogger)
        {
            PollInterval = 1000
        };

        _registerMonitor.Start();

        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _monitorCts = new CancellationTokenSource();

        _ = Task.Run(() => LogLoopAsync(_monitorCts.Token));
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
    }
    private async Task LogLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var snapshot = _registerState.GetSnapshot();

                foreach (var slave in _slaveManager.Slaves)
                {
                    foreach (var reg in slave.RegisterItems)
                    {
                        if (snapshot.TryGetValue(reg.Name, out var value))
                        {
                            Dispatcher.UIThread.Post(() =>
                                TestingLogger.Debug(
                                    $"[Мониторинг] SlvID={slave.SlaveId}, " +
                                    $"Рег={reg.Name}({reg.Address}), " +
                                    $"Значение={value}"
                                ));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                    TestingLogger.Error($"Ошибка мониторинга: {ex.Message}"));
            }

            try
            {
                await Task.Delay(1000, token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
}