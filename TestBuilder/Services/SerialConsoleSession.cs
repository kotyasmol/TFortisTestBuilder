using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Modbus;

namespace TestBuilder.Services;
public interface ISerialConsoleSession
{
    Task<string> ExchangeAsync(string port, int baudRate, string command, string terminator, int quietMs, int timeoutMs, CancellationToken token);
    Task<string> ExchangeAsync(ConsoleDeviceDefinition device, string command, string terminator, int quietMs, int timeoutMs, CancellationToken token)
    {
        // Old injected transports may only support the original 8N1 protocol. Never silently drop new settings.
        if (device.Parity != Parity.None || device.DataBits != 8 || device.StopBits != StopBits.One ||
            device.EncodingName != "utf-8" || device.LineEnding != "\n" || device.DtrEnable || device.RtsEnable)
            throw new NotSupportedException("Этот транспорт не поддерживает заданные параметры консоли.");
        return ExchangeAsync(device.PortName, device.BaudRate, command, terminator, quietMs, timeoutMs, token);
    }
}
public sealed class SerialConsoleSession : ISerialConsoleSession, IDisposable
{
    private SerialPort? _port;
    private ConsoleDeviceDefinition? _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public Task<string> ExchangeAsync(string port, int baudRate, string command, string terminator, int quietMs, int timeoutMs, CancellationToken token)
        => ExchangeAsync(new ConsoleDeviceDefinition { Id = "legacy", PortName = port, BaudRate = baudRate }, command, terminator, quietMs, timeoutMs, token);

    public async Task<string> ExchangeAsync(ConsoleDeviceDefinition device, string command, string terminator, int quietMs, int timeoutMs, CancellationToken token)
    {
        device = device with { PortName = device.PortName.Trim() };
        device.Validate(requirePort: true);
        if (timeoutMs is < 100 or > 3600000 || quietMs is < 50 or > 60000)
            throw new InvalidOperationException("Консоль: укажите COM-порт, скорость и допустимые таймауты.");
        await _gate.WaitAsync(token);
        try
        {
            return await Task.Run(async () =>
            {
                if (_port == null || !_port.IsOpen || _settings != device)
                {
                    _port?.Dispose();
                    _port = device.CreatePort();
                    _port.Open();
                    _settings = device;
                }
                token.ThrowIfCancellationRequested();
                _port.DiscardInBuffer();
                _port.Write(command + device.LineEnding);
                var elapsed = Stopwatch.StartNew(); var lastData = 0L;
                var output = new StringBuilder();
                while (elapsed.ElapsedMilliseconds < timeoutMs)
                {
                    token.ThrowIfCancellationRequested();
                    var chunk = _port.ReadExisting();
                    if (chunk.Length > 0)
                    {
                        if (output.Length + chunk.Length > 1_048_576) throw new InvalidOperationException("Консоль: ответ превышает 1 МБ.");
                        output.Append(chunk); lastData = elapsed.ElapsedMilliseconds;
                        if (!string.IsNullOrEmpty(terminator) && output.ToString().Contains(terminator, StringComparison.Ordinal)) return output.ToString();
                    }
                    if (output.Length > 0 && string.IsNullOrEmpty(terminator) && elapsed.ElapsedMilliseconds - lastData >= quietMs) return output.ToString();
                    await Task.Delay(25, token);
                }
                throw new TimeoutException("Консоль: не получен полный ответ за заданное время.");
            }, token);
        }
        finally { _gate.Release(); }
    }
    public void Dispose() { _port?.Dispose(); _port = null; _gate.Dispose(); }
}
