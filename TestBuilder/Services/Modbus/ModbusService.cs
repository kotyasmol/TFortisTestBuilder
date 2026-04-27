using Modbus.Device;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Services.Modbus
{
    public class ModbusService : IModbusService, IDisposable
    {
        private readonly SemaphoreSlim _ioLock = new(1, 1);

        private readonly TimeSpan _minRequestGap = TimeSpan.FromMilliseconds(150);
        private DateTime _lastRequestTimeUtc = DateTime.MinValue;

        private SerialPort? _serialPort;
        private IModbusSerialMaster? _master;

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

                        var master = ModbusSerialMaster.CreateRtu(serialPort);
                        master.Transport.ReadTimeout = timeoutMs;
                        master.Transport.WriteTimeout = timeoutMs;

                        _serialPort = serialPort;
                        _master = master;
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
            await _ioLock.WaitAsync(cancellationToken);

            try
            {
                var master = _master;

                if (master == null)
                    throw new InvalidOperationException("Modbus not connected");

                await WaitBusGapAsync(cancellationToken);

                try
                {
                    var result = await master.ReadHoldingRegistersAsync(slaveId, address, count);

                    NotifyWatchers(slaveId, address, result);

                    LastError = null;

                    return result;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;

                    Console.WriteLine(
                        $"[MODBUS READ ERROR] slave={slaveId}, address={address}, count={count}, " +
                        $"{ex.GetType().Name}: {ex.Message}");

                    await RecoverAfterIoErrorAsync();

                    throw;
                }
            }
            finally
            {
                _lastRequestTimeUtc = DateTime.UtcNow;
                _ioLock.Release();
            }
        }

        public async Task<bool> WriteRegisterAsync(
            byte slaveId,
            ushort address,
            ushort value,
            bool verify = true,
            CancellationToken cancellationToken = default)
        {
            await _ioLock.WaitAsync(cancellationToken);

            try
            {
                var master = _master;

                if (master == null)
                    throw new InvalidOperationException("Modbus not connected");

                await WaitBusGapAsync(cancellationToken);

                try
                {
                    await master.WriteSingleRegisterAsync(slaveId, address, value);

                    _lastRequestTimeUtc = DateTime.UtcNow;

                    if (!verify)
                    {
                        LastError = null;
                        return true;
                    }

                    await WaitBusGapAsync(cancellationToken);

                    var read = await master.ReadHoldingRegistersAsync(slaveId, address, 1);

                    NotifyWatchers(slaveId, address, read);

                    LastError = null;

                    return read[0] == value;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;

                    Console.WriteLine(
                        $"[MODBUS WRITE ERROR] slave={slaveId}, address={address}, value={value}, " +
                        $"{ex.GetType().Name}: {ex.Message}");

                    await RecoverAfterIoErrorAsync();

                    throw;
                }
            }
            finally
            {
                _lastRequestTimeUtc = DateTime.UtcNow;
                _ioLock.Release();
            }
        }

        #endregion

        #region BUS HELPERS

        private async Task WaitBusGapAsync(CancellationToken cancellationToken)
        {
            var elapsed = DateTime.UtcNow - _lastRequestTimeUtc;
            var delay = _minRequestGap - elapsed;

            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
        }

        private async Task RecoverAfterIoErrorAsync()
        {
            try
            {
                _serialPort?.DiscardInBuffer();
                _serialPort?.DiscardOutBuffer();
            }
            catch
            {
                // ignore
            }

            try
            {
                await Task.Delay(250);
            }
            catch
            {
                // ignore
            }
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
            _master?.Dispose();
            _serialPort?.Dispose();
            _ioLock.Dispose();
        }
    }
}