using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public sealed class GetUpsStatusStep : ITestStep
    {
        private readonly IHttpRequestService _httpRequestService;
        private readonly ILogger _logger;
        private readonly string _baseUrl;
        private readonly int _timeoutMs;
        private readonly string _outputVariableName;
        private readonly bool _failOnError;

        public GetUpsStatusStep(
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
            _outputVariableName = string.IsNullOrWhiteSpace(outputVariableName) ? "Dut.ups_rez" : outputVariableName.Trim();
            _failOnError = failOnError;
        }

        public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            var url = _baseUrl.TrimEnd('/') + "/api/getUpsStatus";
            var result = await _httpRequestService.GetAsync(url, TimeSpan.FromMilliseconds(_timeoutMs), cancellationToken);
            var raw = result.Body.Trim();

            if (string.IsNullOrWhiteSpace(result.ErrorMessage) &&
                result.IsSuccessStatusCode &&
                int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                context.SetVariable(_outputVariableName, value);
                context.SetVariable("GetUpsStatus.RawResponse", raw);
                context.SetVariable("GetUpsStatus.Success", true);
                context.SetVariable("GetUpsStatus.Error", string.Empty);
                _logger.Info($"[OK] UPS status: {value}.");
                return StepResult.True;
            }

            var error = !string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? result.ErrorMessage
                : $"Некорректный ответ UPS status: '{raw}'.";

            context.SetVariable("GetUpsStatus.RawResponse", raw);
            context.SetVariable("GetUpsStatus.Success", false);
            context.SetVariable("GetUpsStatus.Error", error);
            _logger.Warning($"[ОШИБКА] UPS status не получен: {error}");
            return _failOnError ? StepResult.False : StepResult.True;
        }
    }
}
