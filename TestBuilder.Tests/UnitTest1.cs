using TestBuilder.Domain.Execution;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TestBuilder.Tests
{
    // Мини-шпион для проверки вызова шагов
    public class TestStepSpy : ITestStep
    {
        public bool WasExecuted { get; private set; }
        private readonly StepResult _result;

        public TestStepSpy(StepResult result = StepResult.Next)
        {
            _result = result;
        }

        public Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            WasExecuted = true;
            return Task.FromResult(_result);
        }
    }

    public class UnitTest1
    {
        [Fact]
        public async Task LinearGraph_ExecutesAllSteps()
        {
            // Arrange: создаём два шага и соединяем линейно
            var step1 = new TestStepSpy();
            var step2 = new TestStepSpy();

            var node1 = new TestNode { Step = step1 };
            var node2 = new TestNode { Step = step2 };

            node1.Next = node2;

            var executor = new TestExecutor();
            var context = new TestContext();

            // Act: выполняем граф
            await executor.ExecuteAsync(node1, context, CancellationToken.None);

            // Assert: проверяем, что оба шага выполнены
            Assert.True(step1.WasExecuted);
            Assert.True(step2.WasExecuted);
        }

        [Fact]
        public async Task BranchGraph_ExecutesCorrectBranch()
        {
            // Arrange: создаём условный шаг и две ветки
            var conditionStep = new TestStepSpy(StepResult.True);
            var trueStep = new TestStepSpy();
            var falseStep = new TestStepSpy();

            var conditionNode = new TestNode { Step = conditionStep };
            var trueNode = new TestNode { Step = trueStep };
            var falseNode = new TestNode { Step = falseStep };

            conditionNode.OnTrue = trueNode;
            conditionNode.OnFalse = falseNode;

            var executor = new TestExecutor();
            var context = new TestContext();

            // Act: выполняем граф
            await executor.ExecuteAsync(conditionNode, context, CancellationToken.None);

            // Assert: проверяем правильное ветвление
            Assert.True(conditionStep.WasExecuted);
            Assert.True(trueStep.WasExecuted);
            Assert.False(falseStep.WasExecuted);
        }
    }
}
