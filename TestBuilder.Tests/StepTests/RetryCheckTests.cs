using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.StepTests;

public class RetryCheckTests
{
    private static RetryCheckStep Retry(ITestStep step, int attempts = 2, int delay = 0) =>
        new(new CompiledGraph(new TestNode(step)), NullLogger.Instance, attempts, delay);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FirstFailureThenSuccessDiscardsOnlyThisBlocksIntermediateResult(bool earlierFailure)
    {
        var context = new TestContext(new RegisterState());
        context.AddReportEntry("Другая проверка", !earlierFailure, "раньше");
        var attempts = 0;
        var step = Retry(new ActionStep((ctx, _) =>
        {
            var success = ++attempts == 2;
            ctx.AddReportEntry("Проверка", success, $"попытка {attempts}");
            if (!success) { ctx.HasCriticalError = true; ctx.HasExecutionException = true; }
            return Task.FromResult(success ? StepResult.Next : StepResult.False);
        }));
        Assert.Equal(StepResult.Next, await step.ExecuteAsync(context, default));
        Assert.Equal(2, attempts); Assert.False(context.HasCriticalError); Assert.False(context.HasExecutionException);
        Assert.Equal(2, context.ReportEntries.Count); Assert.True(context.ReportEntries[^1].IsSuccess);
        Assert.Equal(!earlierFailure, context.ReportEntries[0].IsSuccess);
        Assert.Empty(context.Variables); // The dedicated block does not overwrite a user's counter.
        context.SetVariable("SerialNumber", 3300001);
        var report = new BuildTestReportStep(NullLogger.Instance, "Report", "Stand", "SerialNumber", "", "production", false);
        Assert.Equal(StepResult.True, await report.ExecuteAsync(context, default));
        Assert.Contains("test_result=true=" + (earlierFailure ? "0" : "1"), context.GetVariable<string>("Report"));
    }

    [Fact]
    public async Task PreviouslyCriticalFailureIsNeverClearedBySuccessfulRetry()
    {
        var context = new TestContext(new RegisterState()) { HasCriticalError = true };
        Assert.Equal(StepResult.Next, await Retry(new ActionStep((_, _) => Task.FromResult(StepResult.Next))).ExecuteAsync(context, default));
        Assert.True(context.HasCriticalError);
    }

    [Fact]
    public async Task ExhaustedAttemptCannotBeTurnedIntoSuccessByItsFalsePath()
    {
        var context = new TestContext(new RegisterState()); var runs = 0;
        var first = new TestNode(new ActionStep((ctx, _) =>
        {
            runs++; ctx.AddReportEntry("Проверка", false, "не пройдена");
            return Task.FromResult(StepResult.False);
        }));
        first.OnFalse = new TestNode(new ActionStep((_, _) => Task.FromResult(StepResult.Next)));
        var retry = new RetryCheckStep(new(first), NullLogger.Instance, 2, 0);
        Assert.Equal(StepResult.False, await retry.ExecuteAsync(context, default));
        Assert.Equal(2, runs); Assert.False(Assert.Single(context.ReportEntries).IsSuccess);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancellationAndUnsafeCleanupAreNotRetried(bool unsafeCleanup)
    {
        using var cts = new CancellationTokenSource(); var runs = 0;
        var step = Retry(new ActionStep((_, token) =>
        {
            runs++;
            if (unsafeCleanup) throw new ExecutionCleanupException("Процесс ещё работает");
            cts.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult(StepResult.Next);
        }));
        if (unsafeCleanup) await Assert.ThrowsAsync<ExecutionCleanupException>(() => step.ExecuteAsync(new(new RegisterState()), cts.Token));
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => step.ExecuteAsync(new(new RegisterState()), cts.Token));
        Assert.Equal(1, runs);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExhaustedCriticalCheckOrTransportExceptionReachesTheBlocksErrorPath(bool transportException)
    {
        var runs = 0; var handled = 0; var context = new TestContext(new RegisterState());
        var retry = new TestNode(Retry(new ActionStep((ctx, _) =>
        {
            runs++;
            if (transportException) throw new System.IO.IOException("Нет ответа");
            ctx.HasCriticalError = true; ctx.AddReportEntry("Проверка", false, "Не пройдена");
            return Task.FromResult(StepResult.False);
        })));
        retry.OnFalse = new TestNode(new ActionStep((_, _) => { handled++; return Task.FromResult(StepResult.Next); }));
        Assert.Equal(ExecutionStatus.Completed, await new TestExecutor().ExecuteAsync(retry, context, default));
        Assert.Equal(2, runs); Assert.Equal(1, handled);
        Assert.False(context.HasCriticalError); Assert.False(context.HasExecutionException);
        Assert.False(Assert.Single(context.ReportEntries).IsSuccess);
    }

    [Theory]
    [InlineData(true, false)] [InlineData(false, false)]
    [InlineData(true, true)] [InlineData(false, true)]
    public async Task CompiledNodeRoutesSuccessAndExhaustionAndPreservesTheReport(bool secondAttemptPasses, bool parallel)
    {
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        var retry = new RetryCheckNodeViewModel { DelayMs = 0 };
        var check = new CheckRegisterRangeNodeViewModel { SlaveId = 1, Address = 50, Min = 1, Max = 1, ReportName = "Контроль" };
        Fill(retry.BodyGraph, new BodyStartNodeViewModel(), check, new BodyEndNodeViewModel());
        var end = new EndNodeViewModel();
        Fill(vm.RootGraph, new StartNodeViewModel(), retry, end);
        if (parallel)
        {
            var sibling = new DelayNodeViewModel { Milliseconds = 0 }; vm.Nodes.Add(sibling);
            vm.Connections.Add(new(vm.Nodes[0].Output[0], sibling.Input[0]));
            vm.Connections.Add(new(sibling.Output[0], end.Input[0]));
        }
        // The negative route continues execution, but must retain a failed product result.
        vm.Connections.Add(new(retry.ErrorOut, end.Input[0]));
        var json = GraphSerializer.Serialize(vm, "Повтор"); GraphSerializer.Deserialize(json, vm);
        Assert.Contains("Retry Check (v1)", json);
        var loaded = Assert.Single(vm.Nodes.OfType<RetryCheckNodeViewModel>());
        var observer = new CheckObserver(secondAttemptPasses);
        var context = new TestContext(new RegisterState()) { ExecutionObserver = observer };
        using var compiler = new GraphCompiler(bus, NullLogger.Instance);
        Assert.Equal(ExecutionStatus.Completed, await new TestExecutor().ExecuteAsync(compiler.Compile(vm.RootGraph).StartNode, context, default));
        Assert.Equal(2, observer.Attempts);
        Assert.Equal(secondAttemptPasses ? StepResult.Next : StepResult.False, observer.BlockResult);
        Assert.Equal(secondAttemptPasses, Assert.Single(context.ReportEntries).IsSuccess);
        Assert.Equal(2, loaded.MaxAttempts);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(100001, 0)] [InlineData(2, -1)] [InlineData(2, 3600001)]
    public void InvalidRetrySettingsAreRejectedBeforeExecution(int attempts, int delay)
    {
        using var bus = new ModbusService(); using var compiler = new GraphCompiler(bus, NullLogger.Instance);
        var graph = new GraphWorkspaceViewModel();
        Fill(graph, new StartNodeViewModel(), new RetryCheckNodeViewModel { MaxAttempts = attempts, DelayMs = delay }, new EndNodeViewModel());
        Assert.Contains("Повтор проверки", Assert.Throws<InvalidOperationException>(() => compiler.Compile(graph)).Message);
    }

    [Fact]
    public void ClipboardPreservesNestedBodyAndSettingsWithoutConvertingLegacyLoops()
    {
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        var retry = new RetryCheckNodeViewModel { MaxAttempts = 4, DelayMs = 25, CustomName = "Проверка портов" };
        Fill(retry.BodyGraph, new BodyStartNodeViewModel(), new DelayNodeViewModel { Milliseconds = 7 }, new BodyEndNodeViewModel());
        vm.Nodes.Clear(); vm.Nodes.Add(retry); vm.RootGraph.SelectedNodes.Add(retry); vm.CopyNodes(); vm.PasteNodes();
        var copy = vm.Nodes.OfType<RetryCheckNodeViewModel>().Last();
        Assert.NotSame(retry.BodyGraph, copy.BodyGraph); Assert.Equal(4, copy.MaxAttempts); Assert.Equal(25, copy.DelayMs);
        Assert.Equal(7, Assert.Single(copy.BodyGraph.Nodes.OfType<DelayNodeViewModel>()).Milliseconds);
        Assert.Equal(2, copy.BodyGraph.Connections.Count);
        vm.Nodes.Add(new ForEachSlaveNodeViewModel { RepeatOnSameSlave = true, UntilSuccess = true, RepeatCount = 5 });
        GraphSerializer.Deserialize(GraphSerializer.Serialize(vm, "Copy"), vm);
        Assert.Equal(2, vm.Nodes.OfType<RetryCheckNodeViewModel>().Count());
        Assert.Equal(5, Assert.Single(vm.Nodes.OfType<ForEachSlaveNodeViewModel>()).RepeatCount);
    }

    [Fact]
    public void RetryBodyCannotBypassParallelNetworkResourceValidation()
    {
        using var bus = new ModbusService(); using var compiler = new GraphCompiler(bus, NullLogger.Instance);
        var retry = new RetryCheckNodeViewModel();
        Fill(retry.BodyGraph, new BodyStartNodeViewModel(), new SelfTestCheckNodeViewModel(), new BodyEndNodeViewModel());
        var start = new StartNodeViewModel(); var end = new EndNodeViewModel(); var other = new ReadHttpVariableNodeViewModel();
        var graph = new GraphWorkspaceViewModel(); Fill(graph, start, retry, end);
        graph.Nodes.Add(other); graph.Connections.Add(new(start.Output[0], other.Input[0])); graph.Connections.Add(new(other.TrueOut, end.Input[0]));
        Assert.Contains("сети DUT", Assert.Throws<InvalidOperationException>(() => compiler.Compile(graph)).Message);
    }

    private static void Fill(GraphWorkspaceViewModel graph, params NodeViewModel[] nodes)
    {
        graph.Clear(); foreach (var node in nodes) graph.Nodes.Add(node);
        for (var i = 1; i < nodes.Length; i++) graph.Connections.Add(new(nodes[i - 1].Output[0], nodes[i].Input[0]));
    }

    private sealed class CheckObserver(bool passes) : IExecutionObserver
    {
        public int Attempts { get; private set; }
        public StepResult BlockResult { get; private set; }
        public Task NodeStartedAsync(TestNode node, TestContext context, CancellationToken token)
        {
            if (node.Source is CheckRegisterRangeNodeViewModel)
                context.RegisterState.Update(1, 50, (ushort)(++Attempts == 2 && passes ? 1 : 0));
            return Task.CompletedTask;
        }
        public Task NodeCompletedAsync(TestNode node, StepResult result, TestContext context, CancellationToken token)
        { if (node.Source is RetryCheckNodeViewModel) BlockResult = result; return Task.CompletedTask; }
        public Task NodeFinishedAsync(TestNode node, TestContext context, CancellationToken token) => Task.CompletedTask;
        public Task NodeFailedAsync(TestNode node, TestContext context, CancellationToken token) => Task.CompletedTask;
    }
    private sealed class ActionStep(Func<TestContext, CancellationToken, Task<StepResult>> action) : ITestStep
    { public Task<StepResult> ExecuteAsync(TestContext context, CancellationToken token) => action(context, token); }
}
