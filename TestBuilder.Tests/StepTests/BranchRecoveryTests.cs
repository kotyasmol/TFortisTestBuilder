using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Tests.Support;
using TestBuilder.Services.Http;

namespace TestBuilder.Tests.StepTests;

public class BranchRecoveryTests
{
    private static TestNode Node(Func<TestContext, CancellationToken, Task<StepResult>> action) => new(new ActionStep(action));
    private static (TestNode Root, TestNode Confirm, Guid Key) Path(bool confirmationPasses = true, bool optionalFailure = false,
        bool transportFailure = false, bool critical = false, Action<TestContext>? recovery = null)
    {
        var key = Guid.NewGuid();
        var failed = new BranchRecoveryCandidateStep(new ActionStep((context, _) =>
        {
            if (transportFailure) throw new IOException("Нет ответа устройства");
            context.AddReportEntry("Проверка", false, "0"); context.HasCriticalError |= critical;
            return Task.FromResult(StepResult.False);
        }), key, "Исходная проверка", NullLogger.Instance);
        var start = new TestNode(failed);
        var restore = Node((context, _) => { recovery?.Invoke(context); return Task.FromResult(StepResult.Next); });
        var confirm = Node((context, _) =>
        {
            context.AddReportEntry("Повторная проверка", confirmationPasses && !optionalFailure, "1");
            return Task.FromResult(confirmationPasses ? StepResult.True : StepResult.False);
        });
        var end = Node((_, _) => Task.FromResult(StepResult.Stop));
        start.OnFalse = restore; restore.Next = confirm; confirm.OnTrue = end; confirm.OnFalse = end;
        confirm.RecoveryConfirmations[StepResult.True] = new() { key };
        return (start, confirm, key);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)]
    public async Task SuccessOnLinkedFalsePathResolvesOnlyTheOriginalFailure(bool transport, bool critical)
    {
        var path = Path(transportFailure: transport, critical: critical);
        var context = new TestContext(new RegisterState());
        // Same text/value, different check: identity, not record equality, matters.
        var unrelated = new TestReportEntry("Проверка", false, "0"); context.ReportEntries.Add(unrelated);
        Assert.Equal(ExecutionStatus.Completed, await new TestExecutor().ExecuteAsync(path.Root, context, default));
        Assert.False(context.HasCriticalError); Assert.False(context.HasExecutionException);
        Assert.Contains(context.ReportEntries, e => ReferenceEquals(e, unrelated));
        Assert.Single(context.ReportEntries.Where(e => !e.IsSuccess)); Assert.Null(context.RecoveryScope);
        context.SetVariable("SerialNumber", 3300001);
        var build = new BuildTestReportStep(NullLogger.Instance, "Report", "Stand", "SerialNumber", "", "production", false);
        Assert.Equal(StepResult.True, await build.ExecuteAsync(context, default));
        Assert.Contains("test_result=true=0", context.GetVariable<string>("Report"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailedOrOptionalConfirmationCannotAcknowledgeTheOriginalError(bool optional)
    {
        var path = Path(confirmationPasses: optional, optionalFailure: optional);
        var context = new TestContext(new RegisterState());
        Assert.Equal(ExecutionStatus.Failed, await new TestExecutor().ExecuteAsync(path.Root, context, default));
        Assert.Equal(2, context.ReportEntries.Count(e => !e.IsSuccess)); Assert.True(context.HasCriticalError);
        Assert.Equal("Исходная проверка", context.Failure!.StepName);
    }

    [Fact]
    public async Task OnlyTheFailureFromTheCurrentExecutionFrameCanBeAcknowledged()
    {
        var path = Path(); path.Confirm.RecoveryConfirmations.Clear();
        var context = new TestContext(new RegisterState());
        Assert.Equal(ExecutionStatus.Failed, await new TestExecutor().ExecuteAsync(path.Root, context, default));
        var originalFailure = context.ReportEntries.First(); context.HasCriticalError = false; context.CriticalFailure = null;
        path.Confirm.RecoveryConfirmations[StepResult.True] = new() { path.Key };
        Assert.Equal(ExecutionStatus.Completed, await new TestExecutor().ExecuteAsync(path.Root, context, default));
        Assert.Contains(context.ReportEntries, e => ReferenceEquals(e, originalFailure));
        Assert.Single(context.ReportEntries.Where(e => !e.IsSuccess));
    }

    [Fact]
    public async Task OtherFailuresInsideRecoveryPathSurviveSuccessfulConfirmation()
    {
        var path = Path(recovery: context => context.AddReportEntry("Восстановление питания", false, "Не подтверждено"));
        var context = new TestContext(new RegisterState());
        Assert.Equal(ExecutionStatus.Completed, await new TestExecutor().ExecuteAsync(path.Root, context, default));
        Assert.Equal("Восстановление питания", Assert.Single(context.ReportEntries.Where(e => !e.IsSuccess)).Name);
    }

    [Fact]
    public async Task CancellationAndUnfinishedProcessesAreNeverHandledByTheBranch()
    {
        using var cts = new CancellationTokenSource();
        var path = Path(recovery: _ => cts.Cancel());
        var context = new TestContext(new RegisterState());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TestExecutor().ExecuteAsync(path.Root, context, cts.Token));
        Assert.Contains(context.ReportEntries, e => !e.IsSuccess); Assert.Null(context.RecoveryScope);
        var unsafeStep = new TestNode(new BranchRecoveryCandidateStep(new ActionStep((_, _) =>
            throw new ExecutionCleanupException("Процесс ещё работает")), Guid.NewGuid(), "ARP", NullLogger.Instance));
        unsafeStep.OnFalse = path.Confirm;
        await Assert.ThrowsAsync<ExecutionCleanupException>(() => new TestExecutor().ExecuteAsync(unsafeStep, new(new RegisterState()), default));
    }

    [Fact]
    public async Task PreparationStillStopsImmediatelyBeforeTheRecoveryPath()
    {
        var called = false; var path = Path(critical: true, recovery: _ => called = true);
        var context = new TestContext(new RegisterState()) { IsPreparation = true };
        Assert.Equal(ExecutionStatus.Failed, await new TestExecutor().ExecuteAsync(path.Root, context, default));
        Assert.False(called); Assert.True(context.HasCriticalError);
    }

    [Fact]
    public async Task ActualSelftestRecheckReplacesTheFailedSnapshotAndProducesAPassingReport()
    {
        var service = new SelftestResponses();
        SelfTestCheckStep Check() => new(service, NullLogger.Instance, "http://dut/test.shtml", 1000,
            "Dut", "init_ok=1..1\nfirmvare_vers=257..257", failOnError: true,
            useBrowser: false, enforceMinimumDeviceReadyTimeout: false);
        var key = Guid.NewGuid();
        var first = new TestNode(new BranchRecoveryCandidateStep(Check(), key, "Самопроверка", NullLogger.Instance));
        var second = new TestNode(Check()); first.OnFalse = second;
        second.RecoveryConfirmations[StepResult.True] = new() { key };
        var context = new TestContext(new RegisterState());
        var status = await new TestExecutor().ExecuteAsync(first, context, default);
        Assert.True(status == ExecutionStatus.Completed, context.Failure?.Summary ?? string.Join("; ", context.ReportEntries.Select(e => e.Value)));
        Assert.Equal(2, service.Calls); Assert.False(context.HasCriticalError);
        Assert.Equal(0, context.GetVariable<int>("SelfTest.FailedRuleCount"));
        context.SetVariable("SerialNumber", 3300001);
        var report = new BuildTestReportStep(NullLogger.Instance, "Report", "Stand", "SerialNumber", "", "production", false);
        Assert.Equal(StepResult.True, await report.ExecuteAsync(context, default));
        Assert.Contains("test_result=true=1", context.GetVariable<string>("Report"));
    }

    [Fact]
    public async Task SkippingTheConfirmationDoesNotCountAsSuccess()
    {
        var key = Guid.NewGuid(); var context = new TestContext(new RegisterState()) { SkipFirstRunNodes = true };
        var original = new TestNode(new BranchRecoveryCandidateStep(new ActionStep((_, _) => Task.FromResult(StepResult.False)),
            key, "Проверка", NullLogger.Instance));
        var skipped = new TestNode(new ActionStep((_, _) => throw new Exception("Не должна выполняться")))
        { RunOnlyOnFirstRun = true, SkippedResult = StepResult.True };
        original.OnFalse = skipped;
        skipped.RecoveryConfirmations[StepResult.True] = new() { key };
        Assert.Equal(ExecutionStatus.Failed, await new TestExecutor().ExecuteAsync(original, context, default));
        Assert.False(Assert.Single(context.ReportEntries).IsSuccess);
    }

    [Fact]
    public async Task NewCriticalFlagFromTheConfirmingNodeCannotAcknowledgeRecovery()
    {
        var key = Guid.NewGuid(); var context = new TestContext(new RegisterState());
        var original = new TestNode(new BranchRecoveryCandidateStep(new ActionStep((_, _) => Task.FromResult(StepResult.False)),
            key, "Проверка", NullLogger.Instance));
        var confirmation = Node((ctx, _) => { ctx.HasCriticalError = true; return Task.FromResult(StepResult.True); });
        original.OnFalse = confirmation; confirmation.RecoveryConfirmations[StepResult.True] = new() { key };
        Assert.Equal(ExecutionStatus.Failed, await new TestExecutor().ExecuteAsync(original, context, default));
        Assert.False(Assert.Single(context.ReportEntries).IsSuccess);
    }

    private sealed class SelftestResponses : IHttpRequestService
    {
        public int Calls { get; private set; }
        public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout, CancellationToken token) => Task.FromResult(
            HttpRequestResult.Success(200, $"<selftest><init_ok>1</init_ok><default_mac>AC:CC:11:A6:00:00</default_mac><firmvare_vers>{(++Calls == 1 ? "100" : "101")}</firmvare_vers></selftest>", TimeSpan.Zero));
    }

    private sealed class ActionStep(Func<TestContext, CancellationToken, Task<StepResult>> action) : ITestStep
    { public Task<StepResult> ExecuteAsync(TestContext context, CancellationToken token) => action(context, token); }
}
