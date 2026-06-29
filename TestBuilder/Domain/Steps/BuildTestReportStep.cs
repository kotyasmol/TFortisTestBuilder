using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public sealed class BuildTestReportStep : ITestStep
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly ILogger _logger;
        private readonly string _reportVariableName;
        private readonly string _deviceName;
        private readonly int _deviceType;
        private readonly string _serialVariableName;
        private readonly string _macVariableName;
        private readonly bool _includeAllVariables;

        public BuildTestReportStep(
            ILogger logger,
            string reportVariableName,
            string deviceName,
            int deviceType,
            string serialVariableName,
            string macVariableName,
            bool includeAllVariables)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _reportVariableName = string.IsNullOrWhiteSpace(reportVariableName) ? "TestReportJson" : reportVariableName.Trim();
            _deviceName = string.IsNullOrWhiteSpace(deviceName) ? "PSW+UPS-Box 8x2Pro" : deviceName.Trim();
            _deviceType = deviceType;
            _serialVariableName = string.IsNullOrWhiteSpace(serialVariableName) ? "SerialShort" : serialVariableName.Trim();
            _macVariableName = string.IsNullOrWhiteSpace(macVariableName) ? "Dut.NewMac" : macVariableName.Trim();
            _includeAllVariables = includeAllVariables;
        }

        public Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var serial = GetVariableText(context, _serialVariableName);
            var mac = GetVariableText(context, _macVariableName);
            var variables = _includeAllVariables
                ? context.Variables
                    .OrderBy(x => x.Key, StringComparer.Ordinal)
                    .ToDictionary(x => x.Key, x => x.Value?.ToString() ?? string.Empty)
                : new Dictionary<string, string>();

            var report = new Dictionary<string, object?>
            {
                ["test_result"] = !context.HasCriticalError ? 1 : 0,
                ["profile"] = context.ProfileName ?? string.Empty,
                ["device_name"] = _deviceName,
                ["device_type"] = _deviceType,
                ["serial_num"] = serial,
                ["mac"] = mac,
                ["created_at"] = DateTimeOffset.Now.ToString("O"),
                ["variables"] = variables
            };

            var json = JsonSerializer.Serialize(report, JsonOptions);
            context.SetVariable(_reportVariableName, json);
            context.SetVariable("BuildReport.Success", true);
            context.SetVariable("BuildReport.VariableName", _reportVariableName);

            _logger.Info($"[OK] Отчет собран в переменную {_reportVariableName}.");
            return Task.FromResult(StepResult.True);
        }

        private static string GetVariableText(TestContext context, string variableName)
        {
            return context.Variables.TryGetValue(variableName, out var value)
                ? value?.ToString() ?? string.Empty
                : string.Empty;
        }
    }
}
