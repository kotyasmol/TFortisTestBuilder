using System.Reflection;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.StepTests;

public class ErrorPolicyAuditTests
{
    public static IEnumerable<object[]> AtomicCases() => typeof(NodeViewModel).Assembly.GetTypes()
        .Where(type => !type.IsAbstract && typeof(NodeViewModel).IsAssignableFrom(type) && ErrorFlag(type) != null)
        .OrderBy(type => type.Name)
        .SelectMany(type => new[] { false, true }.SelectMany(preparation => new[] { false, true }
            .SelectMany(stop => new[] { false, true }.Select(parallel => new object[] { type.Name, preparation, stop, parallel }))));

    [Theory]
    [MemberData(nameof(AtomicCases))]
    public async Task EveryAtomicPolicy_ControlsRealFailureRouting(string nodeType, bool preparation, bool stop, bool parallel)
    {
        using var bus = new ModbusService();
        using var compiler = new GraphCompiler(bus, new RejectingHttp(), NullLogger.Instance);
        var node = FailingNode(nodeType);
        ErrorFlag(node.GetType())!.SetValue(node, stop);
        var graph = new GraphWorkspaceViewModel(); graph.Nodes.Add(node);
        var compiled = compiler.Compile(graph, node, singleNode: true).StartNode;
        var continued = false;
        var join = new TestNode(new Callback(_ => { continued = true; return StepResult.Stop; }));
        compiled.OnTrue = join;
        var root = parallel ? Fork(compiled, join) : compiled;
        var context = new TestContext(new RegisterState()) { IsPreparation = preparation };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var status = await new TestExecutor().ExecuteAsync(root, context, timeout.Token);

        Assert.Equal(!stop, continued);
        Assert.Equal(stop ? ExecutionStatus.Failed : ExecutionStatus.Completed, status);
        Assert.True(context.ReportEntries.Any(entry => !entry.IsSuccess) ||
            context.GetVariable<string>("ReportDelivery.Status") == "Failed");
        if (!stop)
        {
            Assert.False(context.HasCriticalError);
            Assert.False(context.HasExecutionException);
        }
    }

    public static IEnumerable<object[]> CompoundCases() => new[] { "Subtest", "Slaves", "Repeat" }
        .SelectMany(kind => new[] { false, true }.SelectMany(preparation => new[] { false, true }
            .SelectMany(stop => new[] { false, true }.Select(parallel => new object[] { kind, preparation, stop, parallel }))));

    [Theory]
    [MemberData(nameof(CompoundCases))]
    public async Task CriticalFailureWithConnectedFalsePath_RespectsCompoundPolicy(string kind, bool preparation, bool stop, bool parallel)
    {
        // Selftest marks a failure critical. A connected False path can finish the
        // body normally in a sequential run, but must not bypass the parent's policy.
        var body = new TestNode(new SelfTestCheckStep(new RejectingHttp(), NullLogger.Instance,
            "http://fixture.invalid", 1, "Dut", "init_ok=1..1", true, useBrowser: false,
            enforceMinimumDeviceReadyTimeout: false));
        body.OnFalse = new TestNode(new EndStep(NullLogger.Instance));
        var graph = new CompiledGraph(body);
        ITestStep compound = kind == "Subtest"
            ? new SubtestStep("Setup", true, stop, graph, NullLogger.Instance)
            : new ForEachSlaveStep(1, 3, 1, stop, graph, NullLogger.Instance,
                repeatOnSameSlave: kind == "Repeat", repeatCount: 3);
        var continued = false;
        var next = new TestNode(new Callback(_ => { continued = true; return StepResult.Stop; }));
        var root = new TestNode(compound) { OnTrue = next, Next = next };
        var context = new TestContext(new RegisterState()) { IsPreparation = preparation };

        var status = await new TestExecutor().ExecuteAsync(parallel ? Fork(root, next) : root, context, default);

        Assert.Equal(!stop, continued);
        Assert.Equal(stop ? ExecutionStatus.Failed : ExecutionStatus.Completed, status);
        Assert.Equal(stop, context.HasCriticalError);
        Assert.Contains(context.ReportEntries, entry => !entry.IsSuccess);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EveryErrorFlag_SurvivesCloneAndProfileRoundTrip(bool value)
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new TestBuilder.Domain.Modbus.SlaveManager(bus));
        var names = AtomicCases().Select(row => (string)row[0]).Distinct();
        foreach (var name in names)
        {
            var node = FailingNode(name);
            ErrorFlag(node.GetType())!.SetValue(node, value);
            vm.Nodes.Add(node);
        }
        vm.Nodes.Add(new SubtestNodeViewModel { StopOnError = value });
        vm.Nodes.Add(new ForEachSlaveNodeViewModel { StopOnError = value });
        foreach (var node in vm.Nodes)
            Assert.Equal(value, (ErrorFlag(node.GetType()) ?? node.GetType().GetProperty("StopOnError"))!.GetValue(node.Clone()));
        var json = GraphSerializer.Serialize(vm, "Error policies");
        GraphSerializer.Deserialize(json, vm);
        Assert.Equal(13, vm.Nodes.Count);
        foreach (var node in vm.Nodes)
            Assert.Equal(value, (ErrorFlag(node.GetType()) ?? node.GetType().GetProperty("StopOnError"))!.GetValue(node));
    }

    private static TestNode Fork(TestNode branch, TestNode join)
    {
        var sibling = new TestNode(new Callback(_ => StepResult.Next)) { Next = join };
        var root = new TestNode(null);
        root.ParallelTransitions[StepResult.Next] = new ParallelFork(new[] { branch, sibling }, join);
        return root;
    }

    private static PropertyInfo? ErrorFlag(Type type) => type.GetProperty("FailOnError") ??
        type.GetProperty("FailOnPrinterError") ?? type.GetProperty("FailOnTimeout");

    private static NodeViewModel FailingNode(string type) => type switch
    {
        nameof(SelfTestCheckNodeViewModel) => new SelfTestCheckNodeViewModel { TimeoutMs = 1, UseLegacyTimeout = false, Url = "http://[invalid" },
        nameof(ClearArpCacheNodeViewModel) => new ClearArpCacheNodeViewModel { RunArpdBat = false, Command = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing") },
        nameof(GetSerialNumberFromServerNodeViewModel) => new GetSerialNumberFromServerNodeViewModel { UseFixedSerialNumber = true, FixedSerialNumber = -1 },
        nameof(SetProMacNodeViewModel) => new SetProMacNodeViewModel(),
        nameof(SetPswMacNodeViewModel) => new SetPswMacNodeViewModel(),
        nameof(RunDataTestNodeViewModel) => new RunDataTestNodeViewModel { PortsText = "" },
        nameof(ReadHttpVariableNodeViewModel) => new ReadHttpVariableNodeViewModel { BaseUrl = "http://fixture.invalid" },
        nameof(BuildMacFromSerialNodeViewModel) => new BuildMacFromSerialNodeViewModel(),
        nameof(PrintLabelNodeViewModel) => new PrintLabelNodeViewModel(),
        nameof(SendTestReportNodeViewModel) => new SendTestReportNodeViewModel { SaveLocalCopy = false },
        nameof(WaitVariableUntilNodeViewModel) => new WaitVariableUntilNodeViewModel { PollAction = "None", TimeoutMs = 1, IntervalMs = 1 },
        _ => throw new InvalidOperationException("Add a deterministic failure scenario for " + type)
    };

    private sealed class RejectingHttp : IHttpRequestService
    {
        public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout, CancellationToken token) =>
            Task.FromResult(HttpRequestResult.Failure("Fixture unavailable", TimeSpan.Zero));
    }

    private sealed class Callback(Func<TestContext, StepResult> run) : ITestStep
    {
        public Task<StepResult> ExecuteAsync(TestContext context, CancellationToken token) => Task.FromResult(run(context));
    }
}
