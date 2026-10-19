using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.SerializationTests;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.StepTests;

public class BoardShutdownTests
{
    [Theory]
    [InlineData("SW", "none")]
    [InlineData("POE", "none")]
    [InlineData("SW", "write")]
    [InlineData("POE", "write")]
    [InlineData("SW", "exception")]
    [InlineData("POE", "exception")]
    [InlineData("SW", "readback")]
    [InlineData("POE", "readback")]
    [InlineData("SW", "read-error")]
    [InlineData("SW", "timeout-once")]
    public async Task ShutdownConfirmsEveryOutput_AndContinuesAfterFailedWriteReadOrException(string board, string fault)
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        GraphSerializer.Deserialize(File.ReadAllText(TestProfilePaths.Find($"RTL_{board}_board.json")), vm);
        foreach (var loop in BoardProfileTests.All(vm.RootGraph).OfType<ForEachSlaveNodeViewModel>()) loop.RepeatDelayMs = 0;
        var shutdown = vm.RootGraph.Nodes.OfType<SubtestNodeViewModel>().Single(n => n.RunOnFailure && n.IsEnabled);
        int[] addresses = board == "SW" ? [2301,2302,2303,2304,2305,2307,2308,2385] : [2403,2492,2487,2489,2511];
        var rig = new ShutdownRig(addresses, fault);
        using var compiler = new GraphCompiler(rig, NullLogger.Instance);
        using var cancelledRun = new CancellationTokenSource(); cancelledRun.Cancel();
        var context = new TestContext(new RegisterState()) { CancellationToken = cancelledRun.Token, SkipFirstRunNodes = true };
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var result = await new TestExecutor().ExecuteAsync(compiler.Compile(vm.RootGraph, shutdown, true).StartNode, context, limit.Token);
        Assert.Equal(fault is "none" or "timeout-once" ? ExecutionStatus.Completed : ExecutionStatus.Failed, result);
        Assert.All(addresses, address => Assert.Contains(address, rig.Writes));
        Assert.All(addresses.Skip(1), address => { Assert.Equal(0, rig.Values[address]); Assert.Contains(address, rig.Reads); });
        Assert.Equal(fault == "none" ? 1 : fault == "timeout-once" ? 2 : 3, rig.Writes.Count(a => a == addresses[0]));
        Assert.False(context.IsCleanup);
    }

    private sealed class ShutdownRig(int[] addresses, string fault) : IModbusService
    {
        public Dictionary<int, ushort> Values { get; } = addresses.ToDictionary(a => a, _ => (ushort)1);
        public List<int> Writes { get; } = new();
        public List<int> Reads { get; } = new();
        public async Task<bool> WriteRegisterAsync(byte slaveId, ushort address, ushort value, bool verify = true, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Assert.Equal(0, value); Assert.Equal(1, slaveId);
            Writes.Add(address);
            if (address == addresses[0])
            {
                if (fault == "exception") throw new IOException("Simulated write failure");
                if (fault == "write") return false;
                if (fault == "timeout-once" && Writes.Count == 1) await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            Values[address] = value; return true;
        }
        public Task<ushort[]> ReadRegistersAsync(byte slaveId, ushort address, ushort count, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Assert.Equal((ushort)1, count); Reads.Add(address);
            if (address == addresses[0] && fault == "read-error") throw new IOException("Simulated read failure");
            return Task.FromResult(new[] { address == addresses[0] && fault == "readback" ? (ushort)1 : Values[address] });
        }
        public Task<bool> CheckPortAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public void SubscribeRegister(byte slaveId, ushort address, Action<ushort[]> callback) { }
    }
}
