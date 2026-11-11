using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Headless;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

// Explicitly opt-in hardware diagnostic. Uses the production compiler, executor,
// monitoring and profile steps. Never allocates a serial number or posts reports.
if (!OperatingSystem.IsWindows() || args.Length != 5 || args[2] != "--execute-8x2pro" ||
    args[3] is not ("selftest" or "hardware") || !int.TryParse(args[4], out var cycles) || cycles is < 1 or > 10)
{
    Console.Error.WriteLine("Windows only: BenchProbe PROFILE COM_PORT --execute-8x2pro selftest|hardware CYCLES");
    return 2;
}
using var stop = new CancellationTokenSource(TimeSpan.FromHours(1));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
using var session = HeadlessUnitTestSession.StartNew(typeof(ProbeApp));
return await session.Dispatch(async () =>
{
    var logger = new ConsoleLogger();
    using var bus = new ModbusService();
    var slaves = new SlaveManager(bus);
    using var vm = new TestViewModel(bus, slaves);
    GraphSerializer.Deserialize(File.ReadAllText(args[0]), vm);
    var root = vm.RootGraph;
    if (Path.GetFileName(args[0]) != "PSW+UPS-Box 8x2Pro · Параллельная проверка с SFP · 11.11.26.json" || root.Hardware == null)
        throw new InvalidOperationException("Requires the stock 8x2Pro parallel_start profile.");
    SubtestNodeViewModel Stage(string prefix) => root.Nodes.OfType<SubtestNodeViewModel>()
        .Single(n => n.Name.StartsWith(prefix, StringComparison.Ordinal));
    var dataTest = Stage("10.");
    var sfp = dataTest.BodyGraph.Nodes.OfType<SubtestNodeViewModel>().Single(n => n.Name.StartsWith("SFP"));
    sfp.IsEnabled = false; // Run-local switch; the source JSON and its layout are never saved.
    var hardware = root.Hardware;
    var state = new RegisterState();
    using var monitor = new RegisterMonitor(slaves, state, logger) { PollInterval = hardware.PollIntervalMs };
    monitor.ConnectionLost += (_, _) => { logger.Error("Monitoring connection lost; cancelling run."); stop.Cancel(); };
    using var compiler = new GraphCompiler(bus, logger) { ProfileHardware = hardware, ConnectedStandPort = args[1] };
    var executor = new TestExecutor();
    if (!await bus.ConnectAsync(args[1], hardware.BaudRate, hardware.Parity, hardware.DataBits, hardware.StopBits))
        throw new IOException(bus.LastError);
    try
    {
        var count = await slaves.ConfigureAsync(hardware);
        logger.Info($"HARDWARE_IDENTIFIED={count}; SFP excluded; mode={args[3]}; cycles={cycles}");
        monitor.Start();
        for (var cycle = 1; cycle <= cycles; cycle++)
        {
            var context = Context();
            logger.Info($"CYCLE_START={cycle}");
            try
            {
                var stages = args[3] == "hardware"
                    ? new[] { "01.", "02.", "04.", "05.", "06.", "07.", "08.", "09.", "10.", "проверка акб" }
                    : new[] { "01.", "02.", "05.", "06.", "07." };
                foreach (var prefix in stages) await Run(Stage(prefix), context, stop.Token);
                for (var read = 1; read <= 10; read++)
                {
                    await Run(Stage("07."), context, stop.Token);
                    logger.Info($"SELFTEST_REPEAT_OK cycle={cycle} read={read}");
                }
                logger.Info($"CYCLE_OK={cycle}");
            }
            finally
            {
                // Each cleanup gets a fresh context, unaffected by the failed run.
                // Attempt both groups even if one fails; never start another cycle after failure.
                var errors = new List<Exception>();
                foreach (var prefix in new[] { "Аварийное выключение", "02." })
                {
                    try { await Run(Stage(prefix), Context(cleanup: true), CancellationToken.None); }
                    catch (Exception error) { logger.Error("CLEANUP: " + error); errors.Add(error); }
                }
                foreach (var (slave, address) in new (byte, ushort)[] { (23, 1200), (23, 1201), (23, 1215), (23, 1220), (17, 1706) })
                {
                    try
                    {
                        var value = await bus.ReadRegistersAsync(slave, address, 1);
                        logger.Info($"POWER_OFF_READBACK slave={slave} address={address} value={value[0]}");
                        if (value[0] != 0) throw new IOException($"Output {slave}:{address} is not off.");
                    }
                    catch (Exception error) { errors.Add(error); logger.Error(error.Message); }
                }
                if (errors.Count != 0) throw new AggregateException("Cleanup failed: inspect the stand.", errors);
                logger.Info("CLEANUP_CONFIRMED");
            }
        }
        logger.Info("BENCH_PROBE_SUCCESS");
        return 0;
    }
    catch (Exception error) { logger.Error("BENCH_PROBE_FAILED: " + error); return 1; }
    finally { await monitor.StopAsync(); await bus.DisconnectAsync(); }

    TestContext Context(bool cleanup = false) => new(state)
    {
        IsConnected = true, IsCleanup = cleanup, RequireFreshMonitoring = true,
        MaxRegisterAgeMs = Math.Max(10000, hardware.PollIntervalMs * 3),
        RegisterMonitor = monitor, ModbusRegisters = new ModbusRegisterCatalog(slaves.GetSlavesSnapshot()),
        ProfileName = root.Title, DeviceModel = root.DeviceModel
    };
    async Task Run(SubtestNodeViewModel node, TestContext context, CancellationToken token)
    {
        var start = context.ReportEntries.Count;
        var graph = compiler.Compile(root, node, singleNode: true);
        var status = await executor.ExecuteAsync(graph.StartNode, context, token);
        if (status != ExecutionStatus.Completed || context.ReportEntries.Skip(start).Any(e => !e.IsSuccess))
            throw new InvalidOperationException($"Stage '{node.Name}' failed: {status}.");
    }
}, CancellationToken.None);

public sealed class ProbeApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestBuilder.App>()
        .UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
public sealed class ConsoleLogger : ILogger
{
    public string Category => "BenchProbe";
    public ObservableCollection<LogEntry> Entries { get; } = new();
    public void Log(LogLevel level, string message) => Console.WriteLine($"{DateTimeOffset.Now:O} [{level}] {message}");
    public void Trace(string message) => Log(LogLevel.Trace, message);
    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warning(string message) => Log(LogLevel.Warning, message);
    public void Error(string message) => Log(LogLevel.Error, message);
    public void Clear() { }
}
