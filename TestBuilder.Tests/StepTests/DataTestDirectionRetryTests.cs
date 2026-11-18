using System.Net;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class DataTestDirectionRetryTests
{
    private static readonly IPAddress Source = IPAddress.Parse("192.168.0.2");
    private static readonly IPAddress Destination = IPAddress.Parse("192.168.0.3");
    private static DataTestPortResult Measurement(bool passed, bool infrastructure = false) =>
        DataTestPortResult.Fail(infrastructure ? "Ошибка захвата" : "Потери 10% превышают допуск 1%", infrastructure)
            with { Passed = passed, Error = passed ? "" : infrastructure ? "Ошибка захвата" : "Потери 10% превышают допуск 1%",
                TargetBandwidthMbps = 100, TransmittedPackets = 100, ReceivedPackets = passed ? 100 : 90,
                ExpectedPackets = 100, LossPercent = passed ? 0 : 10, ActualTxMbps = 100, ActualRxMbps = passed ? 100 : 90 };

    private static Task<DataTestPortResult> Run(Func<CancellationToken, Task<DataTestPortResult>> measure,
        CancellationToken token = default, Action<DataTestPortResult>? log = null,
        Func<CancellationToken, Task>? delay = null, int timeoutMs = 5000) =>
        DataTestDirectionRetry.RunAsync(measure, Source, Destination, timeoutMs, log ?? (_ => { }),
            delay ?? (_ => Task.CompletedTask), token);

    [Fact]
    public async Task SuccessfulDirectionIsMeasuredOnlyOnce()
    {
        var calls = 0;
        var result = await Run(_ => { calls++; return Task.FromResult(Measurement(true)); },
            delay: _ => throw new Exception("No retry expected"));
        Assert.True(result.Passed); Assert.Equal(1, calls);
        Assert.Equal(1, result.AttemptCount); Assert.Single(result.Attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LossAndInfrastructureFaultsCanRecoverWithoutAveragingMeasurements(bool infrastructure)
    {
        var calls = 0; var delays = 0;
        var logged = new List<DataTestPortResult>();
        var result = await Run(_ => Task.FromResult(Measurement(++calls == 2, infrastructure && calls == 1)),
            log: logged.Add, delay: _ => { delays++; return Task.CompletedTask; });
        Assert.True(result.Passed); Assert.Equal(2, calls); Assert.Equal(1, delays);
        Assert.Equal(2, result.AttemptCount); Assert.Equal(2, result.Attempts.Count);
        Assert.False(logged[0].Passed); Assert.True(logged[1].Passed);
        Assert.Equal(infrastructure, result.Attempts[0].InfrastructureFailure);
        Assert.Contains("192.168.0.2 → 192.168.0.3 (попытка 1)", result.Attempts[0].Error);
        Assert.Equal(100, result.ReceivedPackets); Assert.Equal(0, result.LossPercent);
        Assert.Equal(90, result.Attempts[0].ReceivedPackets); Assert.Equal(10, result.Attempts[0].LossPercent);
        Assert.All(result.Attempts, attempt => Assert.Empty(attempt.Attempts));
        var context = new TestContext(new RegisterState());
        Step().WritePortResult(context, 0, new("Пара", Destination.ToString(), Source.ToString()),
            DataTestPairResult.FromDirections(result, null), "");
        Assert.Equal(infrastructure, context.GetVariable<bool>("DataTest.Port0.Forward.Attempt1.InfrastructureFailure"));
        Assert.False(context.GetVariable<bool>("DataTest.Port0.Forward.Attempt2.InfrastructureFailure"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AllFailureTypesShareOneRetryBudget(bool firstInfrastructure, bool lastInfrastructure)
    {
        var calls = 0;
        var result = await Run(_ => Task.FromResult(Measurement(false, ++calls == 1 ? firstInfrastructure : lastInfrastructure)));
        Assert.False(result.Passed); Assert.Equal(2, calls); Assert.Equal(2, result.AttemptCount);
        Assert.Equal(lastInfrastructure, result.InfrastructureFailure);
        Assert.Contains("192.168.0.2 → 192.168.0.3 (попытка 2)", result.Error);
        Assert.Equal(2, result.Attempts.Count);
    }

    [Fact]
    public async Task TimeoutGetsANewDeadlineForItsSingleRetry()
    {
        var calls = 0; var tokens = new List<CancellationToken>();
        var result = await Run(async token =>
        {
            tokens.Add(token);
            if (++calls == 1) await Task.Delay(Timeout.Infinite, token);
            token.ThrowIfCancellationRequested();
            return Measurement(true);
        }, timeoutMs: 30);
        Assert.True(result.Passed); Assert.Equal(2, calls); Assert.NotEqual(tokens[0], tokens[1]);
        Assert.Contains("Таймаут измерения", result.Attempts[0].Error);
        Assert.True(result.Attempts[0].InfrastructureFailure);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("measurement")]
    [InlineData("delay")]
    public async Task UserCancellationIsNeverRetried(string phase)
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        if (phase == "before") cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(async token =>
        {
            calls++;
            if (phase == "measurement") { cancellation.Cancel(); await Task.Delay(Timeout.Infinite, token); }
            return Measurement(false);
        }, cancellation.Token, delay: token =>
        {
            cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask;
        }));
        Assert.Equal(phase == "before" ? 0 : 1, calls);
    }

    [Fact]
    public async Task CompletionAfterExpiredDeadlineCannotClaimSuccess()
    {
        var calls = 0;
        var result = await Run(async token =>
        {
            if (++calls == 1)
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { }
            return Measurement(true);
        }, timeoutMs: 30);
        Assert.True(result.Passed); Assert.Equal(2, calls);
        Assert.False(result.Attempts[0].Passed);
        Assert.Contains("Таймаут", result.Attempts[0].Error);
    }

    [Fact]
    public async Task PairUsesFinalMeasurementsButKeepsFirstFailureInReportAndVariables()
    {
        var forwardCalls = 0; var reverseCalls = 0;
        var forward = await Run(_ => Task.FromResult(Measurement(++forwardCalls == 2)));
        var reverse = await Run(_ => { reverseCalls++; return Task.FromResult(Measurement(true)); });
        var pair = DataTestPairResult.FromDirections(forward, reverse);
        Assert.True(pair.Passed); Assert.Equal(2, forwardCalls); Assert.Equal(1, reverseCalls);
        Assert.Equal(200, pair.TransmittedPackets); Assert.Equal(200, pair.ReceivedPackets);
        var context = new TestContext(new RegisterState());
        var step = Step();
        step.WritePortResult(context, 0, new("Пара", Destination.ToString(), Source.ToString()), pair, "");
        Assert.True(context.GetVariable<bool>("DataTest.Port0.RecoveredOnRetry"));
        Assert.True(context.GetVariable<bool>("DataTest.Port0.Passed"));
        Assert.Equal(90, context.GetVariable<int>("DataTest.Port0.Forward.Attempt1.ReceivedPackets"));
        Assert.Equal(100, context.GetVariable<int>("DataTest.Port0.Forward.Attempt2.ReceivedPackets"));
        Assert.False(context.GetVariable<bool>("DataTest.Port0.Forward.Attempt1.Passed"));
        Assert.True(context.GetVariable<bool>("DataTest.Port0.Forward.Attempt2.Passed"));
        Assert.DoesNotContain("DataTest.Port0.Reverse.Attempt2.Passed", context.Variables.Keys);
        var report = Assert.Single(context.ReportEntries);
        Assert.True(report.IsSuccess); Assert.Contains("пройдено со второй попытки", report.Value);
        Assert.Contains("Потери 10%", report.Value);
        context.SetVariable("SerialNumber", 3200001);
        var build = new BuildTestReportStep(NullLogger.Instance, "Report", "TEST", "SerialNumber", "", "production", false);
        Assert.Equal(StepResult.True, await build.ExecuteAsync(context, default));
        Assert.Contains("пройдено со второй попытки", context.GetVariable<string>("Report"));
        Assert.Contains("Потери 10%", context.GetVariable<string>("Report"));
        // The next execution of the same node must not expose old Attempt2/Reverse values.
        step.WritePortResult(context, 0, new("Пара", Destination.ToString(), Source.ToString()),
            DataTestPairResult.FromDirections(await Run(_ => Task.FromResult(Measurement(true))), null), "");
        Assert.False(context.GetVariable<bool>("DataTest.Port0.RecoveredOnRetry"));
        Assert.Equal("", context.GetVariable<string>("DataTest.Port0.RetryDetails"));
        Assert.DoesNotContain(context.Variables.Keys, key => key.StartsWith("DataTest.Port0.Forward.Attempt2.") || key.StartsWith("DataTest.Port0.Reverse."));
    }

    private static RunDataTestStep Step() => new(NullLogger.Instance, "SoftwarePcap", 1, 1514, 43962, 5000,
        100, 300, 0, 0, 1, 2, true, Array.Empty<DataTestPortConfig>(), "DataTest", true);
}
