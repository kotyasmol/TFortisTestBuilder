using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Modbus;


namespace TestBuilder.Services.Modbus
{
    /// <summary>
    /// Сервис поллинга Modbus-устройств.
    /// Асинхронно опрашивает все слейвы в SlaveManager и обновляет их состояния.
    /// </summary>
    public class ModbusPollingService
    {
        private readonly SlaveManager _slaveManager;
        private CancellationTokenSource _cts;

        public ModbusPollingService(SlaveManager slaveManager)
        {
            _slaveManager = slaveManager;
        }

        /// <summary>
        /// Запуск поллинга.
        /// </summary>
        public void Start()
        {
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => LoopAsync(_cts.Token));
        }

        /// <summary>
        /// Остановка поллинга.
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
        }

        private async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                foreach (var slave in _slaveManager.Slaves)
                {
                    try
                    {
                        await slave.PollAsync();
                    }
                    catch
                    {
                        // Игнорируем ошибки чтения, например timeout
                    }
                }

                await Task.Delay(1000, token); // пауза между циклами поллинга
            }
        }
    }
}
