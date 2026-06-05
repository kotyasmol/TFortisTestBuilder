using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class CheckVariableStepTests
{
    [Fact]
    public async Task CheckVariableEqualityStep_ReturnsTrue_ForNumber()
    {
        var context = CreateContext();
        context.SetVariable("Dut.init_ok", "1");
        var step = CreateEquality("Dut.init_ok", "1", VariableComparisonType.Number);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.True(context.GetVariable<bool>("LastCheck.Passed"));
        Assert.Equal("Dut.init_ok", context.GetVariable<string>("LastCheck.VariableName"));
    }

    [Fact]
    public async Task CheckVariableEqualityStep_ReturnsFalse_WhenVariableMissing()
    {
        var context = CreateContext();
        var step = CreateEquality("Dut.ups_det", "1", VariableComparisonType.Number);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.False(context.GetVariable<bool>("LastCheck.Passed"));
    }

    [Fact]
    public async Task CheckVariableEqualityStep_NormalizesMacAddress()
    {
        var context = CreateContext();
        context.SetVariable("Dut.default_mac", "aa-bb-cc-dd-ee-ff");
        var step = CreateEquality("Dut.default_mac", "AA:BB:CC:DD:EE:FF", VariableComparisonType.MacAddress);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
    }

    [Fact]
    public async Task CheckVariableEqualityStep_ComparesVersions()
    {
        var context = CreateContext();
        context.SetVariable("Dut.firmvare_vers", "1.1");
        var step = CreateEquality("Dut.firmvare_vers", "1.1.0", VariableComparisonType.Version);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
    }

    [Fact]
    public async Task CheckVariableEqualityStep_ParsesBooleanAliases()
    {
        var context = CreateContext();
        context.SetVariable("Dut.ups_det", "1");
        var step = CreateEquality("Dut.ups_det", "true", VariableComparisonType.Boolean);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
    }

    [Fact]
    public async Task CheckVariableRangeStep_ReturnsTrue_WhenValueInsideInclusiveRange()
    {
        var context = CreateContext();
        context.SetVariable("Dut.akb_voltage", "24.5");
        var step = CreateRange("Dut.akb_voltage", 12.0, 27.0, inclusive: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.True(context.GetVariable<bool>("LastCheck.Passed"));
    }

    [Fact]
    public async Task CheckVariableRangeStep_ReturnsFalse_WhenValueOnExclusiveBoundary()
    {
        var context = CreateContext();
        context.SetVariable("Dut.temperature", "10");
        var step = CreateRange("Dut.temperature", 10.0, 50.0, inclusive: false);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.False(context.GetVariable<bool>("LastCheck.Passed"));
    }

    private static TestContext CreateContext()
    {
        return new TestContext(new RegisterState());
    }

    private static CheckVariableEqualityStep CreateEquality(
        string variableName,
        string expectedValue,
        VariableComparisonType comparisonType)
    {
        return new CheckVariableEqualityStep(
            NullLogger.Instance,
            variableName,
            expectedValue,
            comparisonType,
            "Проверка не пройдена");
    }

    private static CheckVariableRangeStep CreateRange(
        string variableName,
        double min,
        double max,
        bool inclusive)
    {
        return new CheckVariableRangeStep(
            NullLogger.Instance,
            variableName,
            min,
            max,
            inclusive,
            "Значение вне диапазона");
    }
}
