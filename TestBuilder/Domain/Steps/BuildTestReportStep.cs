using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services;
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
            context.Variables.Remove("BuildReport.RtlPayload");

            if (_template?.Validate() is { } error)
                return Task.FromResult(Fail(context, error));

            var series = context.ProductionModel?.SerialSeries;
            var rtl = context.ProductionModel?.RtlReport;
            if (series == null && SerialNumberPrefix.Validate(_expectedSerialPrefix) is { } prefixError)
                return Task.FromResult(Fail(context, prefixError));

            if (string.IsNullOrWhiteSpace(_standId))
            {
                return Task.FromResult(Fail(
                    context,
                    "Stand ID не задан. Укажи его во вкладке Настройки."));
            }

            var actualDeviceIdent = rtl == null ? string.Empty : GetVariableText(context, rtl.DeviceIdentifierVariable).Trim();
            var issuedSerial = context.IssuedSerialNumber;
            var failureBeforeAssignment = context.IsCleanup && context.GetVariable<bool>("Execution.SendFailureReport") &&
                !issuedSerial.HasValue;
            var usesFailureRoute = rtl != null && failureBeforeAssignment;
            var deviceIdent = usesFailureRoute ? rtl?.FailureDeviceIdentifier?.Trim() ?? string.Empty : actualDeviceIdent;
            if (usesFailureRoute && deviceIdent.Length == 0)
                return Task.FromResult(Fail(context, "В справочнике изделия не задан серверный идентификатор для отчётов об отказах."));
            if (rtl != null && string.IsNullOrEmpty(deviceIdent))
                return Task.FromResult(Fail(context, $"Идентификатор платы не найден в переменной '{rtl.DeviceIdentifierVariable}'."));

            var serial = GetVariableText(context, _serialVariableName);
            var fallbackSerial = rtl == null && failureBeforeAssignment && string.IsNullOrWhiteSpace(serial)
                ? series?.FailureReportSerialNumber : null;
            var assignOnReport = usesFailureRoute || (rtl?.AssignSerialOnReport == true && !issuedSerial.HasValue);
            long serialNumber = 0;
            if (fallbackSerial.HasValue)
                serialNumber = fallbackSerial.Value;
            else if (!assignOnReport &&
                (!long.TryParse(serial, NumberStyles.Integer, CultureInfo.InvariantCulture, out serialNumber) || serialNumber <= 0))
            {
                return Task.FromResult(Fail(
                    context,
                    $"Полный серийный номер не найден в переменной '{_serialVariableName}'."));
            }

            if (!assignOnReport)
            {
                var seriesError = series == null ? SerialNumberPrefix.Check(serialNumber, _expectedSerialPrefix)
                    : series.Check(serialNumber, issuedSerial);
                if (series == null && context.ProductionModel != null)
                    seriesError ??= ModelSerialSeries.CheckAssigned(serialNumber, issuedSerial);
                if (seriesError != null)
                    return Task.FromResult(Fail(context, $"Переменная '{_serialVariableName}': {seriesError}"));
                if (rtl != null && !issuedSerial.HasValue)
                    return Task.FromResult(Fail(context, "Сервер ещё не выдал номер для платы в текущем прогоне."));
            }

            var builder = new StringBuilder();
            var entries = context.ReportEntries.Concat(ProductionReportFields.GetEntries(context)).ToList();
            var successful = !usesFailureRoute && !fallbackSerial.HasValue && !context.HasCriticalError &&
                entries.All(entry => entry.IsSuccess);
            var reportEntries = new List<TestReportEntry>();
            var serialText = assignOnReport ? null : serialNumber.ToString(CultureInfo.InvariantCulture);

            AppendEntry(builder, "test_result", true, successful ? "1" : "0");
            AppendEntry(builder, "stand_id", true, _standId);
            if (serialText != null)
                AppendEntry(builder, SerialField, true, serialText);

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
                        reportEntries.Add(entry);
                }

                // A selected Boolean failure identifies the failed check, but does
                // not carry its diagnostic details (pair/IP, timeout, etc.). Keep
                // the stop reason even when that check is already in the template.
                if (!successful)
                {
                    var failedEntry = entries.FirstOrDefault(entry => !entry.IsSuccess);
                    reportEntries.Add(new TestReportEntry(ReportTemplate.FailureLabel, false, FailureReason(context, failedEntry)));
                }
            }
            else
            {
                reportEntries.Add(new TestReportEntry(ReportField.TestTypeLabel, true, _testType));

                foreach (var entry in entries)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Name) && !IsSerialField(entry.Name))
                        reportEntries.Add(entry);
                }

                if (_includeAllVariables)
                {
                    foreach (var variable in context.Variables
                                 .Where(item => ReportTemplate.IsLegacyReportVariable(item.Key, _reportVariableName) && !IsSerialField(item.Key))
                                 .OrderBy(item => item.Key, StringComparer.Ordinal))
                    {
                        reportEntries.Add(new TestReportEntry(
                            variable.Key,
                            true, // Diagnostic values are not independent pass/fail criteria.
                            ReportField.FormatText(variable.Value)));
                    }
                }
            }

            if (usesFailureRoute && actualDeviceIdent.Length > 0)
                reportEntries.Add(new TestReportEntry("Идентификатор проверенной платы", true, actualDeviceIdent));
            if (usesFailureRoute || fallbackSerial.HasValue)
            {
                if (!reportEntries.Any(entry => entry.Name == ReportTemplate.FailureLabel))
                    reportEntries.Add(new TestReportEntry(ReportTemplate.FailureLabel, false,
                        FailureReason(context, entries.FirstOrDefault(entry => !entry.IsSuccess))));
            }
            foreach (var entry in reportEntries)
                AppendEntry(builder, entry.Name, entry.IsSuccess, entry.Value);

            var report = builder.ToString();
            if (rtl != null)
                context.SetVariable("BuildReport.RtlPayload", new RtlTestReport(
                    report, rtl.DeviceType, deviceIdent, serialText, _standId, DateTime.UtcNow,
                    !context.IsCleanup && !context.HasCriticalError, successful, usesFailureRoute, reportEntries));
            context.SetVariable(_reportVariableName, report);
            context.SetVariable("BuildReport.Success", true);
            context.SetVariable("BuildReport.DevicePassed", successful);
            context.SetVariable("ReportDelivery.Status", "Pending");
            context.SetVariable("ReportDelivery.Message", "Отчёт сформирован, но не отправлен.");
            context.SetVariable("BuildReport.VariableName", _reportVariableName);
            context.SetVariable("BuildReport.Format", rtl == null ? "QTstand legacy text" : "RTL JSON");
            context.SetVariable("BuildReport.StandId", _standId);
            if (serialText == null) context.Variables.Remove("BuildReport.SerialNumber");
            else context.SetVariable("BuildReport.SerialNumber", serialNumber);
            context.SetVariable("BuildReport.SessionIncluded", !string.IsNullOrWhiteSpace(_sessionId));
            context.SetVariable("BuildReport.TestType", _testType);
            context.SetVariable("BuildReport.Error", string.Empty);

            if (fallbackSerial.HasValue)
                _logger.Info($"Для аварийного отчёта выбран служебный серийный номер {serialText}.");
            _logger.Info(
                $"[OK] Отчёт собран в переменную {_reportVariableName}: " +
                $"stand={_standId}, serial={serialText ?? "ожидается от сервера"}.");
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
