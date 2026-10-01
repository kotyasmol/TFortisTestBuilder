using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class ParallelExecutionTests
{
    [Fact]
    public async Task Fork_OverlapsBothBranches_IsolatesVariables_AndRunsJoinOnce()
    {
        var bothStarted = Signal();
        var release = Signal();
        var started = 0;
        var joined = 0;
        var context = Context();
        context.CurrentSlaveId = 7;
        context.SetVariable("slaveId", (byte)7);
        var scopes = new List<Guid>();
        TestNode Branch(string variable) => Node(async (branch, ct) =>
        {
            Assert.True(branch.IsParallelBranch);
            Assert.Equal(context.ExecutionScopeId, branch.ParentExecutionScopeId);
            Assert.Equal(ct, branch.CancellationToken);
            Assert.Equal((byte)7, branch.CurrentSlaveId);
            branch.CurrentSlaveId = (byte)(variable == "A" ? 1 : 2);
            branch.SetVariable("slaveId", branch.CurrentSlaveId.Value);
            scopes.Add(branch.ExecutionScopeId);
            branch.SetVariable(variable, true);
            if (Interlocked.Increment(ref started) == 2) bothStarted.SetResult();
            await release.Task.WaitAsync(ct);
            return StepResult.Next;
        });
        var join = Node((ctx, _) =>
        {
            joined++;
            Assert.True(ctx.GetVariable<bool>("A"));
            Assert.True(ctx.GetVariable<bool>("B"));
            Assert.False(ctx.IsParallelBranch);
            Assert.Equal((byte)7, ctx.CurrentSlaveId);
            Assert.Equal((byte)7, ctx.GetVariable<byte>("slaveId"));
            return Task.FromResult(StepResult.Next);
        });
        var first = Branch("A");
        var second = Branch("B");
        first.Next = second.Next = join;
        var execution = Execute(Fork(join, first, second), context);
        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(context.Variables.ContainsKey("A"));
        Assert.False(execution.IsCompleted);
        release.SetResult();
        Assert.Equal(ExecutionStatus.Completed, await execution);
        Assert.Equal(1, joined);
        Assert.Equal(2, scopes.Distinct().Count());
    }

    [Fact]
    public async Task Reports_AreMergedInBranchOrder_NotCompletionOrder()
    {
        var secondDone = Signal();
        var context = Context();
        context.AddReportEntry("Before", true, "true");
        var first = Node(async (ctx, _) =>
        {
            await secondDone.Task;
            ctx.AddReportEntry("First", true, "true");
            return StepResult.Next;
        });
        var second = Node((ctx, _) =>
        {
            ctx.AddReportEntry("Second", true, "true");
            secondDone.SetResult();
            return Task.FromResult(StepResult.Next);
        });
        var join = Node((ctx, _) =>
        {
            ctx.AddReportEntry("After", true, "true");
            return Task.FromResult(StepResult.Next);
        });
        first.Next = second.Next = join;
        await Execute(Fork(join, first, second), context);
        Assert.Equal(new[] { "Before", "First", "Second", "After" }, context.ReportEntries.Select(entry => entry.Name));
    }

    [Fact]
    public async Task UnhandledFalse_CancelsSibling_AndDrainsBeforeReturningFailure()
    {
        var siblingStarted = Signal();
        var cleanupStarted = Signal();
        var cleanupRelease = Signal();
        var joined = false;
        var first = Node(async (_, _) => { await siblingStarted.Task; return StepResult.False; });
        var second = BlockingBranch(siblingStarted, cleanupStarted, cleanupRelease);
        var join = Node((_, _) => { joined = true; return Task.FromResult(StepResult.Next); });
        second.Next = join;
        var context = Context();
        var execution = Execute(Fork(join, first, second), context);
        await cleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(execution.IsCompleted);
        Assert.False(joined);
        cleanupRelease.SetResult();
        Assert.Equal(ExecutionStatus.Failed, await execution);
        Assert.True(context.HasCriticalError);
        Assert.Equal(new[] { ExecutionStatus.Failed, ExecutionStatus.Cancelled }, context.ParallelBranchResults.Select(result => result.Status));
    }

    [Fact]
    public async Task Exception_IsPreserved_AfterSiblingCleanup()
    {
        var started = Signal();
        var cleanup = Signal();
        var release = Signal();
        var cause = new InvalidOperationException("Hardware disconnected");
        var first = Node(async (_, _) => { await started.Task; throw cause; });
        var second = BlockingBranch(started, cleanup, release);
        var join = Node();
        second.Next = join;
        var execution = Execute(Fork(join, first, second));
        await cleanup.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(execution.IsCompleted);
        release.SetResult();
        Assert.Same(cause, await Assert.ThrowsAsync<InvalidOperationException>(() => execution));
    }

    [Fact]
    public async Task ObserverFailure_DoesNotHideTheUnderlyingStepException()
    {
        var cause = new InvalidOperationException("Hardware disconnected");
        var context = Context();
        context.ExecutionObserver = new FailingObserver();
        var error = await Assert.ThrowsAsync<AggregateException>(() => Execute(Node((_, _) => throw cause), context));
        Assert.Contains(cause, error.Flatten().InnerExceptions);
        Assert.Contains(error.Flatten().InnerExceptions, exception => exception.Message == "Observer failure");
    }

    [Fact]
    public async Task UserCancellation_DrainsAllBranches_AndFinishesObserversWithUncancelledTokens()
    {
        var firstStarted = Signal();
        var secondStarted = Signal();
        var firstCleanup = Signal();
        var secondCleanup = Signal();
        var release = Signal();
        var first = BlockingBranch(firstStarted, firstCleanup, release);
        var second = BlockingBranch(secondStarted, secondCleanup, release);
        var join = Node();
        first.Next = second.Next = join;
        var observer = new Observer();
        var context = Context();
        context.ExecutionObserver = observer;
        using var cts = new CancellationTokenSource();
        var execution = Execute(Fork(join, first, second), context, cts.Token);
        await Task.WhenAll(firstStarted.Task, secondStarted.Task).WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await Task.WhenAll(firstCleanup.Task, secondCleanup.Task).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(execution.IsCompleted);
        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.Equal(2, observer.BranchesFinished);
        Assert.Equal(3, observer.NodesFinished); // fork and both branch heads; join is never entered
        Assert.False(observer.TerminalTokenWasCancelled);
        Assert.Equal(0, observer.NodesFailed);
    }

    [Fact]
    public async Task HandledFalse_IsAnAlternativePath_AndDoesNotCancelSibling()
    {
        var join = Node();
        var recovered = false;
        var alternate = Node((_, _) => { recovered = true; return Task.FromResult(StepResult.Next); });
        alternate.Next = join;
        var condition = Node((_, _) => Task.FromResult(StepResult.False));
        condition.OnFalse = alternate;
        var sibling = Node();
        sibling.Next = join;
        var context = Context();
        Assert.Equal(ExecutionStatus.Completed, await Execute(Fork(join, condition, sibling), context));
        Assert.True(recovered);
        Assert.False(context.HasCriticalError);
    }

    [Fact]
    public async Task CriticalFailure_CancelsEvenWhenAnErrorTransitionExists()
    {
        var recoveryRan = false;
        var recovery = Node((_, _) => { recoveryRan = true; return Task.FromResult(StepResult.Next); });
        var join = Node();
        recovery.Next = join;
        var first = Node((ctx, _) => { ctx.HasCriticalError = true; return Task.FromResult(StepResult.False); });
        first.OnFalse = recovery;
        var sibling = Node();
        sibling.Next = join;
        Assert.Equal(ExecutionStatus.Failed, await Execute(Fork(join, first, sibling)));
        Assert.False(recoveryRan);
    }

    [Fact]
    public async Task NestedForks_JoinOnce_AndPropagateResultsToTheOuterScope()
    {
        var innerJoinCount = 0;
        var outerJoinCount = 0;
        var context = Context();
        var outerJoin = Node((ctx, _) =>
        {
            outerJoinCount++;
            Assert.Equal(1, ctx.GetVariable<int>("A"));
            Assert.Equal(2, ctx.GetVariable<int>("B"));
            Assert.Equal(3, ctx.GetVariable<int>("C"));
            return Task.FromResult(StepResult.Next);
        });
        var innerJoin = Node((ctx, _) =>
        {
            innerJoinCount++;
            Assert.True(ctx.IsParallelBranch);
            return Task.FromResult(StepResult.Next);
        });
        innerJoin.Next = outerJoin;
        var a = Write("A", 1); a.Next = innerJoin;
        var b = Write("B", 2); b.Next = innerJoin;
        var c = Write("C", 3); c.Next = outerJoin;
        Assert.Equal(ExecutionStatus.Completed, await Execute(Fork(outerJoin, Fork(innerJoin, a, b), c), context));
        Assert.Equal(1, innerJoinCount);
        Assert.Equal(1, outerJoinCount);
        Assert.Equal(4, context.ParallelBranchResults.Count);
    }

    [Fact]
    public async Task ConflictingWrites_IncludingAnExplicitWriteOfInitialValue_FailBeforeJoin()
    {
        var context = Context(); context.SetVariable("Shared", 0);
        var joined = false;
        var join = Node((_, _) => { joined = true; return Task.FromResult(StepResult.Next); });
        var a = Write("Shared", 0); a.Next = join;
        var b = Write("Shared", 1); b.Next = join;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Execute(Fork(join, a, b), context));
        Assert.Contains("Shared", error.Message);
        Assert.False(joined);
        Assert.True(context.HasCriticalError);
        Assert.Equal(0, context.GetVariable<int>("Shared"));
    }

    [Fact]
    public async Task IdenticalWrites_AreMerged_AndLastCheckRemainsOnlyInBranchDiagnostics()
    {
        var context = Context();
        var join = Node();
        var a = Node((ctx, _) =>
        {
            ctx.SetVariable("Shared", 12);
            ctx.SetVariable("LastCheck.ActualValue", 1);
            return Task.FromResult(StepResult.Next);
        });
        var b = Node((ctx, _) =>
        {
            ctx.SetVariable("Shared", 12);
            ctx.SetVariable("LastCheck.ActualValue", 2);
            return Task.FromResult(StepResult.Next);
        });
        a.Next = b.Next = join;
        await Execute(Fork(join, a, b), context);
        Assert.Equal(12, context.GetVariable<int>("Shared"));
        Assert.False(context.Variables.ContainsKey("LastCheck.ActualValue"));
        Assert.Equal(new[] { 1, 2 }, context.ParallelBranchResults.Select(result => (int)result.Variables["LastCheck.ActualValue"]));
    }

    [Fact]
    public async Task MutableValues_AreDeepCopied_AndOnlyChangedContainersAreMerged()
    {
        var context = Context();
        context.SetVariable("Map", new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase) { ["key"] = new() { 1 } });
        var updated = Signal();
        var join = Node();
        var a = Node((ctx, _) =>
        {
            ctx.GetVariable<Dictionary<string, List<int>>>("Map")!["KEY"].Add(2);
            updated.SetResult();
            return Task.FromResult(StepResult.Next);
        });
        var b = Node(async (ctx, _) =>
        {
            await updated.Task;
            Assert.Equal(new[] { 1 }, ctx.GetVariable<Dictionary<string, List<int>>>("Map")!["KEY"]);
            Assert.Equal(new[] { 1 }, context.GetVariable<Dictionary<string, List<int>>>("Map")!["key"]);
            return StepResult.Next;
        });
        a.Next = b.Next = join;
        await Execute(Fork(join, a, b), context);
        Assert.Equal(new[] { 1, 2 }, context.GetVariable<Dictionary<string, List<int>>>("Map")!["key"]);
        context.GetVariable<Dictionary<string, List<int>>>("Map")!["key"].Add(3);
        Assert.Equal(new[] { 1, 2 }, ((Dictionary<string, List<int>>)context.ParallelBranchResults[0].Variables["Map"])["key"]);
    }

    [Fact]
    public async Task CyclicBranchOutput_IsRejectedWithoutLosingReportsOrOverflowing()
    {
        var context = Context();
        TestNode Branch(string name) => Node((ctx, _) =>
        {
            var cycle = new List<object>(); cycle.Add(cycle);
            ctx.SetVariable("Cycle", cycle);
            ctx.AddReportEntry(name, true, "true");
            return Task.FromResult(StepResult.Next);
        });
        var a = Branch("A"); var b = Branch("B"); var join = Node(); a.Next = b.Next = join;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Execute(Fork(join, a, b), context));
        Assert.Contains("Cycle", error.Message);
        Assert.Equal(2, context.ReportEntries.Count);
        Assert.True(context.HasCriticalError);
        Assert.False(context.Variables.ContainsKey("Cycle"));
    }

    [Fact]
    public async Task IndependentWaitDiagnostics_AreRetainedByScopeWithoutMergeConflict()
    {
        var context = Context(); context.SetVariable("WaitVariable.RawResponse", "stale");
        TestNode Branch(string name) => Node((ctx, _) =>
        {
            ctx.SetVariable("WaitVariable.VariableName", name);
            ctx.SetVariable("LastCheck.VariableName", name);
            return Task.FromResult(StepResult.Next);
        });
        var a = Branch("A"); var b = Branch("B"); var join = Node(); a.Next = b.Next = join;
        Assert.Equal(ExecutionStatus.Completed, await Execute(Fork(join, a, b), context));
        Assert.DoesNotContain(context.Variables.Keys, key => key.StartsWith("WaitVariable.") || key.StartsWith("LastCheck."));
        Assert.Equal(new[] { "A", "B" }, context.ParallelBranchResults.Select(result => (string)result.Variables["WaitVariable.VariableName"]));
    }

    [Fact]
    public async Task UnknownMutableValue_IsRejectedBeforeAnyBranchStarts()
    {
        var context = Context(); context.SetVariable("Unsafe", new System.Text.StringBuilder());
        var entered = false;
        var a = Node((_, _) => { entered = true; return Task.FromResult(StepResult.Next); });
        var b = Node(); var join = Node(); a.Next = b.Next = join;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Execute(Fork(join, a, b), context));
        Assert.Contains("Unsafe", error.Message);
        Assert.False(entered);
    }

    [Theory]
    [InlineData(StepResult.Stop)]
    [InlineData(StepResult.Next)]
    public async Task BranchTerminatingBeforeJoin_IsRejected(StepResult earlyResult)
    {
        var a = Node((_, _) => Task.FromResult(earlyResult));
        var b = Node(); var join = Node(); b.Next = join;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Execute(Fork(join, a, b)));
    }

    [Fact]
    public async Task VariableRemoval_IsMerged_AndConflictsWithAnotherWriter()
    {
        var context = Context(); context.SetVariable("Value", 7);
        var a = Node((ctx, _) => { ctx.Variables.Remove("Value"); return Task.FromResult(StepResult.Next); });
        var b = Write("Value", 8); var join = Node(); a.Next = b.Next = join;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Execute(Fork(join, a, b), context));
        Assert.Contains("Value", error.Message);
    }

    [Fact]
    public async Task Pause_BlocksTheNextOperationOfEveryBranch()
    {
        var bothStarted = Signal(); var releaseFirstOperations = Signal();
        var bothPaused = Signal(); var resume = Signal();
        var started = 0; var paused = 0; var nextOperations = 0; var pauseRequested = false;
        var context = Context();
        context.WaitIfPausedAsync = async ct =>
        {
            if (!pauseRequested) return;
            if (Interlocked.Increment(ref paused) == 2) bothPaused.SetResult();
            await resume.Task.WaitAsync(ct);
        };
        var join = Node();
        TestNode Branch()
        {
            var first = Node(async (_, ct) =>
            {
                if (Interlocked.Increment(ref started) == 2) bothStarted.SetResult();
                await releaseFirstOperations.Task.WaitAsync(ct);
                return StepResult.Next;
            });
            first.Next = Node((_, _) => { Interlocked.Increment(ref nextOperations); return Task.FromResult(StepResult.Next); });
            first.Next.Next = join;
            return first;
        }
        var execution = Execute(Fork(join, Branch(), Branch()), context);
        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        pauseRequested = true;
        releaseFirstOperations.SetResult();
        await bothPaused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, nextOperations);
        resume.SetResult();
        Assert.Equal(ExecutionStatus.Completed, await execution);
        Assert.Equal(2, nextOperations);
    }

    [Fact]
    public async Task ForEach_RestoresOuterCursorEvenWhenItsBodyThrows()
    {
        var context = Context(); context.CurrentSlaveId = 9; context.SetVariable("slaveId", (byte)9);
        var body = Node((ctx, _) =>
        {
            Assert.Equal((byte)2, ctx.CurrentSlaveId);
            throw new InvalidOperationException("body failed");
        });
        var step = new ForEachSlaveStep(2, 3, 1, true, new CompiledGraph(body), NullLogger.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => step.ExecuteAsync(context, CancellationToken.None));
        Assert.Equal((byte)9, context.CurrentSlaveId);
        Assert.Equal((byte)9, context.GetVariable<byte>("slaveId"));
    }

    private static TestContext Context() => new(new RegisterState());
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TestNode Node(Func<TestContext, CancellationToken, Task<StepResult>>? action = null) =>
        new(new Step(action ?? ((_, _) => Task.FromResult(StepResult.Next))));
    private static TestNode Write(string name, object value) => Node((ctx, _) =>
    { ctx.SetVariable(name, value); return Task.FromResult(StepResult.Next); });
    private static TestNode Fork(TestNode join, params TestNode[] branches)
    {
        var start = Node(); start.ParallelTransitions[StepResult.Next] = new ParallelFork(branches, join); return start;
    }
    private static Task<ExecutionStatus> Execute(TestNode node, TestContext? context = null, CancellationToken token = default) =>
        new TestExecutor().ExecuteAsync(node, context ?? Context(), token).WaitAsync(TimeSpan.FromSeconds(10));
    private static TestNode BlockingBranch(TaskCompletionSource started, TaskCompletionSource cleanup, TaskCompletionSource release) =>
        Node(async (_, ct) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { cleanup.SetResult(); await release.Task; }
            return StepResult.Next;
        });
    private sealed class Step(Func<TestContext, CancellationToken, Task<StepResult>> action) : ITestStep
    {
        public Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken) => action(context, cancellationToken);
    }
    private sealed class Observer : IExecutionObserver
    {
        public int BranchesFinished;
        public int NodesFinished;
        public int NodesFailed;
        public bool TerminalTokenWasCancelled;
        public Task NodeStartedAsync(TestNode node, TestContext context, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NodeFailedAsync(TestNode node, TestContext context, CancellationToken cancellationToken)
        { Interlocked.Increment(ref NodesFailed); TerminalTokenWasCancelled |= cancellationToken.IsCancellationRequested; return Task.CompletedTask; }
        public Task NodeFinishedAsync(TestNode node, TestContext context, CancellationToken cancellationToken)
        { Interlocked.Increment(ref NodesFinished); TerminalTokenWasCancelled |= cancellationToken.IsCancellationRequested; return Task.CompletedTask; }
        public Task ParallelBranchFinishedAsync(TestContext context, CancellationToken cancellationToken)
        { Interlocked.Increment(ref BranchesFinished); TerminalTokenWasCancelled |= cancellationToken.IsCancellationRequested; return Task.CompletedTask; }
    }
    private sealed class FailingObserver : IExecutionObserver
    {
        public Task NodeStartedAsync(TestNode node, TestContext context, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NodeFailedAsync(TestNode node, TestContext context, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NodeFinishedAsync(TestNode node, TestContext context, CancellationToken cancellationToken) => throw new Exception("Observer failure");
    }
}
