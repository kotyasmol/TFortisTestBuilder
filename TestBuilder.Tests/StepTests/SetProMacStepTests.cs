using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class SetProMacStepTests
{
    [Fact]
    public async Task ExecuteAsync_PassesNormalizedMacBoardVersionAndUnixTimestamp()
    {
        SetProMacProcessRequest? capturedRequest = null;
        var step = CreateStep(
            (request, _) =>
            {
                capturedRequest = request;
                return Task.FromResult(new SetProMacProcessResult(
                    Started: true,
                    TimedOut: false,
                    ExitCode: 0,
                    StdOut: "WinSCP output\r\nSUCCESSFUL",
                    StdErr: string.Empty));
            },
            timestamp: 1_788_888_888);
        var context = CreateContext("c0-11-a6-20-01-ac");

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.NotNull(capturedRequest);
        Assert.Equal("set_mac_pro.bat", capturedRequest.BatchPath);
        Assert.Equal("C0:11:A6:20:01:AC", capturedRequest.MacAddress);
        Assert.Equal("PSW+UPS-Box 8x2Pro", capturedRequest.BoardVersion);
        Assert.Equal(1_788_888_888, capturedRequest.Timestamp);
        Assert.Equal(60000, capturedRequest.TimeoutMs);
        Assert.True(context.GetVariable<bool>("SetMac.Started"));
        Assert.True(context.GetVariable<bool>("SetMac.Success"));
        Assert.False(context.GetVariable<bool>("SetMac.TimedOut"));
        Assert.Equal("ProBatch", context.GetVariable<string>("SetMac.Method"));
        Assert.Equal("C0:11:A6:20:01:AC", context.GetVariable<string>("SetMac.Mac"));
        Assert.Equal("PSW+UPS-Box 8x2Pro", context.GetVariable<string>("SetMac.BoardVersion"));
        Assert.Equal(1_788_888_888L, context.GetVariable<long>("SetMac.Timestamp"));
        Assert.Equal(0, context.GetVariable<int>("SetMac.ExitCode"));
        Assert.Equal(string.Empty, context.GetVariable<string>("SetMac.Error"));
    }

    [Fact]
    public async Task ExecuteAsync_RejectsInvalidMacWithoutStartingBatch()
    {
        var called = false;
        var step = CreateStep((_, _) =>
        {
            called = true;
            return Task.FromResult(SetProMacProcessResult.NotStarted("unexpected"));
        });
        var context = CreateContext("C0:11:A6:20:1:AC");

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.False(called);
        Assert.False(context.GetVariable<bool>("SetMac.Success"));
        Assert.Contains("Некорректный MAC", context.GetVariable<string>("SetMac.Error"));
    }

    [Fact]
    public async Task ExecuteAsync_FailsWhenBatchReturnsNonZeroExitCode()
    {
        var step = CreateStep((_, _) => Task.FromResult(new SetProMacProcessResult(
            Started: true,
            TimedOut: false,
            ExitCode: 1,
            StdOut: "ERROR 1",
            StdErr: "Connection failed")));
        var context = CreateContext();

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.False(context.GetVariable<bool>("SetMac.Success"));
        Assert.Equal(1, context.GetVariable<int>("SetMac.ExitCode"));
        Assert.Contains("кодом 1", context.GetVariable<string>("SetMac.Error"));
    }

    [Fact]
    public async Task ExecuteAsync_FailsWhenSuccessMarkerIsMissing()
    {
        var step = CreateStep((_, _) => Task.FromResult(new SetProMacProcessResult(
            Started: true,
            TimedOut: false,
            ExitCode: 0,
            StdOut: "WinSCP ended",
            StdErr: string.Empty)));
        var context = CreateContext();

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.False(context.GetVariable<bool>("SetMac.Success"));
        Assert.Contains("SUCCESSFUL", context.GetVariable<string>("SetMac.Error"));
    }

    [Fact]
    public async Task ExecuteAsync_TimesOutAndUsesFalseOutput()
    {
        var step = CreateStep((_, _) => Task.FromResult(new SetProMacProcessResult(
            Started: true,
            TimedOut: true,
            ExitCode: -1,
            StdOut: "waiting",
            StdErr: string.Empty)));
        var context = CreateContext();

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.True(context.GetVariable<bool>("SetMac.TimedOut"));
        Assert.Contains("был остановлен", context.GetVariable<string>("SetMac.Error"));
    }

    [Fact]
    public async Task ExecuteAsync_CanContinueAfterFailureWhenConfigured()
    {
        var step = CreateStep(
            (_, _) => Task.FromResult(SetProMacProcessResult.NotStarted("bat missing")),
            failOnError: false);
        var context = CreateContext();

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.False(context.GetVariable<bool>("SetMac.Success"));
        Assert.Equal("bat missing", context.GetVariable<string>("SetMac.Error"));
    }

    [Fact]
    public void BuildCmdArguments_QuotesAllThreeArguments()
    {
        var request = new SetProMacProcessRequest(
            @"C:\Program Files\TFortis\set_mac_pro.bat",
            "C0:11:A6:20:01:AC",
            "PSW+UPS-Box 8x2Pro",
            1_788_888_888,
            60000);

        var arguments = SetProMacStep.BuildCmdArguments(request);

        Assert.Equal(
            "/d /s /c \"\"C:\\Program Files\\TFortis\\set_mac_pro.bat\" \"C0:11:A6:20:01:AC\" \"PSW+UPS-Box 8x2Pro\" \"1788888888\"\"",
            arguments);
    }

    [Fact]
    public async Task ExecuteAsync_AcceptsPathCopiedFromWindowsExplorerWithQuotes()
    {
        SetProMacProcessRequest? capturedRequest = null;
        var step = new SetProMacStep(
            NullLogger.Instance,
            "\"C:\\TFortisStandNew\\set_mac_pro.bat\"",
            "Dut.NewMac",
            "PSW+UPS-Box 8x2Pro",
            60000,
            true,
            (request, _) =>
            {
                capturedRequest = request;
                return Task.FromResult(new SetProMacProcessResult(true, false, 0, "SUCCESSFUL", string.Empty));
            },
            () => 1_788_888_888);

        var result = await step.ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(@"C:\TFortisStandNew\set_mac_pro.bat", capturedRequest?.BatchPath);
    }

    private static SetProMacStep CreateStep(
        Func<SetProMacProcessRequest, CancellationToken, Task<SetProMacProcessResult>> runner,
        long timestamp = 1_788_888_888,
        bool failOnError = true) =>
        new(
            NullLogger.Instance,
            "set_mac_pro.bat",
            "Dut.NewMac",
            "PSW+UPS-Box 8x2Pro",
            60000,
            failOnError,
            runner,
            () => timestamp);

    private static TestContext CreateContext(string mac = "C0:11:A6:20:01:AC")
    {
        var context = new TestContext(new RegisterState());
        context.SetVariable("Dut.NewMac", mac);
        return context;
    }
}
