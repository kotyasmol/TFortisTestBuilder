using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public class OperatorActionStep : ITestStep
    {
        private readonly string _message;
        private readonly ILogger _logger;

        public OperatorActionStep(string message, ILogger logger)
        {
            _message = message;
            _logger = logger;
        }

        public async Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            if (context.OperatorPrompt == null)
            {
                _logger.Warning("[ШАГ] Действие оператора → обработчик диалога не задан.");
                return StepResult.False;
            }

            _logger.Info($"[ШАГ] Ожидание действия оператора: {_message}");

            var confirmed = await context.OperatorPrompt(_message);

            _logger.Info($"[ШАГ] Оператор выбрал: {(confirmed ? "Продолжить" : "Отмена")}");

            return confirmed ? StepResult.True : StepResult.False;
        }
    }
}
