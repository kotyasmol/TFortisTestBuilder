using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public sealed class GetSerialNumberFromServerStep : ITestStep
    {
        private readonly IHttpRequestService _httpRequestService;
        private readonly ILogger _logger;
        private readonly string _serverBaseUrl;
        private readonly string _deviceType;
        private readonly string _cpuIdVariableName;
        private readonly int _timeoutMs;
        private readonly int _retryCount;
        private readonly int _retryDelayMs;
        private readonly string _outputVariableName;
        private readonly bool _failOnError;

        public GetSerialNumberFromServerStep(
            IHttpRequestService httpRequestService,
            ILogger logger,
            string serverBaseUrl,
            string deviceType,
            string cpuIdVariableName,
            int timeoutMs,
            int retryCount,
            int retryDelayMs,
            string outputVariableName,
            bool failOnError)
        {
            _httpRequestService = httpRequestService ?? throw new ArgumentNullException(nameof(httpRequestService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serverBaseUrl = serverBaseUrl?.Trim() ?? string.Empty;
            _deviceType = deviceType?.Trim() ?? string.Empty;
            _cpuIdVariableName = cpuIdVariableName?.Trim() ?? string.Empty;
            _timeoutMs = Math.Max(1, timeoutMs);
            _retryCount = Math.Max(0, retryCount);
            _retryDelayMs = Math.Max(0, retryDelayMs);
            _outputVariableName = string.IsNullOrWhiteSpace(outputVariableName) ? "SerialNumber" : outputVariableName.Trim();
            _failOnError = failOnError;
        }

        public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var url = BuildUrl(context);
            var timeout = TimeSpan.FromMilliseconds(_timeoutMs);
            var attempts = _retryCount + 1;
            string lastError = string.Empty;
            string raw = string.Empty;

            _logger.Info($"[ШАГ] Запрос серийного номера: {url}");

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                var result = await _httpRequestService.GetAsync(url, timeout, cancellationToken);
                raw = result.Body.Trim();

                if (string.IsNullOrWhiteSpace(result.ErrorMessage) &&
                    result.IsSuccessStatusCode &&
                    int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var serial) &&
                    serial > 0)
                {
                    context.SetVariable(_outputVariableName, serial);
                    context.SetVariable("SerialNumberReceived", true);
                    context.SetVariable("SerialNumberRawResponse", raw);
                    context.SetVariable("SerialNumberRequestUrl", url);
                    context.SetVariable("SerialNumberError", string.Empty);

                    _logger.Info($"[OK] Получен серийный номер: {serial}.");
                    return StepResult.True;
                }

                lastError = BuildError(result, raw);

                if (attempt < attempts && _retryDelayMs > 0)
                {
                    _logger.Warning($"Серийный номер не получен: {lastError}. Повтор через {_retryDelayMs} мс.");
                    await Task.Delay(_retryDelayMs, cancellationToken);
                }
            }

            context.SetVariable("SerialNumberReceived", false);
            context.SetVariable("SerialNumberRawResponse", raw);
            context.SetVariable("SerialNumberRequestUrl", url);
            context.SetVariable("SerialNumberError", lastError);

            _logger.Warning($"[ОШИБКА] Серийный номер не получен: {lastError}");
            return _failOnError ? StepResult.False : StepResult.True;
        }

        private string BuildUrl(TestContext context)
        {
            var query = $"devType={Uri.EscapeDataString(_deviceType)}";

            if (!string.IsNullOrWhiteSpace(_cpuIdVariableName) &&
                context.Variables.TryGetValue(_cpuIdVariableName, out var cpuIdValue))
            {
                var cpuId = cpuIdValue?.ToString()?.Trim();

                if (!string.IsNullOrWhiteSpace(cpuId))
                {
                    query += $"&cpuId={Uri.EscapeDataString(cpuId)}";
                }
            }

            return _serverBaseUrl.TrimEnd('/') + "/api/api.svc/getSerialNum?" + query;
        }

        private static string BuildError(HttpRequestResult result, string raw)
        {
            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                return result.ErrorMessage;
            }

            if (!result.IsSuccessStatusCode)
            {
                return $"HTTP {(result.StatusCode?.ToString() ?? "unknown")}.";
            }

            return $"Ответ сервера не является положительным серийным номером: '{raw}'.";
        }
    }
}
