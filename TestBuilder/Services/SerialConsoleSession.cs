using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.IO.Ports;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Modbus;

namespace TestBuilder.Services;
public interface ISerialConsoleSession
{
    Task ConnectAsync(ConsoleDeviceDefinition device, int timeoutMs, CancellationToken token)
        => throw new NotSupportedException("Этот транспорт не поддерживает ожидание подключения консоли.");
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
    private IConsolePort? _port;
    private readonly Func<ConsoleDeviceDefinition, IConsolePort> _createPort;
    private ConsoleDeviceDefinition? _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ISerialPortCatalog _catalog;
    private readonly Func<string, IEnumerable<string>> _excludedPorts;
    private readonly Dictionary<string, string> _resolvedPorts = new(StringComparer.Ordinal);
    public SerialConsoleSession() : this(new SystemSerialPortCatalog(), _ => Array.Empty<string>()) { }
    internal SerialConsoleSession(ISerialPortCatalog catalog, Func<string, IEnumerable<string>> excludedPorts,
        Func<ConsoleDeviceDefinition, IConsolePort>? createPort = null)
    { _catalog = catalog; _excludedPorts = excludedPorts; _createPort = createPort ?? (d => new ConsolePort(d.CreatePort())); }

    internal async Task<ConsoleDeviceDefinition> ResolveDeviceAsync(ConsoleDeviceDefinition device, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var excluded = _excludedPorts(device.Id).Concat(_resolvedPorts.Where(p => p.Key != device.Id).Select(p => p.Value)).ToArray();
        if (string.IsNullOrWhiteSpace(device.PortName) && _resolvedPorts.TryGetValue(device.Id, out var previous))
            device = device with { PortName = previous };
        var ports = string.IsNullOrWhiteSpace(device.PortName) ? await _catalog.GetPortsAsync(token) : Array.Empty<SerialPortInfo>();
        var port = ConsolePortDiscovery.Resolve(device, ports, excluded);
        _resolvedPorts[device.Id] = port;
        return device with { PortName = port };
    }
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
            device = await ResolveDeviceAsync(device, token);
            return await Task.Run(async () =>
            {
                var port = EnsureOpen(device);
                token.ThrowIfCancellationRequested();
                port.DiscardInBuffer();
                port.Write(command + device.LineEnding);
                var elapsed = Stopwatch.StartNew(); var lastData = 0L;
                var output = new StringBuilder();
                while (elapsed.ElapsedMilliseconds < timeoutMs)
                {
                    token.ThrowIfCancellationRequested();
                    var chunk = port.ReadExisting();
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
    private IConsolePort EnsureOpen(ConsoleDeviceDefinition device)
    {
        if (_port != null && _port.IsOpen && _settings == device) return _port;
        _port?.Dispose();
        _port = _createPort(device);
        try { _port.Open(); }
        catch { _resolvedPorts.Remove(device.Id); _port.Dispose(); _port = null; throw; }
        _settings = device;
        return _port;
    }

    public async Task ConnectAsync(ConsoleDeviceDefinition device, int timeoutMs, CancellationToken token)
    {
        device = device with { PortName = device.PortName.Trim() };
        device.Validate(requirePort: true);
        if (timeoutMs is < 100 or > 3600000)
            throw new InvalidOperationException("Ожидание подключения: задайте время от 100 до 3600000 мс.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeoutMs);
        var locked = false;
        try
        {
            await _gate.WaitAsync(deadline.Token); locked = true;
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                try
                {
                    var resolved = await ResolveDeviceAsync(device, deadline.Token);
                    await Task.Run(() => EnsureOpen(resolved), deadline.Token);
                    deadline.Token.ThrowIfCancellationRequested();
                    return;
                }
                catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
                {
                    // The adapter can appear before the OS allows opening it. Retry without sending any bytes.
                    await Task.Delay(1000, deadline.Token);
                }
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException($"«{device.Name}»: USB-UART не подключился за {timeoutMs / 1000.0:g} с. Проверьте питание платы и USB-кабель."); }
        finally { if (locked) _gate.Release(); }
    }
    public void Dispose() { _port?.Dispose(); _port = null; _gate.Dispose(); }
}

internal interface IConsolePort : IDisposable
{
    bool IsOpen { get; }
    void Open();
    void DiscardInBuffer();
    void Write(string text);
    string ReadExisting();
}
internal sealed class ConsolePort(SerialPort port) : IConsolePort
{
    public bool IsOpen => port.IsOpen;
    public void Open() => port.Open();
    public void DiscardInBuffer() => port.DiscardInBuffer();
    public void Write(string text) => port.Write(text);
    public string ReadExisting() => port.ReadExisting();
    public void Dispose() => port.Dispose();
}
