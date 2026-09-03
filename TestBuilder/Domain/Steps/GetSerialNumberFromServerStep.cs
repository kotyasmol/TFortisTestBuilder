using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services;
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

            ResetResult(context);

            string url;
            string cpuId;
            try
            {
                url = BuildUrl(context, out cpuId);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is UriFormatException)
            {
                return Fail(context, ex.Message, string.Empty, string.Empty);
            }

            context.SetVariable("SerialNumberDeviceType", _deviceType);
            context.SetVariable("SerialNumberCpuId", cpuId);
            context.SetVariable("SerialNumberRequestUrl", url);

            var timeout = TimeSpan.FromMilliseconds(_timeoutMs);
            var attempts = _retryCount + 1;
            string lastError = string.Empty;
            string raw = string.Empty;

            _logger.Info(
                $"[ШАГ] Запрос серийного номера: device={_deviceType}, cpuId={cpuId}, " +
                $"timeout={_timeoutMs} мс, попыток={attempts}, url={url}");

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                var result = await _httpRequestService.GetAsync(url, timeout, cancellationToken);
                raw = result.Body.Trim();
                SaveAttemptDiagnostics(context, attempt, result, raw);

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
                    _logger.Warning(
                        $"Серийный номер не получен (попытка {attempt}/{attempts}): {lastError}. " +
                        $"Повтор через {_retryDelayMs} мс.");
                    await Task.Delay(_retryDelayMs, cancellationToken);
                }
            }

            return Fail(context, lastError, raw, url);
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

        private StepResult Fail(TestContext context, string error, string raw, string url)
        {
            context.SetVariable("SerialNumberReceived", false);
            context.SetVariable("SerialNumberRawResponse", raw);
            context.SetVariable("SerialNumberRequestUrl", url);
            context.SetVariable("SerialNumberError", error);

            _logger.Warning($"[ОШИБКА] Серийный номер не получен: {error}");
            return _failOnError ? StepResult.False : StepResult.True;
        }

        private void ResetResult(TestContext context)
        {
            context.Variables.Remove(_outputVariableName);
            context.Variables.Remove("SerialNumber");
            context.Variables.Remove("NetTest.SerialNumber");
            context.Variables.Remove("SerialNumberText");

            context.SetVariable("SerialNumberReceived", false);
            context.SetVariable("SerialNumberRawResponse", string.Empty);
            context.SetVariable("SerialNumberRequestUrl", string.Empty);
            context.SetVariable("SerialNumberError", string.Empty);
            context.SetVariable("SerialNumberAttempts", 0);
            context.SetVariable("SerialNumberStatusCode", 0);
            context.SetVariable("SerialNumberElapsedMs", 0);
            context.SetVariable("SerialNumberDeviceType", _deviceType);
            context.SetVariable("SerialNumberCpuId", string.Empty);
        }

        private static void SaveAttemptDiagnostics(
            TestContext context,
            int attempt,
            HttpRequestResult result,
            string raw)
        {
            context.SetVariable("SerialNumberAttempts", attempt);
            context.SetVariable("SerialNumberStatusCode", result.StatusCode ?? 0);
            context.SetVariable("SerialNumberElapsedMs", (int)result.Elapsed.TotalMilliseconds);
            context.SetVariable("SerialNumberRawResponse", raw);
        }

        private string BuildUrl(TestContext context, out string cpuId)
        {
            var baseUrl = NormalizeServerBaseUrl(_serverBaseUrl);

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new InvalidOperationException(
                    "ServerBaseUrl не задан. Укажи адрес сервера серийников в ноде или во вкладке Настройки.");
            }

            if (string.IsNullOrWhiteSpace(_deviceType))
            {
                throw new InvalidOperationException("DeviceType не задан для запроса серийного номера.");
            }

            cpuId = ResolveCpuId(context);

            if (LooksLikeGetSerialEndpoint(baseUrl))
            {
                var endpointUri = new Uri(baseUrl, UriKind.Absolute);
                var endpointBuilder = new UriBuilder(endpointUri)
                {
                    Path = endpointUri.AbsolutePath.TrimEnd('/'),
                    Query = BuildQuery(cpuId)
                };

                return endpointBuilder.Uri.AbsoluteUri;
            }

            var baseUri = new Uri(baseUrl, UriKind.Absolute);
            var builder = new UriBuilder(baseUri)
            {
                Path = BuildEndpointPath(baseUri.AbsolutePath),
                Query = BuildQuery(cpuId)
            };

            return builder.Uri.AbsoluteUri;
        }

        private string ResolveCpuId(TestContext context)
        {
            if (string.IsNullOrWhiteSpace(_cpuIdVariableName))
            {
                return string.Empty;
            }

            if (!context.Variables.TryGetValue(_cpuIdVariableName, out var cpuIdValue))
            {
                cpuIdValue = context.Variables
                    .FirstOrDefault(item =>
                        string.Equals(item.Key, _cpuIdVariableName, StringComparison.OrdinalIgnoreCase))
                    .Value;
            }

            var cpuId = cpuIdValue?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cpuId))
            {
                throw new InvalidOperationException(
                    $"CPU ID не найден в переменной '{_cpuIdVariableName}'. " +
                    "Сначала выполни Selftest Check или очисти поле CPU var, если сервер допускает запрос без CPU ID.");
            }

            return cpuId;
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
            return ServerBaseUrlResolver.NormalizeForHttp(serverBaseUrl);
        }

        private static bool LooksLikeGetSerialEndpoint(string baseUrl)
        {
            return Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) &&
                   uri.AbsolutePath.TrimEnd('/').EndsWith("/getSerialNum", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildEndpointPath(string basePath)
        {
            var normalizedBase = string.IsNullOrWhiteSpace(basePath) || basePath == "/"
                ? string.Empty
                : basePath.TrimEnd('/');

            if (normalizedBase.EndsWith("/api/api.svc", StringComparison.OrdinalIgnoreCase))
            {
                return $"{normalizedBase}/getSerialNum";
            }

            if (normalizedBase.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                return $"{normalizedBase}/api.svc/getSerialNum";
            }

            return $"{normalizedBase}/api/api.svc/getSerialNum";
        }

        private static bool TryParseSerial(string raw, out int serial)
        {
            serial = 0;
            var normalized = (raw ?? string.Empty)
                .Trim()
                .TrimStart('\uFEFF')
                .Trim();

            if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out serial) &&
                serial > 0)
            {
                return true;
            }

            try
            {
                using var document = JsonDocument.Parse(normalized);
                var root = document.RootElement;

                if (root.ValueKind == JsonValueKind.Number && root.TryGetInt32(out serial))
                {
                    return serial > 0;
                }

                if (root.ValueKind == JsonValueKind.String &&
                    int.TryParse(root.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out serial))
                {
                    return serial > 0;
                }
            }
            catch (JsonException)
            {
                // Legacy API returns plain text. Invalid JSON is handled by the normal error path below.
            }

            serial = 0;
            return false;
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
