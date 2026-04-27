using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Threading;

using TestBuilder.Domain.Modbus;

namespace TestBuilder.Services.Modbus
{
    /// <summary>
    /// Сервис поллинга Modbus-устройств.
    /// Опрашивает устройства по схеме round-robin: один тик — одно устройство.
    /// </summary>
    public class ModbusPollingService
    {
        private readonly SlaveManager _slaveManager;

        private readonly object _sync = new();

        private CancellationTokenSource? _cts;
        private Task? _loopTask;

        private int _currentIndex;

        private const int DelayBetweenPollsMs = 1000;
        private const int DelayWhenNoSlavesMs = 1000;

        public ModbusPollingService(SlaveManager slaveManager)
        {
            _slaveManager = slaveManager ?? throw new ArgumentNullException(nameof(slaveManager));
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_loopTask is { IsCompleted: false } && _cts is { IsCancellationRequested: false })
                    return;

                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                _loopTask = Task.Run(() => LoopAsync(_cts.Token));
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (_cts == null)
                    return;

                _cts.Cancel();
            }
        }

        private async Task LoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var slaves = await Dispatcher.UIThread.InvokeAsync(
                        () => _slaveManager.Slaves.ToArray()
                    );

                    if (slaves.Length == 0)
                    {
                        await Task.Delay(DelayWhenNoSlavesMs, token);
                        continue;
                    }

                    if (_currentIndex >= slaves.Length)
                        _currentIndex = 0;

                    var slave = slaves[_currentIndex];
                    _currentIndex++;

                    try
                    {
                        await slave.PollAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[MODBUS POLL ERROR] slave={slave.SlaveId}, type={slave.DeviceType}, " +
                            $"{ex.GetType().Name}: {ex.Message}");
                    }

                    await Task.Delay(DelayBetweenPollsMs, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Нормальная остановка поллинга.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MODBUS POLL LOOP ERROR] {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}