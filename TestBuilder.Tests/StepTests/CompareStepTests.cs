using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using Xunit;

namespace TestBuilder.Tests.StepTests
{
    public class CompareStepTests
    {
        [Fact]
        public async Task CompareStep_ReturnsTrue_WhenValueInRange()
        {
            // Arrange
            var context = new TestContext();
            context.RegisterState.Update("CR2032", 3200); // <- пишем в RegisterState
            var step = new CompareStep("CR2032", 3000, 3300);

            // Act
            var result = await step.ExecuteAsync(context, CancellationToken.None);

            // Assert
            Assert.Equal(StepResult.True, result);
        }

        [Fact]
        public async Task CompareStep_ReturnsFalse_WhenValueOutOfRange()
        {
            // Arrange
            var context = new TestContext();
            context.RegisterState.Update("CR2032", 3400); // <- пишем в RegisterState
            var step = new CompareStep("CR2032", 3000, 3300);

            // Act
            var result = await step.ExecuteAsync(context, CancellationToken.None);

            // Assert
            Assert.Equal(StepResult.False, result);
        }

        [Fact]
        public async Task CompareStep_ReturnsFalse_WhenRegisterMissing()
        {
            // Arrange
            var context = new TestContext();
            var step = new CompareStep("CR2032", 3000, 3300);

            // Act
            var result = await step.ExecuteAsync(context, CancellationToken.None);

            // Assert
            Assert.Equal(StepResult.False, result);
        }
    }
}
