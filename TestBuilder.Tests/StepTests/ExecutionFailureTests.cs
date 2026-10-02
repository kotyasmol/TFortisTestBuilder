using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.StepTests;

public class ExecutionFailureTests
{
    [Fact]
    public async Task CompiledNestedLoop_PreservesLeafReasonRegisterNameAndActualSlave()
    {
        using var modbus = new ModbusService();
        using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        var subtest = new SubtestNodeViewModel { Name = "Проверка нагревателей" };
        var loop = new ForEachSlaveNodeViewModel { FromSlaveId = 1, ToSlaveId = 3, Step = 2 };
        var check = new CheckRegisterRangeNodeViewModel { UseCurrentSlaveId = true, Address = 1215, Min = 1, Max = 1 };
        Fill(loop.BodyGraph, new BodyStartNodeViewModel(), check, new BodyEndNodeViewModel());
        Fill(subtest.BodyGraph, new StartNodeViewModel(), loop, new EndNodeViewModel());
        Fill(vm.RootGraph, new StartNodeViewModel(), subtest, new EndNodeViewModel());
        var context = new TestContext(new RegisterState())
        {
            ModbusRegisters = new ModbusRegisterCatalog(new[] { new PS2Model(3, modbus) })
        };
        context.RegisterState.Update(1, 1215, 1);
        context.RegisterState.Update(3, 1215, 0);
        using var compiler = new GraphCompiler(modbus, NullLogger.Instance);

        Assert.Equal(ExecutionStatus.Failed, await Execute(compiler.Compile(vm.RootGraph).StartNode, context));
        Assert.NotNull(context.Failure);
        Assert.Contains("Проверка нагревателей", context.Failure.StepName);
        Assert.EndsWith("Проверка диапазона", context.Failure.StepName);
        Assert.Equal((byte)3, context.Failure.SlaveId);
        Assert.Contains("0 вне диапазона [1..1]", context.Failure.Reason);
        Assert.Contains("1215 (Реле нагревателя)", context.Failure.Reason);
        Assert.Null(context.CurrentSlaveId);
        Assert.Same(context.Failure, context.CriticalFailure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandledFalse_DoesNotLeakIntoLaterResult(bool laterFails)
    {
        var logger = new StepDiagnosticLogger(NullLogger.Instance);
        var condition = Node("Condition", (_, _) =>
        {
            logger.Warning("Not matched, take the alternative path");
            return Task.FromResult(StepResult.False);
        });
        condition.OnFalse = Node("Actual step", (_, _) =>
        {
            logger.Warning("Actual step's diagnostic");
            return Task.FromResult(laterFails ? StepResult.False : StepResult.True);
        });
        var context = Context();
        Assert.Equal(laterFails ? ExecutionStatus.Failed : ExecutionStatus.Completed, await Execute(condition, context));
        if (laterFails)
        {
            Assert.Equal("Actual step", context.Failure?.StepName);
            Assert.Equal("Actual step's diagnostic", context.Failure?.Reason);
        }
        else Assert.Null(context.Failure);
        Assert.Null(context.CriticalFailure);
    }

    [Fact]
    public async Task ParallelFailure_IsIsolatedFromSiblingLogsAndCancellation()
    {
        var logger = new StepDiagnosticLogger(NullLogger.Instance);
        var ownDiagnostic = Signal();
        var siblingDiagnostic = Signal();
        var first = Node("Voltage", async (ctx, _) =>
        {
            ctx.CurrentSlaveId = 7;
            logger.Warning("[ОШИБКА] Ожидалось 53000..57000, получено 0");
            ownDiagnostic.SetResult();
            await siblingDiagnostic.Task;
            return StepResult.False;
        });
        var sibling = Node("Sibling", async (_, ct) =>
        {
            await ownDiagnostic.Task;
            logger.Warning("Unrelated retry from sibling");
            siblingDiagnostic.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { logger.Error("Sibling was cancelled during cleanup"); }
            return StepResult.Next;
        });
        var join = new TestNode(null);
        first.OnTrue = sibling.Next = join;
        var start = new TestNode(null);
        // The failing branch is intentionally second: a cancelled first branch is not the cause.
        start.ParallelTransitions[StepResult.Next] = new ParallelFork(new[] { sibling, first }, join);
        var context = Context();

        Assert.Equal(ExecutionStatus.Failed, await Execute(start, context));
        Assert.Equal("Voltage", context.Failure?.StepName);
        Assert.Equal((byte)7, context.Failure?.SlaveId);
        Assert.Equal("[ОШИБКА] Ожидалось 53000..57000, получено 0", context.Failure?.Reason);
    }

    [Fact]
    public async Task NestedException_StaysAttachedToOriginalStep_AfterErrorPath()
    {
        var logger = new StepDiagnosticLogger(NullLogger.Instance);
        var leaf = Node("Read register", (_, _) => throw new InvalidOperationException("COM connection closed"));
        var parent = new TestNode(new SubtestStep("Power", true, true, new CompiledGraph(leaf), logger))
        {
            DisplayName = "Power",
            OnFalse = new TestNode(null)
        };
        var context = Context();

        Assert.Equal(ExecutionStatus.Completed, await Execute(parent, context));
        Assert.Null(context.Failure); // Error was handled, but the product still failed the critical check.
        Assert.Equal("Read register", context.CriticalFailure?.StepName);
        Assert.Equal("COM connection closed", context.CriticalFailure?.Reason);
    }

    [Fact]
    public async Task UserCancellation_IsNotReportedAsStepFailure()
    {
        var started = Signal();
        var context = Context();
        var node = Node("Wait", async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return StepResult.Next;
        });
        using var cts = new CancellationTokenSource();
        var task = new TestExecutor().ExecuteAsync(node, context, cts.Token);
        await started.Task;
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Null(context.Failure);
        Assert.Null(context.CriticalFailure);
    }

    private static void Fill(GraphWorkspaceViewModel graph, NodeViewModel start, NodeViewModel step, NodeViewModel end)
    {
        graph.Clear();
        graph.Nodes.Add(start); graph.Nodes.Add(step); graph.Nodes.Add(end);
        graph.Connections.Add(new ConnectionViewModel(start.Output[0], step.Input[0]));
        graph.Connections.Add(new ConnectionViewModel(step.Output[0], end.Input[0]));
    }

    private static TestContext Context() => new(new RegisterState());
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TestNode Node(string name, Func<TestContext, CancellationToken, Task<StepResult>> action) =>
        new(new Step(action)) { DisplayName = name };
    private static Task<ExecutionStatus> Execute(TestNode node, TestContext context) =>
        new TestExecutor().ExecuteAsync(node, context, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
    private sealed class Step(Func<TestContext, CancellationToken, Task<StepResult>> action) : ITestStep
    {
        public Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken) => action(context, cancellationToken);
    }
}
