using TestBuilder.Domain.Modbus;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Tests.Simulation;

// Independent model of the controller protocol, not a lookup of expected graph values.
// Register meanings: RTL_v2 86f7dbd, Sw tests and Infrastructure/Poe/PoeRegisterMap.cs.
// It models command/state transitions; it does not model analogue circuitry or RTU frames.
internal sealed class RtlSimulatedRig : IModbusService, IHttpRequestService, ISerialConsoleSession, IRawLabelPrinter
{
    public const string CpuId = "413148580e5939514c53698b";
    public const string Firmware = "v16.23";
    public const string Mcu = "GigaDevice GD32E230";
    readonly object gate = new();
    readonly Dictionary<int, ushort> registers = new();
    readonly Dictionary<int, long> busyStarted = new();
    readonly TimeSpan operationTime;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly bool sw;
    public string Fault { get; }
    public ConcurrentQueue<object> Trace { get; } = new();
    public List<(int Address, int Value)> Writes { get; } = new();
    public List<(int Address, int Value)> StatusSamples { get; } = new();
    public List<string> Commands { get; } = new();
    public List<string> Reports { get; } = new();
    public List<string> Labels { get; } = new();
    public List<string> SerialRequests { get; } = new();
    public Action? CancelRun { get; set; }
    public bool Armed { get; set; }
    public bool Cleanup { get; set; }
    public int ReadCount { get; private set; }
    public int ReadErrors { get; private set; }
    public int WriteAttempts { get; private set; }
    public bool PowerWasEnabled { get; private set; }

    public RtlSimulatedRig(bool isSw, string fault, TimeSpan operationTime)
    {
        sw = isSw; Fault = fault; this.operationTime = operationTime;
        for (var a = sw ? 2300 : 2400; a <= (sw ? 2385 : 2511); a++) registers[a] = 0;
        registers[0] = (ushort)(sw ? 104 : 105); // controller identification, outside the polling block
        if (sw) registers[2317] = registers[2318] = 3000; // battery is present before power
        else registers[2486] = 0; // fixture PCB revision, independent of the profile
    }

    public void Record(string kind, object data) => Trace.Enqueue(new { ms = clock.Elapsed.TotalMilliseconds, kind, data });
    public int Value(int address) { lock (gate) return registers[address]; }
    bool Powered => sw ? registers[2301] != 0 || registers[2302] != 0
        : registers[2403] == 1 && registers[2492] == 1 && registers[2489] == 0;

    public async Task<ushort[]> ReadRegistersAsync(byte slaveId, ushort address, ushort count, CancellationToken cancellationToken = default)
    {
        await Task.Delay(2, cancellationToken); // asynchronous transport; no writes directly into RegisterState
        lock (gate)
        {
            if (slaveId != 1 || count is 0 or > 125 || !registers.ContainsKey(address) || !registers.ContainsKey(address + count - 1))
                throw new InvalidOperationException($"Unexpected Modbus read: {slaveId}/{address}/{count}");
            ReadCount++;
            if (Armed && Fault is "disconnect" or "bus-lost" && PowerWasEnabled)
            {
                ReadErrors++; Record("read-error", new { address, count }); throw new TimeoutException("Simulated Modbus transport failure");
            }
            if (Armed && Fault == "short-read" && PowerWasEnabled)
            {
                ReadErrors++; Record("short-read", new { address, count }); return new ushort[count - 1];
            }
            // The controller progresses with elapsed time, not with the number of bus reads.
            foreach (var (status, started) in busyStarted.ToArray())
                if (Stopwatch.GetElapsedTime(started) >= (status == 2494 ? operationTime / 4 : operationTime))
                {
                    registers[status] = (ushort)((Fault == "k5" && status == 2324) || (Fault == "uart" && status == 2494) ? 3 : 2);
                    busyStarted.Remove(status);
                }
            UpdateMeasurements();
            var values = Enumerable.Range(address, count).Select(a => registers[a]).ToArray();
            foreach (var status in new[] { 2324, 2326, 2328, 2331, 2494 }.Where(a => a >= address && a < address + count))
                StatusSamples.Add((status, registers[status]));
            Record("read", new { slaveId, address, values }); return values;
        }
    }

    void UpdateMeasurements()
    {
        if (sw)
        {
            registers[2317] = (ushort)(Fault == "battery" ? 2000 : 3000);
            registers[2319] = registers[2305] == 0 ? (ushort)100 : (ushort)3300;
            registers[2320] = registers[2319];
            return;
        }
        registers[2404] = Powered ? (ushort)(Fault == "voltage" ? 0 : 3300) : (ushort)0;
        for (var i = 0; i < 16; i++)
        {
            registers[2405 + i] = (ushort)(Powered && !(Fault == "poe-line" && i == 0) ? 1 : 0);
            registers[2421 + i] = (ushort)(Powered ? 100 : 0);
            registers[2437 + i] = (ushort)(Powered ? (Fault == "led" && i == 0 ? 120 : 10) : 0);
            registers[2453 + i] = (ushort)(Powered ? 10 : 0);
            registers[2469 + i] = (ushort)(Powered ? 100 : 0);
            registers[2495 + i] = (ushort)(Powered && registers[2494] == 2 ? (Fault == "uart-voltage" && i == 15 ? 0 : 51000) : 0);
        }
    }

    public Task<bool> WriteRegisterAsync(byte slaveId, ushort address, ushort value, bool verify = true, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var writable = sw
                ? new[] { 2301, 2302, 2303, 2304, 2305, 2307, 2308, 2323, 2325, 2327, 2329, 2330, 2332, 2385 }.Contains(address) || address is >= 2333 and <= 2370
                : new[] { 2403, 2485, 2487, 2489, 2490, 2492, 2493, 2511 }.Contains(address);
            if (slaveId != 1 || !writable) throw new InvalidOperationException($"Unexpected Modbus write: {slaveId}/{address}/{value}");
            WriteAttempts++; Writes.Add((address, value));
            bool accepted = !(Fault == "write" && !Cleanup && address == (sw ? 2301 : 2403))
                && !(Fault == "cleanup" && PowerWasEnabled && value == 0 && address == (sw ? 2301 : 2403))
                && !(Fault == "bus-lost" && PowerWasEnabled)
                && !(Fault == "write-once" && WriteAttempts == 1);
            Record("write", new { slaveId, address, value, accepted });
            if (!accepted) return Task.FromResult(false);
            registers[address] = value;
            if (value == 1 && address == (sw ? 2301 : 2403)) PowerWasEnabled = true;
            if (sw && value == 1 && address is 2323 or 2325 or 2327 or 2330)
            {
                if (address == 2330 && !Powered) throw new InvalidOperationException("VCC test started without board power");
                var status = address + 1; registers[status] = 1; busyStarted[status] = Stopwatch.GetTimestamp();
                ushort[] measured = address == 2330 ? [3300, 1500, 1100, 3000, 3000] : [52000, 55000, 52000, 2048, 12000];
                for (var i = 0; i < measured.Length; i++) registers[(address == 2330 ? 2376 : 2371) + i] = measured[i];
            }
            if (!sw && address == 2493 && value == 1)
            {
                if (!Powered) throw new InvalidOperationException("UART started without power/reset release");
                registers[2494] = 1; busyStarted[2494] = Stopwatch.GetTimestamp();
            }
            if (Armed && Fault == "cancel" && address == (sw ? 2301 : 2489) && value == 1) CancelRun?.Invoke();
            return Task.FromResult(true);
        }
    }

    public Task ConnectAsync(ConsoleDeviceDefinition device, int timeoutMs, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (gate)
        {
            Assert.Equal(1, registers[2301]); Assert.Equal(1, registers[2302]);
            Record("console-connect", new { device.Id, device.PortName, timeoutMs });
        }
        return Task.CompletedTask;
    }
    public Task<string> ExchangeAsync(string port, int baud, string command, string terminator, int quiet, int timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!sw || !Powered) throw new InvalidOperationException("Console used without SW power");
            Commands.Add(command);
            if (Fault == "console") throw new TimeoutException("Simulated console timeout");
            if (Fault == "json") return Task.FromResult("echo\n{invalid json\nroot@TFortis:/#");
            string response;
            if (command == "ubus call system board") response = "{\"release\":{\"version\":\"1.2.3\"}}";
            else if (command == "ubus call poe info") response = JsonSerializer.Serialize(new { firmware = Fault == "firmware" ? "wrong-firmware" : Firmware, mcu = Mcu });
            else
            {
                var name = Regex.Match(command, "\"name\":\"([^\"]+)\"").Groups[1].Value;
                if (command.Contains("setParam") && name == "relay")
                {
                    registers[2306] = ushort.Parse(Regex.Match(command, "\"value\":\"([01])\"").Groups[1].Value);
                    response = "{}";
                }
                else
                {
                    var value = name switch
                    {
                        "sensor1" => Fault == "sensor" ? "0" : registers[2303].ToString(),
                        "sensor2" => registers[2304].ToString(), "tamper" => registers[2305].ToString(),
                        "sensorConnected" or "upsModeAvalible" => "1", "sensorTemperature" => "23.5",
                        "SW_VERS" => "1.0.0", "DEVICE_ID" => CpuId,
                        "HW_ERR1" or "HW_ERR2" or "HW_ERR3" => "0",
                        _ => throw new InvalidOperationException("Unmodelled console command: " + command)
                    };
                    response = JsonSerializer.Serialize(new Dictionary<string, string> { [name] = value });
                }
            }
            Record("console", new { command, response });
            return Task.FromResult(command + "\r\n" + response + "\r\nroot@TFortis:/#");
        }
    }

    public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); SerialRequests.Add(url); Record("serial-number-request", url);
        return Task.FromResult(HttpRequestResult.Success(200, Fault == "serial" ? "not-a-number" : "123456", TimeSpan.Zero));
    }
    public HttpClient CreateReportClient() => new(new ReportHandler(this));
    sealed class ReportHandler(RtlSimulatedRig rig) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method != HttpMethod.Post || request.RequestUri?.Host != "simulation.invalid") throw new InvalidOperationException("Unexpected report destination");
            var body = await request.Content!.ReadAsStringAsync(ct);
            rig.Reports.Add(body); rig.Record("report", new { url = request.RequestUri.ToString(), contentType = request.Content.Headers.ContentType?.ToString(), body });
            return new HttpResponseMessage(rig.Fault == "server" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
                { Content = new StringContent(rig.Fault == "server" ? "Unavailable" : "Ok") };
        }
    }
    public RawLabelPrintResult Print(string printerName, byte[] bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var label = Encoding.ASCII.GetString(bytes); Labels.Add(label); Record("label", new { printerName, label });
        return Fault == "printer" ? RawLabelPrintResult.Fail(5, "Simulated printer unavailable") : RawLabelPrintResult.Ok();
    }
    public Task<bool> CheckPortAsync(CancellationToken ct = default) => Task.FromResult(true);
    public void SubscribeRegister(byte slaveId, ushort address, Action<ushort[]> callback) => throw new NotSupportedException();
}
