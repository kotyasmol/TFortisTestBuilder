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
        private const string SerialField = "serial_num";
        private readonly ILogger _logger;
        private readonly string _reportVariableName;
        private readonly string _standId;
        private readonly string _serialVariableName;
        private readonly string _sessionId;
        private readonly string _testType;
        private readonly bool _includeAllVariables;
        private readonly ReportTemplate? _template;
        private readonly string _expectedSerialPrefix;

        public BuildTestReportStep(
            ILogger logger,
            string reportVariableName,
            string standId,
            string serialVariableName,
            string sessionId,
            string testType,
            bool includeAllVariables,
            ReportTemplate? template = null,
            string? expectedSerialPrefix = null)
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
            _template = template;
            _expectedSerialPrefix = expectedSerialPrefix?.Trim() ?? string.Empty;
        }

        public Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_template?.Validate() is { } error)
                return Task.FromResult(Fail(context, error));

            if (SerialNumberPrefix.Validate(_expectedSerialPrefix) is { } prefixError)
                return Task.FromResult(Fail(context, prefixError));

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

            if (SerialNumberPrefix.Check(serialNumber, _expectedSerialPrefix) is { } seriesError)
                return Task.FromResult(Fail(context, $"Переменная '{_serialVariableName}': {seriesError}"));

            var builder = new StringBuilder();
            var entries = context.ReportEntries.Concat(ProductionReportFields.GetEntries(context)).ToList();
            var successful = !context.HasCriticalError && entries.All(entry => entry.IsSuccess);

            AppendEntry(builder, "test_result", true, successful ? "1" : "0");
            AppendEntry(builder, "stand_id", true, _standId);
            AppendEntry(builder, SerialField, true, serialNumber.ToString(CultureInfo.InvariantCulture));

            if (!string.IsNullOrWhiteSpace(_sessionId))
            {
                AppendEntry(builder, "session", true, _sessionId);
            }

            if (_template != null)
            {
                var selected = _template.Project(context, entries, _testType, _reportVariableName);
                foreach (var entry in selected)
                {
                    if (!IsSerialField(entry.Name))
                        AppendEntry(builder, entry.Name, entry.IsSuccess, entry.Value);
                }

                if (!successful && selected.All(entry => entry.IsSuccess))
                {
                    var failedEntry = entries.FirstOrDefault(entry => !entry.IsSuccess);
                    AppendEntry(builder, ReportTemplate.FailureLabel, false, FailureReason(context, failedEntry));
                }
            }
            else
            {
                AppendEntry(builder, ReportField.TestTypeLabel, true, _testType);

                foreach (var entry in entries)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Name) && !IsSerialField(entry.Name))
                        AppendEntry(builder, entry.Name, entry.IsSuccess, entry.Value);
                }

                if (_includeAllVariables)
                {
                    foreach (var variable in context.Variables
                                 .Where(item => ReportTemplate.IsLegacyReportVariable(item.Key, _reportVariableName) && !IsSerialField(item.Key))
                                 .OrderBy(item => item.Key, StringComparer.Ordinal))
                    {
                        AppendEntry(
                            builder,
                            variable.Key,
                            true, // Diagnostic values are not independent pass/fail criteria.
                            ReportField.FormatText(variable.Value));
                    }
                }
            }

            var report = builder.ToString();
            context.SetVariable(_reportVariableName, report);
            context.SetVariable("BuildReport.Success", true);
            context.SetVariable("BuildReport.DevicePassed", successful);
            context.SetVariable("ReportDelivery.Status", "Pending");
            context.SetVariable("ReportDelivery.Message", "Отчёт сформирован, но не отправлен.");
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
            context.Variables.Remove("BuildReport.SerialNumber");
            context.SetVariable("BuildReport.Success", false);
            context.SetVariable("BuildReport.VariableName", _reportVariableName);
            context.SetVariable("BuildReport.Error", error);
            _logger.Warning($"[ОШИБКА] Отчёт не собран: {error}");
            return StepResult.False;
        }

        private static string FailureReason(TestContext context, TestReportEntry? failedEntry)
        {
            // Cleanup can update the transient node failure. Use the cause retained from the test first.
            if (context.GetVariable<string>("Execution.Error") is { Length: > 0 } reason)
                return reason;
            if ((context.CriticalFailure ?? context.Failure) is { } failure)
                return failure.Summary;
            if (context.GetVariable<string>("Execution.Status") == nameof(ExecutionStatus.Cancelled))
                return "Проверка остановлена до завершения.";

            return failedEntry switch
            {
                { Name.Length: > 0, Value.Length: > 0 } => $"{failedEntry.Name}: {failedEntry.Value}",
                { Name.Length: > 0 } => failedEntry.Name,
                _ => "Проверка завершилась с ошибкой."
            };
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

            return ReportField.FormatText(value);
        }

        private static bool IsSerialField(string name) =>
            string.Equals(name.Trim(), SerialField, StringComparison.OrdinalIgnoreCase);

        private static void AppendEntry(
            StringBuilder builder,
            string name,
            bool isSuccess,
            string value)
        {
            builder
                .Append(ReportField.SanitizeWireText(name))
                .Append('=')
                .Append(isSuccess ? "true" : "false")
                .Append('=')
                .Append(ReportField.SanitizeWireText(value))
                .Append("\r\n");
        }
    }
}
