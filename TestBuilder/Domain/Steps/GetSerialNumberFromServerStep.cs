using System;
using System.Globalization;
using System.Linq;
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

            _logger.Info($"[STEP] Serial number request: {url}");

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                var result = await _httpRequestService.GetAsync(url, timeout, cancellationToken);
                raw = result.Body.Trim();

                if (TryParseSerial(raw, out var serial) &&
                    string.IsNullOrWhiteSpace(result.ErrorMessage) &&
                    result.IsSuccessStatusCode)
                {
                    SaveSerial(context, serial, raw, url);
                    _logger.Info($"[OK] Serial number received: {serial}.");
                    return StepResult.True;
                }

                lastError = BuildError(result, raw);

                if (attempt < attempts && _retryDelayMs > 0)
                {
                    _logger.Warning($"Serial number was not received: {lastError}. Retry in {_retryDelayMs} ms.");
                    await Task.Delay(_retryDelayMs, cancellationToken);
                }
            }

            context.SetVariable("SerialNumberReceived", false);
            context.SetVariable("SerialNumberRawResponse", raw);
            context.SetVariable("SerialNumberRequestUrl", url);
            context.SetVariable("SerialNumberError", lastError);

            _logger.Warning($"[ERROR] Serial number was not received: {lastError}");
            return _failOnError ? StepResult.False : StepResult.True;
        }

        private void SaveSerial(TestContext context, int serial, string raw, string url)
        {
            context.SetVariable(_outputVariableName, serial);

            if (!string.Equals(_outputVariableName, "SerialNumber", StringComparison.Ordinal))
            {
                context.SetVariable("SerialNumber", serial);
            }

            context.SetVariable("NetTest.SerialNumber", serial);
            context.SetVariable("SerialNumberText", serial.ToString(CultureInfo.InvariantCulture));
            context.SetVariable("SerialNumberReceived", true);
            context.SetVariable("SerialNumberRawResponse", raw);
            context.SetVariable("SerialNumberRequestUrl", url);
            context.SetVariable("SerialNumberError", string.Empty);
        }

        private string BuildUrl(TestContext context)
        {
            var baseUrl = NormalizeServerBaseUrl(_serverBaseUrl);

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return string.Empty;
            }

            var cpuId = ResolveCpuId(context);

            if (LooksLikeGetSerialEndpoint(baseUrl))
            {
                var endpointBuilder = new UriBuilder(baseUrl)
                {
                    Query = BuildQuery(cpuId)
                };

                return endpointBuilder.Uri.AbsoluteUri;
            }

            var baseUri = new Uri(baseUrl, UriKind.Absolute);
            var builder = new UriBuilder(baseUri)
            {
                Path = CombinePath(baseUri.AbsolutePath, "api/api.svc/getSerialNum"),
                Query = BuildQuery(cpuId)
            };

            return builder.Uri.AbsoluteUri;
        }

        private string ResolveCpuId(TestContext context)
        {
            if (!string.IsNullOrWhiteSpace(_cpuIdVariableName) &&
                context.Variables.TryGetValue(_cpuIdVariableName, out var cpuIdValue))
            {
                return cpuIdValue?.ToString()?.Trim() ?? string.Empty;
            }

            return string.Empty;
        }

        private string BuildQuery(string cpuId)
        {
            var query = $"devType={Uri.EscapeDataString(_deviceType)}";

            if (!string.IsNullOrWhiteSpace(cpuId))
            {
                query += $"&cpuId={Uri.EscapeDataString(cpuId)}";
            }

            return query;
        }

        private static string NormalizeServerBaseUrl(string serverBaseUrl)
        {
            var trimmed = serverBaseUrl?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return string.Empty;
            }

            if (!trimmed.Contains("://", StringComparison.Ordinal))
            {
                trimmed = "http://" + trimmed;
            }

            return trimmed;
        }

        private static bool LooksLikeGetSerialEndpoint(string baseUrl)
        {
            return Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) &&
                   uri.AbsolutePath.EndsWith("/getSerialNum", StringComparison.OrdinalIgnoreCase);
        }

        private static string CombinePath(string basePath, string relativePath)
        {
            var normalizedBase = string.IsNullOrWhiteSpace(basePath) || basePath == "/"
                ? string.Empty
                : basePath.TrimEnd('/');

            return $"{normalizedBase}/{relativePath.TrimStart('/')}";
        }

        private static bool TryParseSerial(string raw, out int serial)
        {
            serial = 0;

            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out serial) &&
                serial > 0)
            {
                return true;
            }

            var digits = new string(raw.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out serial) &&
                   serial > 0;
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

            return $"Server response is not a positive serial number: '{raw}'.";
        }
    }
}
