using System.Diagnostics;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class DelayStepTests
{
    [Fact]
    public async Task DelayStep_WaitsAtLeastSpecifiedTime()
    {
        var step = new DelayStep(100, NullLogger.Instance);
        var context = new TestContext(new RegisterState());

        var sw = Stopwatch.StartNew();

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        sw.Stop();

        Assert.Equal(StepResult.Next, result);
        Assert.True(
            sw.Elapsed >= TimeSpan.FromMilliseconds(90),
            $"Step did not wait long enough: {sw.ElapsedMilliseconds} ms");
    }
}
