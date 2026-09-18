using System.Text.Json;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Services.Modbus;
using TestBuilder.Serialization;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

public class Io2ProfileTests
{
    private const string FullProfile = "PSW_2G6F_plus_full_algorithm.json";

    [Theory]
    [InlineData(FullProfile, false)]
    [InlineData("PSW_2G6F_plus_diagnostic_draft.json", false)]
    [InlineData("PSW_UPS_Box_8x2Pro_full_algorithm_polling.json", true)]
    public void ProfilesUseVisibleWriteAndFreshWebWaitNodes(string file, bool relay)
    {
        using var service = new ModbusService();
        var vm = Load(file, service);
        for (var roundTrip = 0; roundTrip < 2; roundTrip++)
        {
            var graph = SensorGraph(vm);
            var sequence = SuccessPath(graph);
            var writes = sequence.OfType<ModbusWriteNodeViewModel>().ToArray();
            Assert.All(writes, n =>
            {
                Assert.Equal(21, n.SlaveId);
                Assert.False(n.UseCurrentSlaveId);
                Assert.True(n.VerifyWrite);
            });
            Assert.Equal(new[] { (1500, 0), (1501, 0), (1500, 1), (1500, 0), (1501, 1), (1501, 0) },
                writes.Where(n => n.Address is 1500 or 1501).Select(n => ((int)n.Address, (int)n.Value)));
            Assert.IsType<ModbusWriteNodeViewModel>(sequence[1]);
            Assert.IsType<ModbusWriteNodeViewModel>(sequence[2]);
            foreach (var sensor in new[] { 1, 2 })
            {
                var on = writes.Single(n => n.Address == 1499 + sensor && n.Value == 1);
                var position = sequence.IndexOf(on);
                var wait = Assert.IsType<WaitVariableUntilNodeViewModel>(sequence[position + 1]);
                Assert.Equal($"Dut.sensor_{sensor}", wait.VariableName);
                Assert.Equal("1", wait.ExpectedValue);
                Assert.Equal("SelftestSnapshot", wait.PollAction);
                Assert.Equal(relay ? "/cgi-bin/luci/admin/statistics/deviceinfo?luci_username=admin&luci_password=admin" : "/test.shtml", wait.Endpoint);
                Assert.Equal(30000, wait.RequestTimeoutMs);
                Assert.Equal(60000, wait.TimeoutMs);
                Assert.Equal(1000, wait.IntervalMs);
                Assert.True(wait.FailOnTimeout);
                Assert.DoesNotContain(graph.Connections, c => ReferenceEquals(c.Source, wait.FalseOut));
                var off = Assert.IsType<ModbusWriteNodeViewModel>(sequence[position + 2]);
                Assert.Equal(on.Address, off.Address);
                Assert.Equal(0, off.Value);
            }
            var cleanup = CleanupGraph(vm);
            var cleanupWrites = cleanup.Nodes.OfType<ModbusWriteNodeViewModel>().Where(n => n.SlaveId == 21).ToArray();
            Assert.All(cleanupWrites, n => Assert.Equal(0, n.Value));
            Assert.Contains(cleanupWrites, n => n.Address == 1500);
            Assert.Contains(cleanupWrites, n => n.Address == 1501);
            foreach (var off in cleanupWrites)
                Assert.Contains(cleanup.Connections, c => ReferenceEquals(c.Source, off.FalseOut));
            if (relay)
            {
                var command = cleanup.Nodes.OfType<ReadHttpVariableNodeViewModel>().Single();
                Assert.EndsWith("set_mb_output=0", command.Endpoint);
                var reset = cleanupWrites.Single(n => n.Address == 1507);
                Assert.Contains(cleanup.Connections, c => ReferenceEquals(c.Source, command.TrueOut) && ReferenceEquals(c.Target.Parent, reset));
                Assert.DoesNotContain(cleanup.Connections, c => ReferenceEquals(c.Source, command.FalseOut) && ReferenceEquals(c.Target.Parent, reset));
            }
            Assert.NotNull(new GraphCompiler(service, NullLogger.Instance).Compile(vm.RootGraph));
            var json = GraphSerializer.Serialize(vm, file);
            Assert.DoesNotContain("Check IO-2 Sensors and Relay", json);
            GraphSerializer.Deserialize(json, vm);
            AssertConnectionsPreserved(JsonSerializer.Deserialize<GraphDto>(json)!, vm.RootGraph);
        }
    }

    [Theory]
    [InlineData("success")]
    [InlineData("web-timeout")]
    [InlineData("cancel")]
    [InlineData("reset-failure")]
    public async Task SensorGraphWaitsForFreshWebStateAndCleanupResetsBothOutputs(string outcome)
    {
        using var settingsService = new ModbusService();
        var vm = Load(FullProfile, settingsService);
        using var cancellation = new CancellationTokenSource();
        var stand = new FakeStand(outcome, cancellation);
        var compiler = new GraphCompiler(stand, stand, NullLogger.Instance);
        var compiled = compiler.Compile(SensorGraph(vm));
        // Keep the real profile transitions and real steps. Only replace browser I/O
        // with the existing direct-HTTP seam so this test never opens a browser/DUT.
        var testGraph = WithMockHttp(compiled.StartNode, stand);
        var context = new TestContext(new RegisterState());
        context.SetVariable("Dut.sensor_0", "0");
        context.SetVariable("Dut.sensor_1", "1"); // A stale success must not pass.
        if (outcome == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TestExecutor().ExecuteAsync(testGraph, context, cancellation.Token));
        else
        {
            var result = await new TestExecutor().ExecuteAsync(testGraph, context, cancellation.Token);
            Assert.Equal(outcome == "success" ? ExecutionStatus.Completed : ExecutionStatus.Failed, result);
        }
        if (outcome == "success")
        {
            Assert.Equal(4, stand.HttpCalls);
            Assert.Equal(new[] { (1500, 0), (1501, 0), (1500, 1), (1500, 0), (1501, 1), (1501, 0) }, stand.Io2Writes);
        }
        if (outcome == "reset-failure")
        {
            Assert.Equal(0, stand.HttpCalls);
            Assert.DoesNotContain(stand.Io2Writes, n => n.Value == 1);
        }
        var beforeCleanup = stand.Io2Writes.Count;
        var cleanup = compiler.Compile(CleanupGraph(vm));
        await new TestExecutor().ExecuteAsync(cleanup.StartNode, context, CancellationToken.None);
        Assert.Equal(new[] { (1500, 0), (1501, 0) }, stand.Io2Writes.Skip(beforeCleanup));
        Assert.Equal(0, stand.Values[(21, 1501)]);
        if (outcome != "reset-failure") Assert.Equal(0, stand.Values[(21, 1500)]);
    }

    [Theory]
    [InlineData(FullProfile, "0", StepResult.True)]
    [InlineData(FullProfile, "1", StepResult.False)]
    [InlineData("PSW_2G6F_plus_diagnostic_draft.json", "0", StepResult.True)]
    [InlineData("PSW_2G6F_plus_diagnostic_draft.json", "1", StepResult.False)]
    public async Task TamperCheckUsesInvertedExpectedValue(string file, string actual, StepResult expected)
    {
        using var service = new ModbusService();
        var vm = Load(file, service);
        var nodes = vm.RootGraph.Nodes.Concat(vm.RootGraph.Nodes.OfType<SubtestNodeViewModel>()
            .SelectMany(n => n.BodyGraph.Nodes));
        var check = nodes.OfType<CheckVariableEqualityNodeViewModel>().Single(n => n.VariableName == "Dut.sensor_0");
        Assert.Equal("0", check.ExpectedValue);
        var context = new TestContext(new RegisterState());
        context.SetVariable("Dut.sensor_0", actual);

        Assert.Equal(expected, await check.CreateStep(NullLogger.Instance).ExecuteAsync(context, CancellationToken.None));
        Assert.Equal(actual, context.GetVariable<string>("Dut.sensor_0"));
    }

    [Theory]
    [InlineData("Check IO-2 Sensors and Relay")]
    [InlineData("CHECK_IO2_SENSORS_AND_RELAY")]
    [InlineData("Проверка Sensor1, Sensor2 и реле")]
    public void RemovedNodeIsRejectedExplicitlyInsteadOfSilentlySkippingChecks(string type)
    {
        using var service = new ModbusService();
        var vm = new TestViewModel(service, new SlaveManager(service));
        var existing = new StartNodeViewModel();
        vm.RootGraph.Nodes.Add(existing);
        var json = $$$"""{"nodes":[{"id":"0","type":"Subtest","bodyGraph":{"nodes":[{"id":"old","type":"{{{type}}}"}],"connections":[]}}],"connections":[]} """;
        var error = Assert.Throws<InvalidOperationException>(() => GraphSerializer.Deserialize(json, vm));
        Assert.Contains("Импортируйте обновлённый профиль", error.Message);
        Assert.Contains(existing, vm.RootGraph.Nodes);
    }

    private static TestViewModel Load(string file, ModbusService service)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "profiles", file));
        var vm = new TestViewModel(service, new SlaveManager(service));
        var json = File.ReadAllText(path);
        GraphSerializer.Deserialize(json, vm);
        AssertConnectionsPreserved(JsonSerializer.Deserialize<GraphDto>(json)!, vm.RootGraph);
        return vm;
    }

    private static void AssertConnectionsPreserved(GraphDto dto, GraphWorkspaceViewModel graph)
    {
        Assert.Equal(dto.Nodes.Count, graph.Nodes.Count);
        Assert.Equal(dto.Connections.Count, graph.Connections.Count);
        for (var i = 0; i < dto.Nodes.Count; i++)
        {
            var body = dto.Nodes[i].BodyGraph ?? dto.Nodes[i].Body;
            if (body != null)
                AssertConnectionsPreserved(body, Assert.IsAssignableFrom<ICompositeNodeViewModel>(graph.Nodes[i]).BodyGraph);
        }
    }

    private static GraphWorkspaceViewModel SensorGraph(TestViewModel vm) => vm.RootGraph.Nodes
        .OfType<SubtestNodeViewModel>().Single(n => n.BodyGraph.Nodes.OfType<WaitVariableUntilNodeViewModel>().Any(w => w.VariableName == "Dut.sensor_1")).BodyGraph;

    private static GraphWorkspaceViewModel CleanupGraph(TestViewModel vm) => vm.RootGraph.Nodes
        .OfType<SubtestNodeViewModel>().Single(n => n.RunOnFailure && n.Name.StartsWith("Аварийное")).BodyGraph;

    private static List<NodeViewModel> SuccessPath(GraphWorkspaceViewModel graph)
    {
        var result = new List<NodeViewModel>();
        var current = graph.Nodes.Single(n => n is StartNodeViewModel);
        while (current is not EndNodeViewModel)
        {
            Assert.DoesNotContain(current, result);
            result.Add(current);
            current = graph.Connections.Single(c => ReferenceEquals(c.Source.Parent, current) && c.Source.Title != "False").Target.Parent!;
        }
        return result;
    }

    private static TestNode WithMockHttp(TestNode root, FakeStand stand)
    {
        var copies = new Dictionary<TestNode, TestNode>();
        TestNode? Copy(TestNode? node)
        {
            if (node == null) return null;
            if (copies.TryGetValue(node, out var existing)) return existing;
            var step = node.Source is WaitVariableUntilNodeViewModel wait
                ? new WaitVariableUntilStep(stand, NullLogger.Instance, wait.VariableName, wait.ExpectedValue,
                    wait.ComparisonType, wait.PollAction, wait.BaseUrl, wait.Endpoint, wait.ResponseType,
                    100, 500, 1, wait.FailOnTimeout, useBrowserForSelftest: false)
                : node.Step;
            var copy = new TestNode(step, node.Source);
            copies.Add(node, copy);
            copy.Next = Copy(node.Next);
            copy.OnTrue = Copy(node.OnTrue);
            copy.OnFalse = Copy(node.OnFalse);
            return copy;
        }
        return Copy(root)!;
    }

    private sealed class FakeStand(string outcome, CancellationTokenSource cancellation) : IModbusService, IHttpRequestService
    {
        public Dictionary<(byte Slave, ushort Address), ushort> Values { get; } = new() { [(21, 1500)] = 1, [(21, 1501)] = 1 };
        public List<(int Address, int Value)> Io2Writes { get; } = new();
        public int HttpCalls { get; private set; }
        private int _pollsSinceWrite;

        public Task<ushort[]> ReadRegistersAsync(byte slaveId, ushort address, ushort count, CancellationToken cancellationToken = default) =>
            Task.FromResult(new[] { Values.GetValueOrDefault((slaveId, address)) });

        public Task<bool> WriteRegisterAsync(byte slaveId, ushort address, ushort value, bool verify = true, CancellationToken cancellationToken = default)
        {
            if (slaveId == 21)
            {
                Io2Writes.Add((address, value));
                _pollsSinceWrite = 0;
                if (outcome == "reset-failure" && address == 1500 && value == 0) return Task.FromResult(false);
            }
            Values[(slaveId, address)] = value;
            return Task.FromResult(true);
        }

        public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout, CancellationToken cancellationToken)
        {
            HttpCalls++;
            Assert.Equal("http://192.168.0.1/test.shtml", url);
            if (outcome == "cancel") cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            var updated = ++_pollsSinceWrite > 1 && outcome != "web-timeout";
            var first = updated ? Values[(21, 1500)] : 0;
            var second = updated ? Values[(21, 1501)] : 0;
            var xml = $"<selftest><default_mac>c0:11:a6:05:00:00</default_mac><init_ok>1</init_ok><dev_type>6</dev_type><firmvare_vers>1</firmvare_vers><boot_vers>0</boot_vers><sensor_1>{first}</sensor_1><sensor_2>{second}</sensor_2></selftest>";
            return Task.FromResult(HttpRequestResult.Success(200, xml, TimeSpan.Zero));
        }

        public Task<bool> CheckPortAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public void SubscribeRegister(byte slaveId, ushort address, Action<ushort[]> callback) { }
    }
}
