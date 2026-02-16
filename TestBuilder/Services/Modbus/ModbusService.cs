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
        private readonly ConcurrentDictionary<(byte slaveId, ushort address), List<Action<ushort[]>>> _watchers = new();
        private readonly SemaphoreSlim _ioLock = new(1, 1);

        private SerialPort _serialPort;
        private IModbusSerialMaster _master;

        private CancellationTokenSource _monitorCts;
        private Task _monitorTask;

        private readonly Dictionary<(byte slaveId, ushort address), ushort> _lastValues = new();

        public bool IsConnected { get; private set; }
        public string LastError { get; private set; }

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
                // Сначала аккуратно закрываем предыдущее соединение.
                await DisconnectAsync();

                // Весь блок открытия порта и создания мастера уносим с UI-потока
                // на thread pool, чтобы SerialPort.Open и внутренние вызовы
                // NModbus не блокировали интерфейс.
                return await Task.Run(() =>
                {
                    try
                    {
                        _serialPort = new SerialPort(port, baudRate, parity, dataBits, stopBits)
                        {
                            ReadTimeout = 500,
                            WriteTimeout = 500
                        };

                        _serialPort.Open();

                        _master = ModbusSerialMaster.CreateRtu(_serialPort);
                        _master.Transport.ReadTimeout = 500;
                        _master.Transport.WriteTimeout = 500;

                        IsConnected = true;
                        LastError = null;

                        StartMonitoring();
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

        public async Task DisconnectAsync()
        {
            StopMonitoring();

            await _ioLock.WaitAsync();
            try
            {
                _master?.Dispose();
                _serialPort?.Close();
            }
            finally
            {
                _ioLock.Release();
            }

            _master = null;
            _serialPort = null;
            IsConnected = false;
        }

        #endregion

        #region MONITORING

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

        private void StartMonitoring()
        {
            _monitorCts = new CancellationTokenSource();
            _monitorTask = Task.Run(() => MonitorLoop(_monitorCts.Token));
        }

        private void StopMonitoring()
        {
            try
            {
                _monitorCts?.Cancel();
                _monitorTask?.Wait(300);
            }
            catch { }
        }

        private async Task MonitorLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (!IsConnected || _master == null)
                {
                    await Task.Delay(1000, token);
                    continue;
                }

                foreach (var (key, callbacks) in _watchers)
                {
                    try
                    {
                        await _ioLock.WaitAsync(token);

                        var values = await _master.ReadHoldingRegistersAsync(
                            key.slaveId, key.address, 1);

                        if (!_lastValues.TryGetValue(key, out var old) || old != values[0])
                        {
                            _lastValues[key] = values[0];
                            foreach (var cb in callbacks)
                                cb(values);
                        }
                    }
                    catch { }
                    finally
                    {
                        _ioLock.Release();
                    }

                    await Task.Delay(50, token);
                }

                await Task.Delay(200, token);
            }
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
                return await _master.ReadHoldingRegistersAsync(slaveId, address, count);
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
                await _master.WriteSingleRegisterAsync(slaveId, address, value);

                if (!verify)
                    return true;

                var read = await _master.ReadHoldingRegistersAsync(slaveId, address, 1);
                return read[0] == value;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        #endregion

        public void Dispose()
        {
            StopMonitoring();
            _master?.Dispose();
            _serialPort?.Dispose();
        }
    }
}
