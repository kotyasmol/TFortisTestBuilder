using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    /// <summary>
    /// Формирует построчный отчёт в формате старого QTstand:
    /// name=true|false=value\r\n.
    /// </summary>
    public sealed class BuildTestReportStep : ITestStep
    {
        private readonly ILogger _logger;
        private readonly string _reportVariableName;
        private readonly string _standId;
        private readonly string _serialVariableName;
        private readonly string _sessionId;
        private readonly string _testType;
        private readonly bool _includeAllVariables;

        public BuildTestReportStep(
            ILogger logger,
            string reportVariableName,
            string standId,
            string serialVariableName,
            string sessionId,
            string testType,
            bool includeAllVariables)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _reportVariableName = string.IsNullOrWhiteSpace(reportVariableName)
                ? "TestReportText"
                : reportVariableName.Trim();
            _standId = standId?.Trim() ?? string.Empty;
            _serialVariableName = string.IsNullOrWhiteSpace(serialVariableName)
                ? "SerialNumber"
                : serialVariableName.Trim();
            _sessionId = sessionId?.Trim() ?? string.Empty;
            _testType = string.IsNullOrWhiteSpace(testType) ? "production" : testType.Trim();
            _includeAllVariables = includeAllVariables;
        }

        public Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(_standId))
            {
                return Task.FromResult(Fail(
                    context,
                    "Stand ID не задан. Укажи его во вкладке Настройки."));
            }

            var serial = GetVariableText(context, _serialVariableName);
            if (!long.TryParse(serial, NumberStyles.Integer, CultureInfo.InvariantCulture, out var serialNumber) ||
                serialNumber <= 0)
            {
                return Task.FromResult(Fail(
                    context,
                    $"Полный серийный номер не найден в переменной '{_serialVariableName}'."));
            }

            var builder = new StringBuilder();
            var successful = !context.HasCriticalError;

            AppendEntry(builder, "test_result", true, successful ? "1" : "0");
            AppendEntry(builder, "stand_id", true, _standId);
            AppendEntry(builder, "serial_num", true, serialNumber.ToString(CultureInfo.InvariantCulture));

            if (!string.IsNullOrWhiteSpace(_sessionId))
            {
                AppendEntry(builder, "session", true, _sessionId);
            }

            AppendEntry(builder, "Тип проверки", true, _testType);

            foreach (var entry in context.ReportEntries)
            {
                if (!string.IsNullOrWhiteSpace(entry.Name))
                {
                    AppendEntry(builder, entry.Name, entry.IsSuccess, entry.Value);
                }
            }

            if (_includeAllVariables)
            {
                foreach (var variable in context.Variables
                             .Where(item => ShouldIncludeVariable(item.Key))
                             .OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    AppendEntry(
                        builder,
                        variable.Key,
                        InferSuccess(variable.Key, variable.Value),
                        FormatValue(variable.Value));
                }
            }

            var report = builder.ToString();
            context.SetVariable(_reportVariableName, report);
            context.SetVariable("BuildReport.Success", true);
            context.SetVariable("BuildReport.VariableName", _reportVariableName);
            context.SetVariable("BuildReport.Format", "QTstand legacy text");
            context.SetVariable("BuildReport.StandId", _standId);
            context.SetVariable("BuildReport.SerialNumber", serialNumber);
            context.SetVariable("BuildReport.SessionIncluded", !string.IsNullOrWhiteSpace(_sessionId));
            context.SetVariable("BuildReport.TestType", _testType);
            context.SetVariable("BuildReport.Error", string.Empty);

            _logger.Info(
                $"[OK] Отчёт QTstand собран в переменную {_reportVariableName}: " +
                $"stand={_standId}, serial={serialNumber}.");
            return Task.FromResult(StepResult.True);
        }

        private StepResult Fail(TestContext context, string error)
        {
            context.Variables.Remove(_reportVariableName);
            context.SetVariable("BuildReport.Success", false);
            context.SetVariable("BuildReport.VariableName", _reportVariableName);
            context.SetVariable("BuildReport.Error", error);
            _logger.Warning($"[ОШИБКА] Отчёт не собран: {error}");
            return StepResult.False;
        }

        private bool ShouldIncludeVariable(string name)
        {
            return !string.Equals(name, _reportVariableName, StringComparison.Ordinal) &&
                   !name.StartsWith("BuildReport.", StringComparison.Ordinal) &&
                   !name.StartsWith("SendReport.", StringComparison.Ordinal);
        }

        private static bool InferSuccess(string name, object? value)
        {
            if (name.EndsWith(".Passed", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".Success", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".Ok", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("Received", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("PacketSent", StringComparison.OrdinalIgnoreCase))
            {
                if (value is bool boolean)
                {
                    return boolean;
                }

                if (bool.TryParse(value?.ToString(), out var parsed))
                {
                    return parsed;
                }
            }

            return true;
        }

        private static string GetVariableText(TestContext context, string variableName)
        {
            if (!context.Variables.TryGetValue(variableName, out var value))
            {
                value = context.Variables
                    .FirstOrDefault(item =>
                        string.Equals(item.Key, variableName, StringComparison.OrdinalIgnoreCase))
                    .Value;
            }

            return FormatValue(value);
        }

        private static string FormatValue(object? value)
        {
            return value switch
            {
                null => string.Empty,
                bool boolean => boolean ? "true" : "false",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
                _ => value.ToString() ?? string.Empty
            };
        }

        private static void AppendEntry(
            StringBuilder builder,
            string name,
            bool isSuccess,
            string value)
        {
            builder
                .Append(Sanitize(name))
                .Append('=')
                .Append(isSuccess ? "true" : "false")
                .Append('=')
                .Append(Sanitize(value))
                .Append("\r\n");
        }

        private static string Sanitize(string value)
        {
            return (value ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('=', ':');
        }
    }
}
