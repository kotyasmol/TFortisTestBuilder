using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;

namespace TestBuilder.Tests.StepTests
{
    public class RegisterWriteStepTests
    {
        [Fact]
        public async Task WriteStep_WritesValueToContext()
        {
            // Arrange
            var context = new TestContext();
            var step = new ModbusWriteStep("CR2032", 3300);

            // Act
            var result = await step.ExecuteAsync(context, CancellationToken.None);

            // Assert
            Assert.Equal(StepResult.Next, result);
            Assert.True(context.Registers.ContainsKey("CR2032"));
            Assert.Equal(3300, context.Registers["CR2032"]);
        }
    }
}
