using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public class OperatorActionStep : ITestStep
    {
        private static readonly Regex VariablePlaceholder = new(
            "\\{([^{}\\r\\n]+)\\}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

            var resolvedMessage = ResolveMessage(context);

            _logger.Info($"[ШАГ] Ожидание действия оператора: {resolvedMessage}");

            var confirmed = await context.OperatorPrompt(resolvedMessage);

            _logger.Info($"[ШАГ] Оператор выбрал: {(confirmed ? "Продолжить" : "Отмена")}");

            return confirmed ? StepResult.True : StepResult.False;
        }

        private string ResolveMessage(TestContext context)
        {
            return VariablePlaceholder.Replace(_message ?? string.Empty, match =>
            {
                var variableName = match.Groups[1].Value.Trim();
                if (string.IsNullOrWhiteSpace(variableName))
                {
                    return match.Value;
                }

                if (!context.Variables.TryGetValue(variableName, out var value))
                {
                    value = context.Variables
                        .FirstOrDefault(item => string.Equals(
                            item.Key,
                            variableName,
                            StringComparison.OrdinalIgnoreCase))
                        .Value;
                }

                return value switch
                {
                    null => $"<не задано: {variableName}>",
                    bool boolean => boolean ? "true" : "false",
                    IFormattable formattable =>
                        formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
                    _ => value.ToString() ?? string.Empty
                };
            });
        }
    }
}
