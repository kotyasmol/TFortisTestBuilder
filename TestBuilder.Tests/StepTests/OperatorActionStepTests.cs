using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class OperatorActionStepTests
{
    [Fact]
    public async Task ExecuteAsync_ExpandsSerialAndMacBeforePrompt()
    {
        var context = new TestContext(new RegisterState());
        context.SetVariable("SerialNumber", 3200428);
        context.SetVariable("Dut.default_mac", "C0:11:A6:20:01:AC");
        string? shownMessage = null;
        context.OperatorPrompt = message =>
        {
            shownMessage = message;
            return Task.FromResult(true);
        };
        var step = new OperatorActionStep(
            "Серийный номер: {SerialNumber}\nMAC: {Dut.default_mac}",
            NullLogger.Instance);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(
            "Серийный номер: 3200428\nMAC: C0:11:A6:20:01:AC",
            shownMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ShowsMissingVariableAndReturnsCancel()
    {
        var context = new TestContext(new RegisterState());
        string? shownMessage = null;
        context.OperatorPrompt = message =>
        {
            shownMessage = message;
            return Task.FromResult(false);
        };
        var step = new OperatorActionStep(
            "Серийный номер: {SerialNumber}",
            NullLogger.Instance);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.Equal("Серийный номер: <не задано: SerialNumber>", shownMessage);
    }
}
