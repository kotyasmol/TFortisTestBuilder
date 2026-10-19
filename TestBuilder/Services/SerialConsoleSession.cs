using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Services;
public interface ISerialConsoleSession
{
    Task<string> ExchangeAsync(string port, int baudRate, string command, string terminator, int quietMs, int timeoutMs, CancellationToken token);
}
public sealed class SerialConsoleSession : ISerialConsoleSession, IDisposable
{
    private SerialPort? _port;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<string> ExchangeAsync(string port, int baudRate, string command, string terminator, int quietMs, int timeoutMs, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(port) || baudRate is < 300 or > 1000000 || timeoutMs is < 100 or > 3600000 || quietMs is < 50 or > 60000)
            throw new InvalidOperationException("Консоль: укажите COM-порт, скорость и допустимые таймауты.");
        await _gate.WaitAsync(token);
        try
        {
            return await Task.Run(async () =>
            {
                if (_port == null || !_port.IsOpen || _port.PortName != port || _port.BaudRate != baudRate)
                {
                    _port?.Dispose();
                    _port = new SerialPort(port, baudRate, Parity.None, 8, StopBits.One) { ReadTimeout = 100, WriteTimeout = 1000, Encoding = Encoding.UTF8, NewLine = "\n", DtrEnable = false, RtsEnable = false };
                    _port.Open();
                }
                token.ThrowIfCancellationRequested();
                _port.DiscardInBuffer();
                _port.Write(command + "\n");
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
