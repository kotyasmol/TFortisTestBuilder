using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;

namespace TestBuilder.Tests.StepTests
{
    public class DelayStepTests
    {
        [Fact]
        public async Task DelayStep_WaitsAtLeastSpecifiedTime()
        {
            // Arrange
            var step = new DelayStep(100); // 100 ms
            var context = new TestContext();
            var cancellationToken = CancellationToken.None;

            var sw = Stopwatch.StartNew();

            // Act
            var result = await step.ExecuteAsync(context, cancellationToken);

            sw.Stop();

            // Assert
            Assert.Equal(StepResult.Next, result);
            Assert.True(sw.ElapsedMilliseconds >= 100, "Step did not wait long enough");
        }
    }
}
