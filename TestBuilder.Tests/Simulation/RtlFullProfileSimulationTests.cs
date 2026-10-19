using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Headless;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.SerializationTests;
using TestBuilder.Tests.Support;
using TestBuilder.Tests.Views;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.Simulation;

[Collection("Graph editor UI")]
public class RtlFullProfileSimulationTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var board in new[] { "SW", "POE" })
        foreach (var fault in new[] { "none", "write-once", "write", "serial", "server", "cancel", "disconnect", "bus-lost", "short-read", "cleanup" })
            yield return [board, fault, false];
        foreach (var fault in new[] { "as-shipped", "battery", "k5", "sensor", "console", "json", "printer" }) yield return ["SW", fault, false];
        foreach (var fault in new[] { "voltage", "poe-line", "led", "uart", "uart-voltage", "print-enabled", "printer" }) yield return ["POE", fault, false];
    }

    [Theory, MemberData(nameof(Cases))]
    public Task CompleteProfile_UsesRealPolling_AndRejectsFaults(string board, string fault, bool originalTiming)
        => RunScenario(board, fault, originalTiming);

    [Theory, Trait("Category", "RtlOriginalTiming")]
    [InlineData("SW"), InlineData("POE")]
    public Task CompleteProfile_WithOriginalDelays(string board) => RunScenario(board, "none", true);

    static async Task RunScenario(string board, string fault, bool originalTiming)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
            var name = $"RTL_{board}_board.json";
            var source = File.ReadAllText(Path.Combine(root, "profiles", name));
            var key = $"{board}-{fault}-{(originalTiming ? "original" : "fast")}";
            var evidenceRoot = Environment.GetEnvironmentVariable("TFORTIS_RTL_EVIDENCE") ?? Path.Combine(Path.GetTempPath(), "tfortis-rtl-simulation");
            var folder = Path.Combine(evidenceRoot, key); Directory.CreateDirectory(folder);
            using var uiBus = new ModbusService();
            using var vm = new TestViewModel(uiBus, new SlaveManager(uiBus)) { IsOperatorView = false };
            GraphSerializer.Deserialize(source, vm);
            var changes = new List<string>();
            foreach (var n in BoardProfileTests.All(vm.RootGraph))
            {
                if (!originalTiming)
                {
                    if (n is DelayNodeViewModel delay) delay.Milliseconds = Math.Min(delay.Milliseconds, 50);
                    if (n is ForEachSlaveNodeViewModel loop) loop.RepeatDelayMs = Math.Min(loop.RepeatDelayMs, 15);
                }
                if (n is GetSerialNumberFromServerNodeViewModel serial) { serial.ServerBaseUrl = "http://simulation.invalid"; if (!originalTiming) serial.RetryDelayMs = 15; }
                if (n is SendTestReportNodeViewModel report)
                {
                    report.ServerBaseUrl = "http://simulation.invalid"; report.LocalReportsDirectory = Path.Combine(folder, "local-reports");
                    if (!originalTiming) report.RetryDelayMs = 15;
                }
                if (fault != "as-shipped" && n is CheckVariableEqualityNodeViewModel eq)
                {
                    if (eq.ExpectedValue == "SET_PRODUCTION_FIRMWARE") { eq.ExpectedValue = RtlSimulatedRig.Firmware; changes.Add("PoeFirmware = SIM-POE-1.0"); }
                    if (eq.ExpectedValue == "SET_PRODUCTION_MCU") { eq.ExpectedValue = RtlSimulatedRig.Mcu; changes.Add("PoeMcu = SIM-MCU-3"); }
                }
                if (board == "POE" && fault is "print-enabled" or "printer" && n is SubtestNodeViewModel sub && sub.Name.StartsWith("Этикетка"))
                { sub.IsEnabled = true; changes.Add("Включена опциональная печать PoE"); }
            }
            changes.Add(originalTiming ? "Все задержки и период опроса исходные" : "Паузы ≤50 мс, интервалы повторов ≤15 мс, опрос 10 мс");
            changes.Add("HTTP и принтер перехвачены в памяти; локальные отчёты в папке сценария; Stand ID = SIMULATION");
            var settings = AppSettings.Instance; var oldStand = settings.StandId; settings.StandId = "SIMULATION";
            var rig = new RtlSimulatedRig(board == "SW", fault);
            var manager = new SlaveManager(rig);
            var state = new RegisterState();
            using var monitor = new RegisterMonitor(manager, state, NullLogger.Instance) { PollInterval = originalTiming ? vm.RootGraph.Hardware!.PollIntervalMs : 10 };
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(originalTiming ? 8 : 2));
            rig.CancelRun = cts.Cancel;
            var connectionLost = false;
            monitor.ConnectionLost += (_, _) => { connectionLost = true; cts.Cancel(); };
            var ctx = new TestContext(state);
            var elapsed = Stopwatch.StartNew();
            var status = ExecutionStatus.Failed; bool verified = false;
            var cleanupResults = new List<object>();
            try
            {
                Assert.Equal(1, await manager.ConfigureAsync(vm.RootGraph.Hardware!));
                ctx = new TestContext(state)
                {
                    RequireFreshMonitoring = true, MaxRegisterAgeMs = 10000,
                    ModbusRegisters = new(manager.GetSlavesSnapshot()), RegisterMonitor = monitor,
                    CancellationToken = cts.Token, IsConnected = true, ProfileName = vm.CurrentProfileName,
                    OperatorPrompt = message => { rig.Record("operator-confirmed", message); return Task.FromResult(true); },
                    OperatorInput = (message, _) => { rig.Record("operator-input", message); return Task.FromResult<string?>(RtlSimulatedRig.CpuId); },
                    ExecutionObserver = new TraceObserver(vm, rig)
                };
                ctx.RequiredPollAfter[1] = Stopwatch.GetTimestamp();
                vm.Station.BeginRun(vm.RootGraph);
                using var compiler = new GraphCompiler(rig, rig, NullLogger.Instance, rig) { LabelPrinter = rig, ReportClientFactory = rig.CreateReportClient };
                monitor.Start(); rig.Armed = true;
                try { status = await new TestExecutor().ExecuteAsync(compiler.Compile(vm.RootGraph).StartNode, ctx, cts.Token); }
                catch (OperationCanceledException) when (cts.IsCancellationRequested) { status = ExecutionStatus.Cancelled; }
                if (status != ExecutionStatus.Completed)
                {
                    // Same top-level RunOnFailure policy as TestViewModel: preserve failure,
                    // run every enabled cleanup in profile order with a fresh cancellation token.
                    ctx.HasCriticalError = true; ctx.SkipFirstRunNodes = false; rig.Cleanup = true;
                    foreach (var cleanup in vm.RootGraph.Nodes.OfType<SubtestNodeViewModel>().Where(n => n.RunOnFailure && n.IsEnabled))
                    {
                        using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                        ctx.CancellationToken = cleanupCts.Token; vm.Station.BeginCleanup(cleanup);
                        var result = await new TestExecutor().ExecuteAsync(compiler.Compile(cleanup.BodyGraph).StartNode, ctx, cleanupCts.Token);
                        vm.Station.FinishCleanup(cleanup, result == ExecutionStatus.Completed);
                        cleanupResults.Add(new { cleanup.Name, result = result.ToString() });
                    }
                }
                elapsed.Stop(); vm.Station.CompleteRun(status, ctx, elapsed: elapsed.Elapsed);
                var successExpected = fault is "none" or "write-once" or "print-enabled";
                Assert.True(vm.Station.IsSuccessful == successExpected,
                    $"{key}: {status}; {vm.Station.Headline}; {ctx.CriticalFailure?.Summary ?? ctx.Failure?.Summary}\n" + string.Join("\n", ctx.ReportEntries.Where(e => !e.IsSuccess).Select(e => e.Name + ": " + e.Value)));
                Assert.True(rig.ReadCount > 1);
                Assert.Equal(fault is "disconnect" or "bus-lost" or "short-read", connectionLost);
                var outputs = board == "SW" ? new[] { 2301, 2302, 2307, 2385 } : new[] { 2403, 2492, 2489, 2511 };
                if (fault is not ("cleanup" or "bus-lost")) Assert.All(outputs, a => Assert.Equal(0, rig.Value(a)));
                else
                {
                    Assert.Equal(1, rig.Value(outputs[0]));
                    Assert.All(outputs, a => Assert.Contains(rig.Writes, w => w.Address == a && w.Value == 0));
                    Assert.Contains("состояние стенда", vm.Station.Headline);
                }
                if (successExpected)
                {
                    Assert.Equal(ExecutionStatus.Completed, status); Assert.False(ctx.HasCriticalError);
                    Assert.All(ctx.ReportEntries, e => Assert.True(e.IsSuccess, e.Name));
                    Assert.Single(rig.SerialRequests); Assert.Contains(RtlSimulatedRig.CpuId, rig.SerialRequests[0]);
                    Assert.Single(rig.Reports); Assert.Contains("test_result=true=1", rig.Reports[0]);
                    Assert.Equal("Sent", ctx.GetVariable<string>("ReportDelivery.Status"));
                    Assert.NotEmpty(Directory.GetFiles(Path.Combine(folder, "local-reports")));
                    if (board == "SW")
                    {
                        Assert.Equal(new[] { 2323, 2325, 2327, 2325, 2323 }, rig.Writes.Where(w => w.Value == 1 && w.Address is 2323 or 2325 or 2327).Select(w => w.Address));
                        Assert.DoesNotContain(rig.Writes, w => new[] { 2335, 2338, 2343, 2346, 2349, 2352, 2355, 2358, 2361, 2364, 2367, 2370 }.Contains(w.Address));
                        foreach (var a in new[] { 2303, 2304, 2305 }) Assert.Equal(new[] { 0, 0, 1, 0 }, rig.Writes.Where(w => w.Address == a).Select(w => w.Value));
                        Assert.Equal(3, rig.Commands.Count(c => c.Contains("setParam")));
                    }
                    else
                    {
                        Assert.Equal(16, ctx.ReportEntries.Count(e => e.Name.StartsWith("Цвет Led")));
                        Assert.Equal(16, ctx.ReportEntries.Count(e => e.Name.StartsWith("Напряжение UART")));
                    }
                    foreach (var a in board == "SW" ? new[] { 2324, 2326, 2328, 2331 } : new[] { 2494 })
                    { Assert.Contains((a, 1), rig.StatusSamples); Assert.Contains((a, 2), rig.StatusSamples); }
                    if (board == "SW" || fault == "print-enabled")
                    { Assert.Single(rig.Labels); Assert.Contains("123456", rig.Labels[0]); Assert.Contains("P1", rig.Labels[0]); }
                    else Assert.Empty(rig.Labels);
                }
                else
                {
                    if (fault != "server") Assert.NotEqual(ExecutionStatus.Completed, status);
                    if (fault == "server") { Assert.Equal("Failed", ctx.GetVariable<string>("ReportDelivery.Status")); Assert.True(rig.Reports.Count >= 3); }
                    if (fault == "as-shipped") Assert.Contains("PoeFirmware", ctx.CriticalFailure?.Summary ?? ctx.Failure?.Summary ?? "");
                    if (fault == "poe-line") { Assert.Contains(ctx.ReportEntries, e => e.Name == "PoE 1A" && !e.IsSuccess); Assert.Contains(ctx.ReportEntries, e => e.Name == "PoE 8B" && e.IsSuccess); }
                    if (fault == "k5") { Assert.Contains(ctx.ReportEntries, e => e.Name == "K5 VMAIN" && !e.IsSuccess); Assert.Contains(ctx.ReportEntries, e => e.Name == "K5 VRES" && e.IsSuccess); }
                    if (fault == "led") { Assert.Contains(ctx.ReportEntries, e => e.Name == "Цвет Led1A" && !e.IsSuccess); Assert.Contains(ctx.ReportEntries, e => e.Name == "Цвет Led8B" && e.IsSuccess); }
                    if (board == "POE" && fault is "voltage" or "poe-line" or "led" or "uart" or "uart-voltage" or "cancel" or "disconnect" or "bus-lost" or "short-read" or "write")
                    { Assert.Single(rig.Reports); Assert.Contains("test_result=true=0", rig.Reports[0]); }
                    if (fault == "serial") Assert.Empty(rig.Reports);
                    if (fault == "printer") Assert.Single(rig.Labels);
                }
                verified = true;
            }
            finally
            {
                await monitor.StopAsync(); settings.StandId = oldStand;
                var opts = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(new
                {
                    scenario = key, board, fault, originalTiming, verified, utc = DateTimeOffset.UtcNow,
                    profile = name, sourceSha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source))),
                    changes, status = status.ToString(), successful = vm.Station.IsSuccessful, vm.Station.Headline,
                    seconds = elapsed.Elapsed.TotalSeconds, rig.ReadCount, rig.ReadErrors, rig.WriteAttempts, connectionLost,
                    failure = ctx.CriticalFailure?.Summary ?? ctx.Failure?.Summary, cleanupResults,
                    reportEntries = ctx.ReportEntries, reportsSentToMock = rig.Reports.Count, labelsCaptured = rig.Labels.Count,
                    powerOutputs = (board == "SW" ? new[] { 2301, 2302, 2307, 2385 } : new[] { 2403, 2492, 2489, 2511 }).ToDictionary(a => a, rig.Value)
                }, opts));
                File.WriteAllLines(Path.Combine(folder, "trace.jsonl"), rig.Trace.Select(e => JsonSerializer.Serialize(e)));
                for (var i = 0; i < rig.Labels.Count; i++) File.WriteAllText(Path.Combine(folder, $"label-{i + 1}.epl"), rig.Labels[i]);
            }
            return true;
        }, CancellationToken.None);
    }

    sealed class TraceObserver(TestViewModel vm, RtlSimulatedRig rig) : IExecutionObserver
    {
        public Task NodeStartedAsync(TestNode n, TestContext c, CancellationToken t) { rig.Record("node-start", n.DisplayName); return vm.NodeStartedAsync(n, c, t); }
        public Task NodeCompletedAsync(TestNode n, StepResult r, TestContext c, CancellationToken t) { rig.Record("node-result", new { n.DisplayName, result = r.ToString() }); return vm.NodeCompletedAsync(n, r, c, t); }
        public Task NodeFinishedAsync(TestNode n, TestContext c, CancellationToken t) => vm.NodeFinishedAsync(n, c, t);
        public Task NodeFailedAsync(TestNode n, TestContext c, CancellationToken t) => vm.NodeFailedAsync(n, c, t);
    }
}
