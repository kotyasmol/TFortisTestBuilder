using Modbus.Device;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Services.Modbus
{
    public class ModbusService : IModbusService, IDisposable
    {
        private sealed record ConnectionSettings(
            string Port,
            int BaudRate,
            Parity Parity,
            int DataBits,
            StopBits StopBits);

        private readonly SemaphoreSlim _ioLock = new(1, 1);

        private readonly TimeSpan _minRequestGap = TimeSpan.FromMilliseconds(150);
        private readonly TimeSpan _reconnectTimeout = TimeSpan.FromMinutes(1);
        private readonly TimeSpan _reconnectPollInterval = TimeSpan.FromSeconds(1);
        private DateTime _lastRequestTimeUtc = DateTime.MinValue;

        private SerialPort? _serialPort;
        private IModbusSerialMaster? _master;
        private ConnectionSettings? _connectionSettings;

        private readonly ConcurrentDictionary<(byte slaveId, ushort address), List<Action<ushort[]>>> _watchers = new();

        private bool _isConnected;

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (_isConnected == value)
                    return;

                _isConnected = value;
                IsConnectedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? IsConnectedChanged;

        public event EventHandler<string>? ReconnectStatusChanged;

        public string? LastError { get; private set; }

        #region CONNECT

        public async Task<bool> ConnectAsync(
            string port,
            int baudRate,
            Parity parity,
            int dataBits,
            StopBits stopBits)
        {
            try
            {
                await DisconnectAsync();

                return await Task.Run(() =>
                {
                    try
                    {
                        const int timeoutMs = 1000;

                        var serialPort = new SerialPort(port, baudRate, parity, dataBits, stopBits)
                        {
                            ReadTimeout = timeoutMs,
                            WriteTimeout = timeoutMs
                        };

                        serialPort.Open();

                        try
                        {
                            serialPort.DiscardInBuffer();
                            serialPort.DiscardOutBuffer();
                        }
                        catch
                        {
                            // ignore
                        }

                        var master = CreateMaster(serialPort, timeoutMs);

                        _serialPort = serialPort;
                        _master = master;
                        _connectionSettings = new ConnectionSettings(port, baudRate, parity, dataBits, stopBits);
                        _lastRequestTimeUtc = DateTime.MinValue;

                        IsConnected = true;
                        LastError = null;

                        return true;
                    }
                    catch (Exception ex)
                    {
                        LastError = ex.Message;
                        IsConnected = false;
                        return false;
                    }
                });
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                IsConnected = false;
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            await _ioLock.WaitAsync();

            try
            {
                _master?.Dispose();
                _serialPort?.Close();
                _serialPort?.Dispose();

                _master = null;
                _serialPort = null;
                _connectionSettings = null;

                IsConnected = false;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        #endregion

        #region INTERFACE METHODS

        public async Task<bool> CheckPortAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ReadRegistersAsync(1, 0, 1, cancellationToken);
                return result.Length == 1;
            }
            catch
            {
                return false;
            }
        }

        public void SubscribeRegister(byte slaveId, ushort address, Action<ushort[]> callback)
        {
            var key = (slaveId, address);

            _watchers.AddOrUpdate(
                key,
                _ => new List<Action<ushort[]>> { callback },
                (_, list) =>
                {
                    lock (list)
                    {
                        list.Add(callback);
                    }

                    return list;
                });
        }

        #endregion

        #region DIRECT IO

        public async Task<ushort[]> ReadRegistersAsync(
            byte slaveId,
            ushort address,
            ushort count,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteWithRecoveryAsync(
                $"read slave={slaveId}, address={address}, count={count}",
                async master =>
                {
                    var result = await master.ReadHoldingRegistersAsync(slaveId, address, count);
                    NotifyWatchers(slaveId, address, result);
                    return result;
                },
                cancellationToken);
        }

        public async Task<bool> WriteRegisterAsync(
            byte slaveId,
            ushort address,
            ushort value,
            bool verify = true,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteWithRecoveryAsync(
                $"write slave={slaveId}, address={address}, value={value}",
                async master =>
                {
                    await master.WriteSingleRegisterAsync(slaveId, address, value);

                    _lastRequestTimeUtc = DateTime.UtcNow;

                    if (!verify)
                        return true;

                    await WaitBusGapAsync(cancellationToken);

                    var read = await master.ReadHoldingRegistersAsync(slaveId, address, 1);

                    NotifyWatchers(slaveId, address, read);

                    return read[0] == value;
                },
                cancellationToken);
        }

        #endregion

        #region BUS HELPERS

        private async Task<T> ExecuteWithRecoveryAsync<T>(
            string operationName,
            Func<IModbusSerialMaster, Task<T>> operation,
            CancellationToken cancellationToken)
        {
            await _ioLock.WaitAsync(cancellationToken);

            try
            {
                var master = await GetReadyMasterAsync(operationName, cancellationToken);

                try
                {
                    await WaitBusGapAsync(cancellationToken);
                    var result = await operation(master);

                    LastError = null;

                    return result;
                }
                catch (Exception ex) when (IsCriticalConnectionError(ex))
                {
                    LastError = ex.Message;

                    Console.WriteLine(
                        $"[MODBUS CRITICAL ERROR] operation={operationName}, " +
                        $"{ex.GetType().Name}: {ex.Message}");

                    await ReconnectAfterCriticalErrorAsync(ex, cancellationToken);

                    master = await GetReadyMasterAsync(operationName, cancellationToken);
                    await WaitBusGapAsync(cancellationToken);

                    var retryResult = await operation(master);
                    LastError = null;
                    Console.WriteLine(
                        $"[MODBUS RECOVERED] operation={operationName} completed after reconnect.");

                    return retryResult;
                }
            }
            finally
            {
                _lastRequestTimeUtc = DateTime.UtcNow;
                _ioLock.Release();
            }
        }

        private async Task<IModbusSerialMaster> GetReadyMasterAsync(
            string operationName,
            CancellationToken cancellationToken)
        {
            var master = _master;
            var serialPort = _serialPort;

            if (master != null &&
                serialPort != null &&
                serialPort.IsOpen &&
                IsConfiguredPortPresent())
            {
                return master;
            }

            var reason = master == null
                ? "Modbus master не создан"
                : serialPort == null
                    ? "SerialPort не создан"
                    : !serialPort.IsOpen
                        ? "COM-порт закрыт"
                        : "COM-порт отсутствует в системе";

            await ReconnectAfterCriticalErrorAsync(
                new InvalidOperationException($"{reason}. Операция: {operationName}."),
                cancellationToken);

            master = _master;

            if (master == null || _serialPort == null || !_serialPort.IsOpen)
                throw new InvalidOperationException("Не удалось восстановить Modbus-соединение.");

            return master;
        }

        private async Task ReconnectAfterCriticalErrorAsync(
            Exception cause,
            CancellationToken cancellationToken)
        {
            var settings = _connectionSettings;

            if (settings == null)
                throw new InvalidOperationException(
                    "Modbus-соединение повреждено, но параметры предыдущего подключения неизвестны.",
                    cause);

            IsConnected = false;

            NotifyReconnectStatus(
                $"Потеряно Modbus-соединение с {settings.Port}. Выполняется автоматическое переподключение.");

            DisposeCurrentConnection();

            var deadline = DateTime.UtcNow + _reconnectTimeout;
            var attempt = 0;

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempt++;

                if (!IsPortPresent(settings.Port))
                {
                    NotifyReconnectStatus(
                        $"COM-порт {settings.Port} не найден. Ожидание подключения... ({attempt})");
                    await Task.Delay(_reconnectPollInterval, cancellationToken);
                    continue;
                }

                try
                {
                    OpenConnection(settings);
                    LastError = null;
                    IsConnected = true;
                    NotifyReconnectStatus(
                        $"Modbus-соединение с {settings.Port} восстановлено. Тест продолжается.");
                    return;
                }
                catch (Exception ex) when (IsCriticalConnectionError(ex))
                {
                    LastError = ex.Message;
                    DisposeCurrentConnection();
                    NotifyReconnectStatus(
                        $"Не удалось открыть {settings.Port}: {ex.Message}. Повторная попытка...");
                    await Task.Delay(_reconnectPollInterval, cancellationToken);
                }
            }

            var message =
                $"Не удалось восстановить Modbus-соединение с {settings.Port} в течение 60 секунд. " +
                "Остановите стенд и проверьте состояние кабеля.";

            LastError = message;
            IsConnected = false;
            NotifyReconnectStatus(message);
            throw new IOException(message, cause);
        }

        private void OpenConnection(ConnectionSettings settings)
        {
            const int timeoutMs = 1000;

            var serialPort = new SerialPort(
                settings.Port,
                settings.BaudRate,
                settings.Parity,
                settings.DataBits,
                settings.StopBits)
            {
                ReadTimeout = timeoutMs,
                WriteTimeout = timeoutMs
            };

            serialPort.Open();
            serialPort.DiscardInBuffer();
            serialPort.DiscardOutBuffer();

            _serialPort = serialPort;
            _master = CreateMaster(serialPort, timeoutMs);
            _lastRequestTimeUtc = DateTime.MinValue;
        }

        private static IModbusSerialMaster CreateMaster(SerialPort serialPort, int timeoutMs)
        {
            var master = ModbusSerialMaster.CreateRtu(serialPort);
            master.Transport.ReadTimeout = timeoutMs;
            master.Transport.WriteTimeout = timeoutMs;
            master.Transport.Retries = 1;
            return master;
        }

        private void DisposeCurrentConnection()
        {
            try
            {
                _master?.Dispose();
            }
            catch
            {
                // ignore cleanup errors
            }

            try
            {
                if (_serialPort?.IsOpen == true)
                    _serialPort.Close();
            }
            catch
            {
                // ignore cleanup errors
            }

            try
            {
                _serialPort?.Dispose();
            }
            catch
            {
                // ignore cleanup errors
            }

            _master = null;
            _serialPort = null;
            _lastRequestTimeUtc = DateTime.MinValue;
        }

        private bool IsConfiguredPortPresent() =>
            _connectionSettings != null && IsPortPresent(_connectionSettings.Port);

        private static bool IsPortPresent(string port) =>
            SerialPort.GetPortNames().Any(p =>
                string.Equals(p, port, StringComparison.OrdinalIgnoreCase));

        private static bool IsCriticalConnectionError(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is IOException ||
                    current is UnauthorizedAccessException ||
                    current is InvalidOperationException ||
                    current is ObjectDisposedException)
                {
                    return true;
                }
            }

            return false;
        }

        private void NotifyReconnectStatus(string message)
        {
            Console.WriteLine($"[MODBUS RECONNECT] {message}");
            ReconnectStatusChanged?.Invoke(this, message);
        }

        private async Task WaitBusGapAsync(CancellationToken cancellationToken)
        {
            var elapsed = DateTime.UtcNow - _lastRequestTimeUtc;
            var delay = _minRequestGap - elapsed;

            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
        }

        #endregion

        #region WATCHERS

        private void NotifyWatchers(byte slaveId, ushort address, ushort[] values)
        {
            var key = (slaveId, address);

            if (!_watchers.TryGetValue(key, out var callbacks))
                return;

            Action<ushort[]>[] snapshot;

            lock (callbacks)
            {
                snapshot = callbacks.ToArray();
            }

            foreach (var callback in snapshot)
            {
                try
                {
                    callback(values);
                }
                catch
                {
                    // ignore
                }
            }
        }

        #endregion

        public void Dispose()
        {
            DisposeCurrentConnection();
            _ioLock.Dispose();
        }
    }
}
