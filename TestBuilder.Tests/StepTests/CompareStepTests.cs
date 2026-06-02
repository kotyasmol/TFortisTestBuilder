using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class CompareStepTests
{
    [Fact]
    public async Task CheckRegisterRangeStep_ReturnsTrue_WhenValueInRange()
    {
        var registerState = new RegisterState();
        var context = new TestContext(registerState);
        registerState.Update(slaveId: 1, address: 100, value: 3200);
        var step = new CheckRegisterRangeStep(1, 100, 3000, 3300, NullLogger.Instance);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
    }

    [Fact]
    public async Task CheckRegisterRangeStep_ReturnsFalse_WhenValueOutOfRange()
    {
        var registerState = new RegisterState();
        var context = new TestContext(registerState);
        registerState.Update(slaveId: 1, address: 100, value: 3400);
        var step = new CheckRegisterRangeStep(1, 100, 3000, 3300, NullLogger.Instance);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
    }

    [Fact]
    public async Task CheckRegisterRangeStep_ReturnsFalse_WhenRegisterMissing()
    {
        var context = new TestContext(new RegisterState());
        var step = new CheckRegisterRangeStep(1, 100, 3000, 3300, NullLogger.Instance);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
    }
}
