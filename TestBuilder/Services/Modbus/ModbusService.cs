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

        private SerialPort? _serialPort;
        private IModbusSerialMaster? _master;

        private readonly ConcurrentDictionary<(byte slaveId, ushort address), List<Action<ushort[]>>> _watchers = new();

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (_isConnected == value) return;
                _isConnected = value;
                IsConnectedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // Событие срабатывает когда IsConnected меняется
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
                        const int timeoutMs = 5000;

                        var serialPort = new SerialPort(port, baudRate, parity, dataBits, stopBits)
                        {
                            ReadTimeout = timeoutMs,
                            WriteTimeout = timeoutMs
                        };

                        serialPort.Open();

                        var master = ModbusSerialMaster.CreateRtu(serialPort);
                        master.Transport.ReadTimeout = timeoutMs;
                        master.Transport.WriteTimeout = timeoutMs;

                        _serialPort = serialPort;
                        _master = master;
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
                    list.Add(callback);
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

                var result = await master.ReadHoldingRegistersAsync(slaveId, address, count);

                NotifyWatchers(slaveId, address, result);

                return result;
            }
            finally
            {
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

                await master.WriteSingleRegisterAsync(slaveId, address, value);

                if (!verify)
                    return true;

                var read = await master.ReadHoldingRegistersAsync(slaveId, address, 1);

                NotifyWatchers(slaveId, address, read);

                return read[0] == value;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        #endregion

        #region WATCHERS

        private void NotifyWatchers(byte slaveId, ushort address, ushort[] values)
        {
            var key = (slaveId, address);

            if (_watchers.TryGetValue(key, out var callbacks))
            {
                foreach (var cb in callbacks)
                {
                    try
                    {
                        cb(values);
                    }
                    catch { }
                }
            }
        }

        #endregion

        public void Dispose()
        {
            _master?.Dispose();
            _serialPort?.Dispose();
        }
    }
}