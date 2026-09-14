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
        private readonly int? _fixedSerialNumber;

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
            bool failOnError,
            int? fixedSerialNumber = null)
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
            _fixedSerialNumber = fixedSerialNumber;
        }

        public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            ResetResult(context);

            if (_fixedSerialNumber.HasValue)
            {
                return UseFixedSerialNumber(context, _fixedSerialNumber.Value);
            }

            string allocationUrl;
            string lookupUrl;
            string cpuId;
            try
            {
                BuildUrls(context, out cpuId, out lookupUrl, out allocationUrl);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is UriFormatException)
            {
                return Fail(context, ex.Message, string.Empty, string.Empty);
            }

            context.SetVariable("SerialNumberDeviceType", _deviceType);
            context.SetVariable("SerialNumberCpuId", cpuId);
            context.SetVariable("SerialNumberLookupUrl", lookupUrl);
            context.SetVariable("SerialNumberAllocationUrl", allocationUrl);

            var timeout = TimeSpan.FromMilliseconds(_timeoutMs);
            var totalAttempts = 0;

            if (!string.IsNullOrWhiteSpace(lookupUrl))
            {
                var lookupAttempts = _retryCount + 1;
                string lookupError = string.Empty;
                string lookupRaw = string.Empty;

                _logger.Info(
                    $"[ШАГ] Проверка существующего серийного номера: cpuId={cpuId}, " +
                    $"timeout={_timeoutMs} мс, попыток={lookupAttempts}, url={lookupUrl}");

                for (var attempt = 1; attempt <= lookupAttempts; attempt++)
                {
                    var result = await _httpRequestService.GetAsync(lookupUrl, timeout, cancellationToken);
                    totalAttempts++;
                    lookupRaw = result.Body.Trim();
                    SaveAttemptDiagnostics(context, totalAttempts, result, lookupRaw);
                    SaveLookupDiagnostics(context, attempt, result, lookupRaw);

                    if (string.IsNullOrWhiteSpace(result.ErrorMessage) &&
                        result.IsSuccessStatusCode &&
                        TryParseNonNegativeSerial(lookupRaw, out var existingSerial))
                    {
                        if (existingSerial > 0)
                        {
                            context.SetVariable("SerialNumberSource", "ServerExisting");
                            context.SetVariable("SerialNumberWasAllocated", false);
                            SaveSerial(context, existingSerial, lookupRaw, lookupUrl);
                            _logger.Info(
                                $"[OK] Для CPU ID {cpuId} уже существует серийный номер {existingSerial}; " +
                                "новый номер не запрашивается.");
                            return StepResult.True;
                        }

                        _logger.Info(
                            $"[ШАГ] Для CPU ID {cpuId} существующий серийный номер не найден; " +
                            "будет выполнен один запрос нового номера.");
                        break;
                    }

                    lookupError = BuildError(result, lookupRaw);

                    if (attempt < lookupAttempts && _retryDelayMs > 0)
                    {
                        _logger.Warning(
                            $"Существующий серийный номер не проверен (попытка {attempt}/{lookupAttempts}): " +
                            $"{lookupError}. Повтор через {_retryDelayMs} мс.");
                        await Task.Delay(_retryDelayMs, cancellationToken);
                    }
                }

                if (!context.GetVariable<bool>("SerialNumberLookupConfirmedMissing"))
                {
                    var error = string.IsNullOrWhiteSpace(lookupError)
                        ? "сервер не подтвердил отсутствие серийного номера ответом 0"
                        : lookupError;
                    return Fail(
                        context,
                        $"Проверка существующего серийного номера не выполнена: {error}. " +
                        "Новый номер не запрашивался, чтобы не создать дубль.",
                        lookupRaw,
                        lookupUrl);
                }
            }

            _logger.Info(
                $"[ШАГ] Запрос нового серийного номера: device={_deviceType}, cpuId={cpuId}, " +
                $"timeout={_timeoutMs} мс, одна попытка, url={allocationUrl}");

            var allocationResult = await _httpRequestService.GetAsync(allocationUrl, timeout, cancellationToken);
            totalAttempts++;
            var allocationRaw = allocationResult.Body.Trim();
            SaveAttemptDiagnostics(context, totalAttempts, allocationResult, allocationRaw);
            SaveAllocationDiagnostics(context, allocationResult, allocationRaw);

            if (TryParseSerial(allocationRaw, out var allocatedSerial) &&
                string.IsNullOrWhiteSpace(allocationResult.ErrorMessage) &&
                allocationResult.IsSuccessStatusCode)
            {
                context.SetVariable("SerialNumberSource", "ServerNew");
                context.SetVariable("SerialNumberWasAllocated", true);
                SaveSerial(context, allocatedSerial, allocationRaw, allocationUrl);
                _logger.Info($"[OK] Новый серийный номер получен: {allocatedSerial}.");
                return StepResult.True;
            }

            return Fail(
                context,
                $"Новый серийный номер не получен: {BuildError(allocationResult, allocationRaw)}. " +
                "Запрос не повторяется автоматически: следующий запуск сначала проверит привязку по CPU ID.",
                allocationRaw,
                allocationUrl);
        }

        private StepResult UseFixedSerialNumber(TestContext context, int serial)
        {
            if (serial <= 0)
            {
                return Fail(
                    context,
                    $"Фиксированный отладочный серийный номер должен быть положительным, получено {serial}.",
                    string.Empty,
                    string.Empty);
            }

            var cpuId = ResolveCpuIdForDiagnostics(context);
            var raw = serial.ToString(CultureInfo.InvariantCulture);

            context.SetVariable("SerialNumberDeviceType", _deviceType);
            context.SetVariable("SerialNumberCpuId", cpuId);
            context.SetVariable("SerialNumberSource", "FixedDebug");
            SaveSerial(context, serial, raw, string.Empty);

            _logger.Info(
                $"[ОТЛАДКА] Используется фиксированный серийный номер {serial}; " +
                "запрос к серверу серийников не выполнялся.");
            return StepResult.True;
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
            context.SetVariable("SerialNumberSource", string.Empty);
            context.SetVariable("SerialNumberWasAllocated", false);
            context.SetVariable("SerialNumberLookupUrl", string.Empty);
            context.SetVariable("SerialNumberLookupAttempts", 0);
            context.SetVariable("SerialNumberLookupStatusCode", 0);
            context.SetVariable("SerialNumberLookupRawResponse", string.Empty);
            context.SetVariable("SerialNumberLookupConfirmedMissing", false);
            context.SetVariable("SerialNumberAllocationUrl", string.Empty);
            context.SetVariable("SerialNumberAllocationAttempts", 0);
            context.SetVariable("SerialNumberAllocationStatusCode", 0);
            context.SetVariable("SerialNumberAllocationRawResponse", string.Empty);
        }

        private string ResolveCpuIdForDiagnostics(TestContext context)
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

            return cpuIdValue?.ToString()?.Trim() ?? string.Empty;
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

        private static void SaveLookupDiagnostics(
            TestContext context,
            int attempt,
            HttpRequestResult result,
            string raw)
        {
            context.SetVariable("SerialNumberLookupAttempts", attempt);
            context.SetVariable("SerialNumberLookupStatusCode", result.StatusCode ?? 0);
            context.SetVariable("SerialNumberLookupRawResponse", raw);
            context.SetVariable(
                "SerialNumberLookupConfirmedMissing",
                string.IsNullOrWhiteSpace(result.ErrorMessage) &&
                result.IsSuccessStatusCode &&
                TryParseNonNegativeSerial(raw, out var serial) &&
                serial == 0);
        }

        private static void SaveAllocationDiagnostics(
            TestContext context,
            HttpRequestResult result,
            string raw)
        {
            context.SetVariable("SerialNumberAllocationAttempts", 1);
            context.SetVariable("SerialNumberAllocationStatusCode", result.StatusCode ?? 0);
            context.SetVariable("SerialNumberAllocationRawResponse", raw);
        }

        private void BuildUrls(
            TestContext context,
            out string cpuId,
            out string lookupUrl,
            out string allocationUrl)
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
                var allocationBuilder = new UriBuilder(endpointUri)
                {
                    Path = endpointUri.AbsolutePath.TrimEnd('/'),
                    Query = BuildAllocationQuery(cpuId)
                };

                allocationUrl = allocationBuilder.Uri.AbsoluteUri;
                lookupUrl = BuildLookupUrl(endpointUri, cpuId);
                return;
            }

            var baseUri = new Uri(baseUrl, UriKind.Absolute);
            var allocationBuilderFromBase = new UriBuilder(baseUri)
            {
                Path = BuildEndpointPath(baseUri.AbsolutePath, "getSerialNum"),
                Query = BuildAllocationQuery(cpuId)
            };

            allocationUrl = allocationBuilderFromBase.Uri.AbsoluteUri;

            var lookupBuilder = new UriBuilder(baseUri)
            {
                Path = BuildEndpointPath(baseUri.AbsolutePath, "getExistsSerialNum"),
                Query = BuildLookupQuery(cpuId)
            };
            lookupUrl = string.IsNullOrWhiteSpace(cpuId) ? string.Empty : lookupBuilder.Uri.AbsoluteUri;
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

        private string BuildAllocationQuery(string cpuId)
        {
            var query = $"devType={Uri.EscapeDataString(_deviceType)}";

            if (!string.IsNullOrWhiteSpace(cpuId))
            {
                query += $"&cpuId={Uri.EscapeDataString(cpuId)}";
            }

            return query;
        }

        private static string BuildLookupQuery(string cpuId) =>
            string.IsNullOrWhiteSpace(cpuId)
                ? string.Empty
                : $"cpuId={Uri.EscapeDataString(cpuId)}";

        private static string BuildLookupUrl(Uri allocationEndpoint, string cpuId)
        {
            if (string.IsNullOrWhiteSpace(cpuId))
            {
                return string.Empty;
            }

            var allocationPath = allocationEndpoint.AbsolutePath.TrimEnd('/');
            var lastSlash = allocationPath.LastIndexOf('/');
            var parentPath = lastSlash >= 0 ? allocationPath[..lastSlash] : string.Empty;
            var builder = new UriBuilder(allocationEndpoint)
            {
                Path = $"{parentPath}/getExistsSerialNum",
                Query = BuildLookupQuery(cpuId)
            };
            return builder.Uri.AbsoluteUri;
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

        private static string BuildEndpointPath(string basePath, string endpointName)
        {
            var normalizedBase = string.IsNullOrWhiteSpace(basePath) || basePath == "/"
                ? string.Empty
                : basePath.TrimEnd('/');

            if (normalizedBase.EndsWith("/api/api.svc", StringComparison.OrdinalIgnoreCase))
            {
                return $"{normalizedBase}/{endpointName}";
            }

            if (normalizedBase.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                return $"{normalizedBase}/api.svc/{endpointName}";
            }

            return $"{normalizedBase}/api/api.svc/{endpointName}";
        }

        private static bool TryParseSerial(string raw, out int serial)
        {
            return TryParseNonNegativeSerial(raw, out serial) && serial > 0;
        }

        private static bool TryParseNonNegativeSerial(string raw, out int serial)
        {
            serial = 0;
            var normalized = (raw ?? string.Empty)
                .Trim()
                .TrimStart('\uFEFF')
                .Trim();

            if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out serial) &&
                serial >= 0)
            {
                return true;
            }

            try
            {
                using var document = JsonDocument.Parse(normalized);
                var root = document.RootElement;

                if (root.ValueKind == JsonValueKind.Number && root.TryGetInt32(out serial))
                {
                    return serial >= 0;
                }

                if (root.ValueKind == JsonValueKind.String &&
                    int.TryParse(root.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out serial))
                {
                    return serial >= 0;
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
