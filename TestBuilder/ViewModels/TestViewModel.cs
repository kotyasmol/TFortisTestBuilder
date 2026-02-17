using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;

namespace TestBuilder.ViewModels
{
    public partial class TestViewModel : ViewModelBase
    {
        private readonly ModbusService _modbusService = new();
        private readonly SlaveManager _slaveManager;
        private readonly RegisterState _registerState = new();
        private RegisterMonitor? _registerMonitor;
        private CancellationTokenSource? _monitorLogCts;

        public ILogger TestingLogger { get; }

        [ObservableProperty]
        private bool isConnected;

        [ObservableProperty]
        private string? statusMessage;

        [ObservableProperty]
        private string? selectedPort;

        public string ConnectionButtonText => IsConnected ? "Отключиться" : "Подключиться";

        public IAsyncRelayCommand ToggleConnectionCommand { get; }

        public TestViewModel()
        {
            IsConnected = false;
            _slaveManager = new SlaveManager(_modbusService);
            TestingLogger = LoggingService.Instance.CreateLogger("Testing");

            ToggleConnectionCommand = new AsyncRelayCommand(ToggleConnectionAsync);

            StatusMessage = "Поиск доступных COM-портов...";
        }

        private async Task ToggleConnectionAsync()
        {
            try
            {
                if (IsConnected)
                {
                    await DisconnectAsync();
                }
                else
                {
                    await ConnectAsync();
                }
            }
            catch (Exception ex)
            {
                TestingLogger.Error($"Ошибка при переключении: {ex.Message}");
                StatusMessage = $"Ошибка: {ex.Message}";
            }
            finally
            {
                OnPropertyChanged(nameof(ConnectionButtonText));
            }
        }

        private async Task ConnectAsync()
        {
            StatusMessage = "Инициализация поиска COM-портов...";
            TestingLogger.Info("Запуск поиска COM-портов для подключения к Modbus-устройствам.");

            var ports = SerialPort.GetPortNames().OrderBy(p => p).ToArray();
            if (ports.Length == 0)
            {
                StatusMessage = "COM-порты не обнаружены.";
                TestingLogger.Warning("Не найдено ни одного COM-порта.");
                return;
            }

            foreach (var port in ports)
            {
                StatusMessage = $"Попытка подключения к порту {port}...";
                TestingLogger.Info($"Проверка порта {port}.");

                try
                {
                    bool ok = await _modbusService.ConnectAsync(port, 9600, Parity.None, 8, StopBits.One);
                    if (!ok)
                    {
                        TestingLogger.Warning($"Не удалось подключиться к порту {port}.");
                        await _modbusService.DisconnectAsync();
                        continue;
                    }

                    var values = await _modbusService.ReadRegistersAsync(1, 0, 1);
                    if (values != null && values.Length > 0)
                    {
                        SelectedPort = port;
                        IsConnected = true;
                        StatusMessage = $"Подключение успешно к {port}.";
                        TestingLogger.Info($"Успешное подключение к {port}.");

                        await _slaveManager.ScanAsync();

                        if (_slaveManager.Slaves.Count > 0)
                        {
                            _registerMonitor = new RegisterMonitor(_slaveManager, _registerState, TestingLogger)
                            {
                                PollInterval = 1000
                            };
                            _registerMonitor.Start();

                            _monitorLogCts?.Cancel();
                            _monitorLogCts = new CancellationTokenSource();
                            _ = Task.Run(() => LogLoopAsync(_monitorLogCts.Token));
                        }
                        else
                        {
                            StatusMessage = "Слейвы не обнаружены.";
                            TestingLogger.Warning("Слейвы не обнаружены.");
                        }

                        return;
                    }

                    await _modbusService.DisconnectAsync();
                    TestingLogger.Warning($"Порт {port} не ответил корректно.");
                }
                catch (Exception ex)
                {
                    TestingLogger.Error($"Ошибка при подключении к {port}: {ex.Message}");
                    await _modbusService.DisconnectAsync();
                }
            }

            StatusMessage = "Не удалось подключиться ни к одному порту.";
        }

        private async Task DisconnectAsync()
        {
            StatusMessage = "Отключение от Modbus...";
            _monitorLogCts?.Cancel();
            _registerMonitor?.Stop();
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
                                // обновляем лог через UI-диспетчер
                                Dispatcher.UIThread.Post(() =>
                                    TestingLogger.Debug($"[Мониторинг] SlvID={slave.SlaveId}, Рег={reg.Name}({reg.Address}), Значение={value}")
                                );
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() =>
                        TestingLogger.Error($"Ошибка в LogLoop: {ex.Message}"));
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
}