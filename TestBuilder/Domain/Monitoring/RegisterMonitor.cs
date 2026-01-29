using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Modbus; // Здесь будет твой ModbusService/SlaveManager

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
        private CancellationTokenSource _cts;
        private Task _monitorTask;

        /// <summary>
        /// Интервал опроса в миллисекундах.
        /// </summary>
        public int PollInterval { get; set; } = 1000;

        public RegisterMonitor(SlaveManager slaveManager, RegisterState registerState)
        {
            _slaveManager = slaveManager ?? throw new ArgumentNullException(nameof(slaveManager));
            _registerState = registerState ?? throw new ArgumentNullException(nameof(registerState));
        }

        /// <summary>
        /// Запуск мониторинга в фоне.
        /// </summary>
        public void Start()
        {
            if (_monitorTask != null && !_monitorTask.IsCompleted)
                throw new InvalidOperationException("Мониторинг уже запущен.");

            _cts = new CancellationTokenSource();
            _monitorTask = Task.Run(() => MonitorLoop(_cts.Token));
        }

        /// <summary>
        /// Остановка мониторинга.
        /// </summary>
        public void Stop()
        {
            if (_cts == null) return;

            _cts.Cancel();
            try { _monitorTask?.Wait(500); } catch { }
            _cts.Dispose();
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
                    foreach (var slave in _slaveManager.Slaves)
                    {
                        try
                        {
                            // Опрашиваем регистры устройства
                            await slave.PollAsync();

                            // Обновляем RegisterState для каждого регистра
                            foreach (var reg in slave.RegisterItems)
                            {
                                _registerState.Update(reg.Name, reg.Value);
                            }
                        }
                        catch (Exception ex)
                        {
                            // Ошибка связи с устройством
                            // Можно логировать или ставить флаг критической ошибки
                            Console.WriteLine($"[Monitor] Ошибка опроса {slave.SlaveId}: {ex.Message}");
                        }
                    }
                }
                catch { /* общий цикл не должен падать */ }

                await Task.Delay(PollInterval, token);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
