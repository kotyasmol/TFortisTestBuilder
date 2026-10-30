using System.Net;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.StepTests;

public class ContinueOnErrorTests
{
    [Theory]
    [InlineData(false, true)] [InlineData(false, false)] [InlineData(true, false)]
    public async Task OfflinePrinter_ContinuesToReportAndPowerOff_AndReportStillFails(bool stopSubtest, bool stopPrinter)
    {
        using var bus = new ModbusService();
        using var compiler = new GraphCompiler(bus, NullLogger.Instance) { LabelPrinter = new OfflinePrinter() };
        var graph = new GraphWorkspaceViewModel();
        var subtest = new SubtestNodeViewModel { Name = "Печать", StopOnError = stopSubtest };
        var print = new PrintLabelNodeViewModel { UseManualSerialNumber = true, ManualSerialNumber = "12345678",
            FailOnPrinterError = stopPrinter, PrinterName = "Offline printer" };
        Fill(subtest.BodyGraph, new StartNodeViewModel(), print, new EndNodeViewModel());
        Fill(graph, new StartNodeViewModel(), subtest, new EndNodeViewModel());
        var compiled = compiler.Compile(graph);
        var compiledSubtest = compiled.StartNode.Next!;
        var errorBranch = false;
        compiledSubtest.OnFalse = Node((_, _) => { errorBranch = true; return StepResult.Next; });
        var report = new TestNode(new BuildTestReportStep(NullLogger.Instance, "Report", "stand", "SerialNumber", "", "test", false));
        var handler = new ReportHandler();
        var send = new TestNode(new SendTestReportStep(NullLogger.Instance, "http://report.test", "Report", "/result",
            1000, 0, 0, false, "", true, () => new HttpClient(handler)));
        var poweredOff = false;
        report.OnTrue = send;
        send.OnTrue = Node((_, _) => { poweredOff = true; return StepResult.Next; });
        compiledSubtest.OnTrue = report;
        var context = Context(); context.SetVariable("SerialNumber", 12345678);
        Assert.Equal(ExecutionStatus.Completed, await new TestExecutor().ExecuteAsync(compiled.StartNode, context, default));
        Assert.False(errorBranch); Assert.True(poweredOff); Assert.True(handler.Sent);
        Assert.Contains("test_result=true=0", context.GetVariable<string>("Report"));
        Assert.Contains("Печать этикеток=false=Принтер выключен", context.GetVariable<string>("Report"));
        Assert.False(context.GetVariable<bool>("BuildReport.DevicePassed"));
        Assert.False(context.HasCriticalError); Assert.False(context.HasExecutionException);
        Assert.NotNull(context.LabelPrinting.Last);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task OptionalSubtest_RestoresNestedFailureFlags_AndDoesNotCancelParallelSibling(bool throws)
    {
        var body = Node((context, _) =>
        {
            context.HasCriticalError = true; context.HasExecutionException = true;
            if (throws) throw new InvalidOperationException("Nested failure");
            return StepResult.False;
        });
        var optional = new TestNode(new SubtestStep("Optional", true, false, new(body), NullLogger.Instance));
        var joined = false; var siblingRan = false;
        var join = Node((_, _) => { joined = true; return StepResult.Next; });
        optional.OnTrue = join;
        var sibling = Node((_, _) => { siblingRan = true; return StepResult.Next; }); sibling.Next = join;
        var root = new TestNode(null);
        root.ParallelTransitions[StepResult.Next] = new(new[] { optional, sibling }, join);
        var context = Context();
        Assert.Equal(ExecutionStatus.Completed, await new TestExecutor().ExecuteAsync(root, context, default));
        Assert.True(joined); Assert.True(siblingRan);
        Assert.False(context.HasCriticalError); Assert.False(context.HasExecutionException);
        Assert.Null(context.Failure); Assert.Null(context.CriticalFailure);
        Assert.Contains(context.ReportEntries, entry => !entry.IsSuccess);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task OptionalLoop_ContinuesEveryIteration_AndNormalOutput(bool repeat, bool throws)
    {
        var iterations = 0;
        var body = Node((context, _) =>
        {
            iterations++; context.HasCriticalError = true; context.HasExecutionException = true;
            if (throws) throw new InvalidOperationException("Read failed");
            return StepResult.False;
        });
        var loop = new TestNode(new ForEachSlaveStep(1, 3, 1, false, new(body), NullLogger.Instance,
            repeatOnSameSlave: repeat, repeatCount: 3));
        var next = false; var error = false;
        loop.Next = Node((_, _) => { next = true; return StepResult.Next; });
        loop.OnFalse = Node((_, _) => { error = true; return StepResult.Next; });
        var context = Context(); context.CurrentSlaveId = 9; context.SetVariable("slaveId", (byte)9);
        Assert.Equal(ExecutionStatus.Completed, await new TestExecutor().ExecuteAsync(loop, context, default));
        Assert.Equal(3, iterations); Assert.True(next); Assert.False(error);
        Assert.Equal((byte)9, context.CurrentSlaveId); Assert.Equal((byte)9, context.GetVariable<byte>("slaveId"));
        Assert.False(context.HasCriticalError); Assert.False(context.HasExecutionException);
        Assert.Equal(3, context.ReportEntries.Count(entry => !entry.IsSuccess));
    }

    [Fact]
    public async Task OptionalNode_DoesNotEraseEarlierFailure_OrSwallowCancellationOrUnfinishedProcesses()
    {
        var context = Context(); context.HasCriticalError = true;
        var original = new ExecutionFailure("Earlier", "Earlier failure"); context.CriticalFailure = original;
        var optional = new SubtestStep("Optional", true, false, new(Node((_, _) => StepResult.False)), NullLogger.Instance);
        Assert.Equal(StepResult.True, await optional.ExecuteAsync(context, default));
        Assert.True(context.HasCriticalError); Assert.Same(original, context.CriticalFailure);
        using var cts = new CancellationTokenSource();
        var cancelled = new SubtestStep("Optional", true, false, new(Node((_, token) =>
        { cts.Cancel(); token.ThrowIfCancellationRequested(); return StepResult.Next; })), NullLogger.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.ExecuteAsync(Context(), cts.Token));
        var cleanup = new BrowserCleanupException("Browser still running", new Exception());
        var unsafeStep = new ContinueOnErrorStep(new SubtestStep("Optional", true, false,
            new(Node((_, _) => throw cleanup)), NullLogger.Instance), "Node", NullLogger.Instance);
        Assert.Same(cleanup, await Assert.ThrowsAsync<BrowserCleanupException>(() => unsafeStep.ExecuteAsync(Context(), default)));
    }

    [Fact]
    public async Task EveryAtomicErrorCheckbox_UsesTheSamePolicy_AndConditionalNodesStillBranch()
    {
        using var bus = new ModbusService();
        using var compiler = new GraphCompiler(bus, NullLogger.Instance);
        var types = typeof(NodeViewModel).Assembly.GetTypes().Where(type => !type.IsAbstract &&
            typeof(NodeViewModel).IsAssignableFrom(type) &&
            (type.GetProperty("FailOnError") != null || type.GetProperty("FailOnPrinterError") != null || type.GetProperty("FailOnTimeout") != null)).ToArray();
        Assert.NotEmpty(types);
        foreach (var type in types)
        {
            var node = (NodeViewModel)Activator.CreateInstance(type)!;
            var flag = type.GetProperty("FailOnError") ?? type.GetProperty("FailOnPrinterError") ?? type.GetProperty("FailOnTimeout")!;
            var graph = new GraphWorkspaceViewModel(); graph.Nodes.Add(node);
            flag.SetValue(node, false);
            Assert.IsType<ContinueOnErrorStep>(compiler.Compile(graph, node, true).StartNode.Step);
            flag.SetValue(node, true);
            Assert.IsNotType<ContinueOnErrorStep>(compiler.Compile(graph, node, true).StartNode.Step);
        }
        var check = new CheckVariableEqualityNodeViewModel { VariableName = "missing" };
        var conditional = new GraphWorkspaceViewModel(); conditional.Nodes.Add(check);
        Assert.Equal(ExecutionStatus.Failed, await new TestExecutor().ExecuteAsync(compiler.Compile(conditional, check, true).StartNode, Context(), default));
    }

    [Fact]
    public async Task UncheckedHttpNode_HandlesTransportException_AndRecordsIt()
    {
        using var bus = new ModbusService();
        using var compiler = new GraphCompiler(bus, new ThrowingHttp(), NullLogger.Instance);
        var node = new ReadHttpVariableNodeViewModel { FailOnError = false, BaseUrl = "http://dut.test" };
        var graph = new GraphWorkspaceViewModel(); graph.Nodes.Add(node);
        var context = Context();
        Assert.Equal(ExecutionStatus.Completed, await new TestExecutor().ExecuteAsync(compiler.Compile(graph, node, true).StartNode, context, default));
        Assert.Contains(context.ReportEntries, entry => !entry.IsSuccess && entry.Value.Contains("HTTP failed"));
    }

    private static TestContext Context() => new(new RegisterState());
    private static TestNode Node(Func<TestContext, CancellationToken, StepResult> run) => new(new Callback(run));
    private sealed class Callback(Func<TestContext, CancellationToken, StepResult> run) : ITestStep
    { public Task<StepResult> ExecuteAsync(TestContext context, CancellationToken token) => Task.FromResult(run(context, token)); }
    private sealed class OfflinePrinter : IRawLabelPrinter
    { public RawLabelPrintResult Print(string name, byte[] bytes, CancellationToken token) => RawLabelPrintResult.Fail(3, "Принтер выключен"); }
    private sealed class ThrowingHttp : IHttpRequestService
    { public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout, CancellationToken token) => throw new InvalidOperationException("HTTP failed"); }
    private sealed class ReportHandler : HttpMessageHandler
    {
        public bool Sent;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Sent = true; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok") }); }
    }
    private static void Fill(GraphWorkspaceViewModel graph, NodeViewModel start, NodeViewModel step, NodeViewModel end)
    {
        graph.Clear(); graph.Nodes.Add(start); graph.Nodes.Add(step); graph.Nodes.Add(end);
        graph.Connections.Add(new(start.Output[0], step.Input[0]));
        graph.Connections.Add(new(step.Output[0], end.Input[0]));
    }
}
