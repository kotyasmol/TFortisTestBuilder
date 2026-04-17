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
    public PendingConnectionViewModel PendingConnection { get; }
    public ICommand DisconnectConnectorCommand { get; }

    public TestViewModel(ModbusService modbusService, SlaveManager slaveManager)
    {
        _modbusService = modbusService;
        _slaveManager = slaveManager;

        TestingLogger = LoggingService.Instance.CreateLogger("Testing");

        ToggleConnectionCommand = new AsyncRelayCommand(ToggleConnectionAsync);
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
        var start = new StartNodeViewModel { Location = new Point(100, 100) };

        var write = new ModbusWriteNodeViewModel
        {
            Location = new Point(350, 100),
            SlaveId = 1,
            Address = 1412,
            Value = 1
        };

        var check = new CheckRegisterRangeNodeViewModel
        {
            Location = new Point(600, 100),
            SlaveId = 1,
            Address = 1412,
            Min = 1,
            Max = 1
        };

        var end = new EndNodeViewModel { Location = new Point(850, 100) };

        Nodes.Add(start);
        Nodes.Add(write);
        Nodes.Add(check);
        Nodes.Add(end);

        Connections.Add(new ConnectionViewModel(start.Output.First(), write.In));
        Connections.Add(new ConnectionViewModel(write.TrueOut, check.In));
        Connections.Add(new ConnectionViewModel(check.TrueOut, end.Input.First()));
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
        IsMonitoringActive = false; // 👈 ВЫКЛЮЧИЛИ

        StatusMessage = "Отключено.";
    }


    private async Task RunGraphAsync()
    {
        if (!IsConnected)
            return;

        try
        {
            var startNodeVm = Nodes.FirstOrDefault(n => n is StartNodeViewModel);

            if (startNodeVm == null)
                return;

            var map = new Dictionary<NodeViewModel, TestNode>();

            foreach (var node in Nodes)
            {
                map[node] = node switch
                {
                    ModbusWriteNodeViewModel write =>
                        new TestNode(write.CreateStep(_modbusService, TestingLogger)),

                    CheckRegisterRangeNodeViewModel check =>
                        new TestNode(check.CreateStep()),

                    _ => new TestNode(new PassThroughStep())
                };
            }

            foreach (var connection in Connections)
            {
                var source = connection.Source.Parent;
                var target = connection.Target.Parent;

                if (source == null || target == null)
                    continue;

                var src = map[source];
                var dst = map[target];

                if (source is ModbusWriteNodeViewModel write)
                {
                    if (connection.Source == write.TrueOut)
                        src.OnTrue = dst;
                    else if (connection.Source == write.FalseOut)
                        src.OnFalse = dst;
                }
                else if (source is CheckRegisterRangeNodeViewModel check)
                {
                    if (connection.Source == check.TrueOut)
                        src.OnTrue = dst;
                    else if (connection.Source == check.FalseOut)
                        src.OnFalse = dst;
                }
                else
                {
                    src.Next = dst;
                }
            }

            var context = new TestContext(_registerState)
            {
                CancellationToken = CancellationToken.None,
                IsConnected = IsConnected
            };

            var executor = new TestExecutor();

            await executor.ExecuteAsync(
                map[startNodeVm],
                context,
                CancellationToken.None);

            TestingLogger.Info("Граф выполнен.");
        }
        catch (Exception ex)
        {
            TestingLogger.Error(ex.ToString());
        }
    }
}