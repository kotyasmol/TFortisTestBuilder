using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Headless;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.SerializationTests;
using TestBuilder.Tests.Support;
using TestBuilder.Tests.Views;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.MonitoringTests;

[Collection("Graph editor UI")]
public class K5StateWaitingTests
{
    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(1, false)]
    public async Task RealK5GraphWaitsForTimedController_WithoutExtraBusReads(int terminal, bool success)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var uiBus = new ModbusService();
            using var vm = new TestViewModel(uiBus, new SlaveManager(uiBus));
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../profiles/RTL_SW_board.json"));
            GraphSerializer.Deserialize(File.ReadAllText(path), vm);
            Assert.Equal(1000, vm.RootGraph.Hardware!.PollIntervalMs);
            var k5 = BoardProfileTests.All(vm.RootGraph).OfType<SubtestNodeViewModel>().Where(n => n.Name.StartsWith("K5 ") && !n.Name.Contains("пять")).ToArray();
            Assert.Equal(5, k5.Length);
            foreach (var stage in k5)
            {
                var waits = BoardProfileTests.All(stage.BodyGraph).OfType<CheckRegisterRangeNodeViewModel>().Where(n => n.ReadAttempts > 1).ToArray();
                Assert.Equal(2, waits.Length);
                Assert.Equal((1, 1, 60, 1000), (waits[0].Min, waits[0].Max, waits[0].ReadAttempts, waits[0].ReadIntervalMs));
                Assert.Equal((2, 3, 120, 500), (waits[1].Min, waits[1].Max, waits[1].ReadAttempts, waits[1].ReadIntervalMs));
            }
            var graph = k5[0].BodyGraph;
            foreach (var node in BoardProfileTests.All(graph))
            {
                if (node is DelayNodeViewModel delay) delay.Milliseconds = 5;
                if (node is CheckRegisterRangeNodeViewModel range && range.ReadAttempts > 1)
                { range.ReadAttempts = 30; range.ReadIntervalMs = 20; }
            }
            var rig = new TimedK5Rig((ushort)terminal);
            var manager = new SlaveManager(rig);
            await manager.ConfigureAsync(vm.RootGraph.Hardware);
            var state = new RegisterState();
            var logger = new CaptureLogger();
            using var monitor = new RegisterMonitor(manager, state, NullLogger.Instance) { PollInterval = 40 };
            var context = new TestContext(state) { RequireFreshMonitoring = true, MaxRegisterAgeMs = 10000 };
            using var compiler = new GraphCompiler(rig, logger);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            monitor.Start();
            try
            {
                var result = await new TestExecutor().ExecuteAsync(compiler.Compile(graph).StartNode, context, cts.Token);
                Assert.Equal(success, result == ExecutionStatus.Completed);
                Assert.Equal(1, rig.StartWrites);
                Assert.Contains(1, rig.Samples);
                if (terminal != 1) Assert.Contains(terminal, rig.Samples);
                Assert.True(rig.BlockReadTimes.Count >= 3);
                // Ignore the initial ConfigureAsync read. All following reads belong to one poller.
                var times = rig.BlockReadTimes.Skip(1).ToArray();
                Assert.All(times.Zip(times.Skip(1)), pair => Assert.True(pair.Second - pair.First >= 35));
                Assert.InRange(logger.Entries.Count(e => e.Level >= LogLevel.Warning), 0, terminal == 2 ? 0 : 2);
                Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning && e.Message.Contains("значение 1", StringComparison.OrdinalIgnoreCase) && terminal != 1);
            }
            finally { await monitor.StopAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CachedRangeWaitIsCancellable_AndDoesNotAcceptPreCommandResult()
    {
        var state = new RegisterState();
        state.Update(1, 2324, 2);
        var context = new TestContext(state) { RequireFreshMonitoring = true };
        context.RequiredPollAfter[1] = Stopwatch.GetTimestamp();
        var step = new CheckRegisterRangeStep(1, 2324, 2, 3, NullLogger.Instance, readAttempts: 120, readIntervalMs: 500);
        using var cts = new CancellationTokenSource(80);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => step.ExecuteAsync(context, cts.Token));
    }

    [Fact]
    public async Task BusyCacheProducesOnlyOneFinalFailure()
    {
        var state = new RegisterState(); state.Update(1, 2324, 1);
        var logger = new CaptureLogger();
        var step = new CheckRegisterRangeStep(1, 2324, 2, 3, logger, readAttempts: 8, readIntervalMs: 15);
        var clock = Stopwatch.StartNew();
        Assert.Equal(StepResult.False, await step.ExecuteAsync(new TestContext(state), CancellationToken.None));
        Assert.True(clock.ElapsedMilliseconds >= 100);
        Assert.Single(logger.Entries.Where(e => e.Level >= LogLevel.Warning));
        Assert.True(logger.Entries.Count <= 3);
    }

    private sealed class TimedK5Rig(ushort terminal) : IModbusService
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long started = -1;
        public List<long> BlockReadTimes { get; } = new();
        public List<int> Samples { get; } = new();
        public int StartWrites { get; private set; }
        public Task<ushort[]> ReadRegistersAsync(byte slaveId, ushort address, ushort count, CancellationToken cancellationToken = default)
        {
            Assert.Equal(1, slaveId);
            if (address == 0 && count == 1) return Task.FromResult(new ushort[] { 104 });
            Assert.Equal(2300, address); Assert.Equal(86, count);
            var values = new ushort[86];
            // Result changes with elapsed controller time, never with the number of reads.
            values[24] = started < 0 ? (ushort)0 : clock.ElapsedMilliseconds - started < 180 ? (ushort)1 : terminal;
            BlockReadTimes.Add(clock.ElapsedMilliseconds); Samples.Add(values[24]);
            return Task.FromResult(values);
        }
        public Task<bool> WriteRegisterAsync(byte slaveId, ushort address, ushort value, bool verify = true, CancellationToken cancellationToken = default)
        {
            Assert.Equal(2323, address); Assert.Equal(1, value); Assert.False(verify);
            StartWrites++; started = clock.ElapsedMilliseconds;
            return Task.FromResult(true);
        }
        public Task<bool> CheckPortAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void SubscribeRegister(byte slaveId, ushort address, Action<ushort[]> callback) => throw new NotSupportedException();
    }

    private sealed class CaptureLogger : ILogger
    {
        public string Category => "Test";
        public ObservableCollection<LogEntry> Entries { get; } = new();
        public void Log(LogLevel level, string message) => Entries.Add(new LogEntry(DateTime.Now, level, Category, message));
        public void Trace(string message) => Log(LogLevel.Trace, message);
        public void Debug(string message) => Log(LogLevel.Debug, message);
        public void Info(string message) => Log(LogLevel.Info, message);
        public void Warning(string message) => Log(LogLevel.Warning, message);
        public void Error(string message) => Log(LogLevel.Error, message);
        public void Clear() => Entries.Clear();
    }
}
