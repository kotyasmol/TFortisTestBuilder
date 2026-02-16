using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Modbus; 
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Monitoring
{
    /// <summary>
    /// Параллельный мониторинг регистров Modbus.
    /// Запускается в фоне, обновляет RegisterState.
    /// </summary>
    public class RegisterMonitor : IDisposable
    {
        private readonly SlaveManager _slaveManager;       // Менеджер устройств/слейвов
        private readonly RegisterState _registerState;     // Общий state для теста
        private readonly ILogger _logger;                  // Логгер для мониторинга
        private CancellationTokenSource _cts;
        private Task _monitorTask;

        /// <summary>
        /// Интервал опроса в миллисекундах.
        /// </summary>
        public int PollInterval { get; set; } = 1000;

        public RegisterMonitor(SlaveManager slaveManager, RegisterState registerState, ILogger logger)
        {
            _slaveManager = slaveManager ?? throw new ArgumentNullException(nameof(slaveManager));
            _registerState = registerState ?? throw new ArgumentNullException(nameof(registerState));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Запуск мониторинга в фоне.
        /// </summary>
        public void Start(CancellationToken externalToken = default)
        {
            if (_monitorTask != null && !_monitorTask.IsCompleted)
                throw new InvalidOperationException("Мониторинг уже запущен.");

            _cts = externalToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(externalToken)
                : new CancellationTokenSource();

            _logger.Info($"Запуск мониторинга регистров (PollInterval={PollInterval} мс)");
            _monitorTask = Task.Run(() => MonitorLoop(_cts.Token));
        }

        /// <summary>
        /// Остановка мониторинга.
        /// </summary>
        public void Stop()
        {
            if (_cts == null)
                return;

            _logger.Info("Остановка мониторинга регистров (отмена токена).");

            // Отмена без блокирующего ожидания завершения задачи,
            // чтобы не блокировать UI‑поток.
            _cts.Cancel();
            _cts = null;
        }

        /// <summary>
        /// Основной цикл мониторинга.
        /// </summary>
        private async Task MonitorLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Делаем снимок списка слейвов, чтобы не зависеть от изменений коллекции.
                    var slavesSnapshot = new System.Collections.Generic.List<TestBuilder.Domain.Modbus.Models.SlaveModelBase>(_slaveManager.Slaves);

                    // Параллельный опрос всех слейвов: каждый слейв в своей задаче.
                    var tasks = new System.Collections.Generic.List<Task>(slavesSnapshot.Count);

                    foreach (var slave in slavesSnapshot)
                    {
                        tasks.Add(PollSingleSlaveAsync(slave, token));
                    }

                    await Task.WhenAll(tasks);
                }
                catch (TaskCanceledException)
                {
                    // Корректное завершение по отмене.
                    _logger.Info("Цикл мониторинга остановлен по отмене (TaskCanceledException).");
                    break;
                }
                catch (Exception ex)
                {
                    // Общий цикл не должен падать даже при непредвиденной ошибке.
                    _logger.Error($"Необработанная ошибка в цикле мониторинга: {ex.Message}");
                }

                try
                {
                    await Task.Delay(PollInterval, token);
                }
                catch (TaskCanceledException)
                {
                    _logger.Info("Цикл мониторинга прерван во время ожидания интервала (TaskCanceledException).");
                    break;
                }
            }
        }

        /// <summary>
        /// Опрос одного слейва с обработкой ошибок и логированием.
        /// Выполняется в отдельной задаче для обеспечения параллелизма.
        /// </summary>
        private async Task PollSingleSlaveAsync(TestBuilder.Domain.Modbus.Models.SlaveModelBase slave, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                // Опрашиваем регистры устройства (1000–1018 для EL‑60 и подобных моделей).
                await slave.PollAsync();

                // Обновляем RegisterState и логируем успешные чтения.
                foreach (var reg in slave.RegisterItems)
                {
                    _registerState.Update(reg.Name, reg.Value);
                    _logger.Debug($"Read OK: Slave {slave.SlaveId}, Reg {reg.Name} (Addr={reg.Address}) = {reg.Value}");
                }
            }
            catch (TaskCanceledException)
            {
                // Отмена не считается ошибкой.
                _logger.Info($"Опрос слейва {slave.SlaveId} отменён (TaskCanceledException).");
            }
            catch (TimeoutException ex)
            {
                // Таймаут чтения – отдельный тип ошибки.
                _logger.Warning($"Timeout при опросе слейва {slave.SlaveId}: {ex.Message}");
            }
            catch (Exception ex)
            {
                // Ошибка связи с конкретным устройством не должна падать весь мониторинг.
                _logger.Error($"Ошибка опроса слейва {slave.SlaveId}: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
