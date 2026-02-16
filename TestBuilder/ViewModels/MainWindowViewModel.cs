using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodify;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;

namespace TestBuilder.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly ModbusService _modbusService = new();
        private readonly SlaveManager _slaveManager;
        private readonly RegisterState _registerState = new();
        private RegisterMonitor _registerMonitor;
        private CancellationTokenSource _monitorLogCts;

        public ObservableCollection<NodeViewModel> Nodes { get; } = new();
        public ObservableCollection<ConnectionViewModel> Connections { get; } = new();

        public ObservableCollection<string> AvailablePorts { get; } = new();

        // Логгер для вкладки "ТЕСТИРОВАНИЕ"
        public ILogger TestingLogger { get; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        private string? selectedPort;

        // Скорость и формат кадра

        [ObservableProperty]
        private int baudRate = 9600;

        [ObservableProperty]
        private Parity parity = Parity.None;

        [ObservableProperty]
        private int dataBits = 8;

        [ObservableProperty]
        private StopBits stopBits = StopBits.One;

        // Индексы для ComboBox'ов в UI
        [ObservableProperty]
        private int parityIndex;

        [ObservableProperty]
        private int stopBitsIndex;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
        private bool isConnecting;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
        private bool isConnected;

        [ObservableProperty]
        private string? statusMessage;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartMonitoringCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopMonitoringCommand))]
        private bool isMonitoring;

        public PendingConnectionViewModel PendingConnection { get; }

        public IAsyncRelayCommand RefreshPortsCommand { get; }
        public IAsyncRelayCommand ConnectCommand { get; }
        public IAsyncRelayCommand DisconnectCommand { get; }
        public IAsyncRelayCommand StartMonitoringCommand { get; }
        public IAsyncRelayCommand StopMonitoringCommand { get; }


        public MainWindowViewModel()
        {
            PendingConnection = new PendingConnectionViewModel(this);

            _slaveManager = new SlaveManager(_modbusService);

            TestingLogger = LoggingService.Instance.CreateLogger("Testing");
            TestingLogger.Info("Инициализация вкладки ТЕСТИРОВАНИЕ");

            RefreshPortsCommand = new AsyncRelayCommand(RefreshPortsAsync);
            // Всегда разрешаем нажатие кнопок, а проверки делаем внутри методов
            ConnectCommand = new AsyncRelayCommand(ConnectAsync);
            DisconnectCommand = new AsyncRelayCommand(DisconnectAsync);
            StartMonitoringCommand = new AsyncRelayCommand(StartMonitoringAsync);
            StopMonitoringCommand = new AsyncRelayCommand(StopMonitoringAsync);

            // Инициализируем список доступных COM-портов и пробуем автоподключиться
            _ = InitializeAsync();

            var node1 = new NodeViewModel
            {
                Title = "Welcome",
                Input = { new ConnectorViewModel { Title = "In" } },
                Output = { new ConnectorViewModel { Title = "Out" } }
            };

            var node2 = new NodeViewModel
            {
                Title = "To Nodify",
                Input = { new ConnectorViewModel { Title = "In" } }
            };

            Nodes.Add(node1);
            Nodes.Add(node2);
        }

        public void Connect(ConnectorViewModel source, ConnectorViewModel target)
        {
            Connections.Add(new ConnectionViewModel(source, target));
        }

        private async Task InitializeAsync()
        {
            await RefreshPortsAsync();
            await AutoConnectAsync();
        }

        private async Task RefreshPortsAsync()
        {
            TestingLogger.Info("Сканирование доступных COM‑портов...");

            AvailablePorts.Clear();

            foreach (var port in SerialPort.GetPortNames().OrderBy(p => p))
            {
                AvailablePorts.Add(port);
            }

            if (SelectedPort is null)
            {
                SelectedPort = AvailablePorts.FirstOrDefault();
            }

            StatusMessage = AvailablePorts.Count == 0
                ? "COM‑порты не найдены"
                : "Выберите порт и параметры, затем подключитесь";

            if (AvailablePorts.Count == 0)
            {
                TestingLogger.Warning("COM‑порты не найдены");
            }
            else
            {
                TestingLogger.Info($"Найдено COM‑портов: {AvailablePorts.Count}");
            }
        }

        private bool CanConnect()
        {
            return !IsConnected
                   && !IsConnecting
                   && !string.IsNullOrWhiteSpace(SelectedPort);
        }

        private async Task ConnectAsync()
        {
            if (!CanConnect())
                return;

            IsConnecting = true;
            StatusMessage = $"Подключение к {SelectedPort}...";

            TestingLogger.Info($"Подключение к {SelectedPort} (BaudRate={BaudRate}, DataBits={DataBits}, Parity={Parity}, StopBits={StopBits})");

            var ok = await _modbusService.ConnectAsync(SelectedPort!, BaudRate, Parity, DataBits, StopBits);

            IsConnecting = false;
            IsConnected = ok;

            StatusMessage = ok
                ? $"Подключено к {SelectedPort}"
                : $"Ошибка подключения: {_modbusService.LastError}";

            if (ok)
            {
                TestingLogger.Info($"Успешное подключение к {SelectedPort}");
                // После успешного подключения сразу сканируем слейвы и запускаем мониторинг,
                // как это делалось в предыдущем проекте.
                await StartMonitoringAsync();
            }
            else
            {
                TestingLogger.Error($"Ошибка подключения к {SelectedPort}: {_modbusService.LastError}");
            }
        }

        private async Task DisconnectAsync()
        {
            if (!IsConnected && !IsConnecting)
                return;

            StatusMessage = "Отключение...";
            TestingLogger.Info("Отключение от Modbus");

            await StopMonitoringAsync();
            await _modbusService.DisconnectAsync();

            IsConnected = false;
            IsConnecting = false;
            StatusMessage = "Отключено";
        }

        private async Task StartMonitoringAsync()
        {
            if (!IsConnected || IsMonitoring)
                return;

            StatusMessage = "Сканирование устройств и запуск мониторинга...";
            TestingLogger.Info("Сканирование устройств перед запуском мониторинга");

            // Сканируем слейвы
            await _slaveManager.ScanAsync();

            if (_slaveManager.Slaves.Count == 0)
            {
                StatusMessage = "Устройства не найдены, мониторинг не запущен";
                TestingLogger.Warning("Устройства не найдены, мониторинг не запущен");
                return;
            }

            // Создаем и запускаем доменный монитор регистров, если он еще не создан
            _registerMonitor ??= new RegisterMonitor(_slaveManager, _registerState, TestingLogger)
            {
                PollInterval = 1000
            };

            _registerMonitor.Start();

            // Запускаем отдельный логирующий цикл, который раз в секунду читает значения
            // из RegisterState и пишет их в лог.
            _monitorLogCts?.Cancel();
            _monitorLogCts = new CancellationTokenSource();
            IsMonitoring = true;

            TestingLogger.Info($"Найдено слейвов: {_slaveManager.Slaves.Count}. Запуск мониторинга и логирования регистров.");

            _ = Task.Run(() => LogLoopAsync(_monitorLogCts.Token));
        }

        private async Task StopMonitoringAsync()
        {
            if (!IsMonitoring)
                return;

            _monitorLogCts?.Cancel();
            _registerMonitor?.Stop();
            IsMonitoring = false;

            StatusMessage = "Мониторинг остановлен";
            TestingLogger.Info("Мониторинг остановлен");

            // Небольшая пауза, чтобы фоновые задачи успели завершиться
            await Task.Delay(50);
        }

        /// <summary>
        /// Цикл логирования: раз в секунду считывает значения из RegisterState
        /// и пишет в лог первые 13 регистров каждого слейва.
        /// Сам опрос устройств выполняет RegisterMonitor.
        /// </summary>
        private async Task LogLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                foreach (var slave in _slaveManager.Slaves)
                {
                    // Делаем снимок интересующих регистров по именам
                    var items = slave.RegisterItems;
                    var count = items.Count < 13 ? items.Count : 13;

                    if (count == 0)
                        continue;

                    var snapshot = _registerState.GetSnapshot();

                    Dispatcher.UIThread.Post(() =>
                    {
                        for (int i = 0; i < count; i++)
                        {
                            var reg = items[i];
                            if (snapshot.TryGetValue(reg.Name, out var value))
                            {
                                TestingLogger.Debug($"Slave {slave.SlaveId} {reg.Name}({reg.Address}) = {value}");
                            }
                        }
                    });
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

        /// <summary>
        /// Автоматическое подключение: перебирает доступные COM-порты и
        /// пытается прочитать 1 регистр (slaveId=1, address=0).
        /// При первом успешном ответе соединение считается установленным.
        /// </summary>
        private async Task AutoConnectAsync()
        {
            if (IsConnected || AvailablePorts.Count == 0)
                return;

            IsConnecting = true;
            StatusMessage = "Автоподключение к Modbus...";

            TestingLogger.Info("Запуск автоподключения к Modbus");

            const byte testSlaveId = 1;
            const ushort testAddress = 0;

            foreach (var port in AvailablePorts)
            {
                try
                {
                    StatusMessage = $"Пробуем порт {port}...";

                    TestingLogger.Debug($"Пробуем порт {port}");

                    var ok = await _modbusService.ConnectAsync(port, BaudRate, Parity, DataBits, StopBits);
                    if (!ok)
                    {
                        TestingLogger.Warning($"Не удалось подключиться к порту {port}: {_modbusService.LastError}");
                        await _modbusService.DisconnectAsync();
                        continue;
                    }

                    // Пробуем прочитать один регистр. Если есть ответ – считаем, что соединение рабочее.
                    var values = await _modbusService.ReadRegistersAsync(testSlaveId, testAddress, 1);
                    if (values is { Length: > 0 })
                    {
                        SelectedPort = port;
                        IsConnected = true;
                        StatusMessage = $"Автоматически подключено к {port} (Slave {testSlaveId}, Addr {testAddress})";
                        TestingLogger.Info($"Автоматически подключено к {port} (Slave {testSlaveId}, Addr {testAddress})");
                        // Аналогично старому проекту: как только найден рабочий порт,
                        // сканируем устройства и запускаем фоновой мониторинг.
                        await StartMonitoringAsync();
                        IsConnecting = false;
                        return;
                    }

                    await _modbusService.DisconnectAsync();
                }
                catch
                {
                    TestingLogger.Error($"Ошибка при попытке автоподключения к порту {port}");
                    await _modbusService.DisconnectAsync();
                }
            }

            IsConnecting = false;
            IsConnected = false;
            StatusMessage = "Автоподключение не удалось: устройства не отвечают";
            TestingLogger.Warning("Автоподключение не удалось: устройства не отвечают");
        }

        partial void OnParityIndexChanged(int value)
        {
            Parity = value switch
            {
                0 => System.IO.Ports.Parity.None,
                1 => System.IO.Ports.Parity.Odd,
                2 => System.IO.Ports.Parity.Even,
                _ => System.IO.Ports.Parity.None
            };
        }

        partial void OnStopBitsIndexChanged(int value)
        {
            StopBits = value switch
            {
                0 => System.IO.Ports.StopBits.One,
                1 => System.IO.Ports.StopBits.Two,
                _ => System.IO.Ports.StopBits.One
            };
        }
    }
}
