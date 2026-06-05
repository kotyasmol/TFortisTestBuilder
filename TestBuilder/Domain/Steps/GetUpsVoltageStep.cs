using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public sealed class GetUpsVoltageStep : ITestStep
    {
        private readonly IHttpRequestService _httpRequestService;
        private readonly ILogger _logger;
        private readonly string _baseUrl;
        private readonly int _timeoutMs;
        private readonly string _outputVariableName;
        private readonly bool _failOnError;

        public GetUpsVoltageStep(
            IHttpRequestService httpRequestService,
            ILogger logger,
            string baseUrl,
            int timeoutMs,
            string outputVariableName,
            bool failOnError)
        {
            _httpRequestService = httpRequestService ?? throw new ArgumentNullException(nameof(httpRequestService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://192.168.0.1" : baseUrl.Trim();
            _timeoutMs = Math.Max(1, timeoutMs);
            _outputVariableName = string.IsNullOrWhiteSpace(outputVariableName) ? "Dut.akb_voltage" : outputVariableName.Trim();
            _failOnError = failOnError;
        }

        public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            var url = _baseUrl.TrimEnd('/') + "/api/getUpsVoltage";
            var result = await _httpRequestService.GetAsync(url, TimeSpan.FromMilliseconds(_timeoutMs), cancellationToken);
            var raw = result.Body.Trim();

            if (string.IsNullOrWhiteSpace(result.ErrorMessage) &&
                result.IsSuccessStatusCode &&
                double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                context.SetVariable(_outputVariableName, value);
                context.SetVariable("GetUpsVoltage.RawResponse", raw);
                context.SetVariable("GetUpsVoltage.Success", true);
                context.SetVariable("GetUpsVoltage.Error", string.Empty);
                _logger.Info($"[OK] UPS voltage: {value.ToString(CultureInfo.InvariantCulture)}.");
                return StepResult.True;
            }

            var error = !string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? result.ErrorMessage
                : $"Некорректный ответ UPS voltage: '{raw}'.";

            context.SetVariable("GetUpsVoltage.RawResponse", raw);
            context.SetVariable("GetUpsVoltage.Success", false);
            context.SetVariable("GetUpsVoltage.Error", error);
            _logger.Warning($"[ОШИБКА] UPS voltage не получен: {error}");
            return _failOnError ? StepResult.False : StepResult.True;
        }
    }
}
