using System;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class SubtestStepTests
{
    [Fact]
    public async Task ExecuteAsync_WhenDisabled_SkipsBodyAndReturnsTrue()
    {
        var bodyStep = new CountingStep(StepResult.True);
        var step = CreateSubtestStep(isEnabled: false, stopOnError: true, bodyStep);

        var result = await step.ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(0, bodyStep.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenBodyCompletes_ReturnsTrue()
    {
        var bodyStep = new CountingStep(StepResult.Next);
        var step = CreateSubtestStep(isEnabled: true, stopOnError: true, bodyStep);

        var result = await step.ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(1, bodyStep.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenBodyFailsAndStopOnErrorDisabled_ReturnsFalse()
    {
        var bodyStep = new CountingStep(StepResult.False);
        var step = CreateSubtestStep(isEnabled: true, stopOnError: false, bodyStep);

        var result = await step.ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.Equal(1, bodyStep.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenBodyFailsAndStopOnErrorEnabled_ReturnsFalseAndMarksCritical()
    {
        var bodyStep = new CountingStep(StepResult.False);
        var step = CreateSubtestStep(isEnabled: true, stopOnError: true, bodyStep);
        var context = CreateContext();

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.True(context.HasCriticalError);
        Assert.Equal(1, bodyStep.ExecuteCount);
    }

    private static SubtestStep CreateSubtestStep(
        bool isEnabled,
        bool stopOnError,
        ITestStep bodyStep)
    {
        var bodyNode = new TestNode(bodyStep);
        var bodyGraph = new CompiledGraph(bodyNode);

        return new SubtestStep(
            "Selftest",
            isEnabled,
            stopOnError,
            bodyGraph,
            NullLogger.Instance);
    }

    private static TestContext CreateContext()
    {
        return new TestContext(new RegisterState());
    }

    private sealed class CountingStep : ITestStep
    {
        private readonly StepResult _result;

        public int ExecuteCount { get; private set; }

        public CountingStep(StepResult result)
        {
            _result = result;
        }

        public Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            ExecuteCount++;
            return Task.FromResult(_result);
        }
    }
}
