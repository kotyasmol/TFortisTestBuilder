using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public sealed class GetIrpStatusStep : ITestStep
    {
        private readonly IHttpRequestService _httpRequestService;
        private readonly ILogger _logger;
        private readonly string _baseUrl;
        private readonly int _timeoutMs;
        private readonly string _outputVariableName;
        private readonly bool _failOnError;

        public GetIrpStatusStep(
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
            _outputVariableName = string.IsNullOrWhiteSpace(outputVariableName) ? "Dut.ups_det" : outputVariableName.Trim();
            _failOnError = failOnError;
        }

        public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            var url = _baseUrl.TrimEnd('/') + "/api/isUps";
            var result = await _httpRequestService.GetAsync(url, TimeSpan.FromMilliseconds(_timeoutMs), cancellationToken);
            var raw = result.Body.Trim();

            if (string.IsNullOrWhiteSpace(result.ErrorMessage) &&
                result.IsSuccessStatusCode &&
                int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                context.SetVariable(_outputVariableName, value);
                context.SetVariable("GetIrpStatus.RawResponse", raw);
                context.SetVariable("GetIrpStatus.Success", true);
                context.SetVariable("GetIrpStatus.Error", string.Empty);
                _logger.Info($"[OK] IRP/UPS detect status: {value}.");
                return StepResult.True;
            }

            var error = !string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? result.ErrorMessage
                : $"Некорректный ответ IRP status: '{raw}'.";

            context.SetVariable("GetIrpStatus.RawResponse", raw);
            context.SetVariable("GetIrpStatus.Success", false);
            context.SetVariable("GetIrpStatus.Error", error);
            _logger.Warning($"[ОШИБКА] IRP status не получен: {error}");
            return _failOnError ? StepResult.False : StepResult.True;
        }
    }
}
