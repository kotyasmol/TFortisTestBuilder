using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public enum TestPageRequestStatus
    {
        Success,
        Timeout,
        NetworkError,
        EmptyResponse,
        InvalidContent,
        AuthenticationRequired,
        Cancelled
    }

    /// <summary>
    /// Доменный шаг запроса test.shtml с DUT.
    /// Получает сырую тестовую страницу и сохраняет ее в контекст для последующего парсинга.
    /// </summary>
    public sealed class RequestTestPageStep : ITestStep
    {
        public const string DefaultBaseUrl = "http://192.168.0.1";
        public const string DefaultPath = "/test.shtml";
        public const int DefaultTimeoutMs = 160000;
        public const int DefaultRetryCount = 1;
        public const int DefaultRetryDelayMs = 2000;
        public const string DefaultOutputVariableName = "TestPageRaw";
        public const string DefaultExpectedContentContains = "<!DOCTYPE settings>";
        public const string DefaultStatusCodeVariableName = "TestPageStatusCode";
        public const string DefaultErrorVariableName = "TestPageError";
        public const string DefaultElapsedMsVariableName = "TestPageElapsedMs";

        private readonly IHttpRequestService _httpRequestService;
        private readonly ILogger _logger;
        private readonly string _baseUrl;
        private readonly string _path;
        private readonly int _timeoutMs;
        private readonly int _retryCount;
        private readonly int _retryDelayMs;
        private readonly string _outputVariableName;
        private readonly bool _failOnError;
        private readonly bool _requireSuccessStatusCode;
        private readonly string _expectedContentContains;
        private readonly string _saveStatusCodeTo;
        private readonly string _saveErrorTo;
        private readonly string _saveElapsedMsTo;

        public RequestTestPageStep(
            IHttpRequestService httpRequestService,
            ILogger logger,
            string baseUrl,
            string path,
            int timeoutMs,
            int retryCount,
            int retryDelayMs,
            string outputVariableName,
            bool failOnError,
            bool requireSuccessStatusCode,
            string expectedContentContains,
            string saveStatusCodeTo,
            string saveErrorTo,
            string saveElapsedMsTo)
        {
            _httpRequestService = httpRequestService ?? throw new ArgumentNullException(nameof(httpRequestService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim();
            _path = string.IsNullOrWhiteSpace(path) ? DefaultPath : path.Trim();
            _timeoutMs = Math.Max(1, timeoutMs);
            _retryCount = Math.Max(0, retryCount);
            _retryDelayMs = Math.Max(0, retryDelayMs);
            _outputVariableName = NormalizeVariableName(outputVariableName, DefaultOutputVariableName);
            _failOnError = failOnError;
            _requireSuccessStatusCode = requireSuccessStatusCode;
            _expectedContentContains = expectedContentContains ?? string.Empty;
            _saveStatusCodeTo = NormalizeVariableName(saveStatusCodeTo, DefaultStatusCodeVariableName);
            _saveErrorTo = NormalizeVariableName(saveErrorTo, DefaultErrorVariableName);
            _saveElapsedMsTo = NormalizeVariableName(saveElapsedMsTo, DefaultElapsedMsVariableName);
        }

        public async Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var url = BuildUrl(_baseUrl, _path);
            var timeout = TimeSpan.FromMilliseconds(_timeoutMs);
            var totalStopwatch = Stopwatch.StartNew();
            var attempts = _retryCount + 1;

            _logger.Info($"[ШАГ] Запрос тестовой страницы: {url}");

            HttpRequestResult? lastResult = null;
            var lastStatus = TestPageRequestStatus.NetworkError;
            var lastError = string.Empty;

            try
            {
                for (var attempt = 1; attempt <= attempts; attempt++)
                {
                    if (attempt > 1)
                    {
                        _logger.Info($"Повторный запрос тестовой страницы: попытка {attempt} из {attempts}.");
                    }

                    lastResult = await _httpRequestService.GetAsync(
                        url,
                        timeout,
                        cancellationToken);

                    if (TryGetSuccess(lastResult, out lastStatus, out lastError))
                    {
                        SaveSuccess(context, url, lastResult, totalStopwatch.Elapsed);

                        _logger.Info(
                            $"[OK] Тестовая страница была загружена: HTTP {lastResult.StatusCode}, " +
                            $"{lastResult.Body.Length} символов, {totalStopwatch.Elapsed.TotalMilliseconds:0} мс.");

                        return StepResult.True;
                    }

                    if (attempt < attempts)
                    {
                        _logger.Warning($"Тестовая страница не была загружена: {lastError}. Повтор через {_retryDelayMs} мс.");

                        if (_retryDelayMs > 0)
                        {
                            await Task.Delay(_retryDelayMs, cancellationToken);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                lastStatus = TestPageRequestStatus.Cancelled;
                lastError = "Запрос тестовой страницы отменен.";
            }

            SaveFailure(
                context,
                url,
                lastStatus,
                lastError,
                lastResult?.StatusCode,
                totalStopwatch.Elapsed);

            if (lastStatus == TestPageRequestStatus.AuthenticationRequired)
            {
                _logger.Warning("Устройство требует аутентификации.");
            }

            _logger.Warning($"[ОШИБКА] Тестовая страница не была загружена: {lastError}");

            if (_failOnError)
            {
                context.HasCriticalError = true;
            }

            return StepResult.False;
        }

        private bool TryGetSuccess(
            HttpRequestResult result,
            out TestPageRequestStatus status,
            out string error)
        {
            if (IsAuthenticationRequired(result))
            {
                status = TestPageRequestStatus.AuthenticationRequired;
                error = "Устройство требует аутентификации.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                status = IsTimeout(result.ErrorMessage)
                    ? TestPageRequestStatus.Timeout
                    : TestPageRequestStatus.NetworkError;
                error = result.ErrorMessage;
                return false;
            }

            if (_requireSuccessStatusCode && !result.IsSuccessStatusCode)
            {
                status = TestPageRequestStatus.NetworkError;
                error = $"HTTP {(result.StatusCode?.ToString() ?? "unknown")}.";
                return false;
            }

            if (string.IsNullOrEmpty(result.Body))
            {
                status = TestPageRequestStatus.EmptyResponse;
                error = "Пустой ответ test.shtml.";
                return false;
            }

            if (!string.IsNullOrEmpty(_expectedContentContains) &&
                !result.Body.Contains(_expectedContentContains, StringComparison.Ordinal))
            {
                status = TestPageRequestStatus.InvalidContent;
                error = $"Ответ не содержит ожидаемую строку '{_expectedContentContains}'.";
                return false;
            }

            status = TestPageRequestStatus.Success;
            error = string.Empty;
            return true;
        }

        private void SaveSuccess(
            TestContext context,
            string url,
            HttpRequestResult result,
            TimeSpan elapsed)
        {
            context.SetVariable(_outputVariableName, result.Body);
            context.SetVariable("TestPageRequestOk", true);
            context.SetVariable("TestPageUrl", url);
            context.SetVariable("TestPageRequestStatus", TestPageRequestStatus.Success.ToString());
            context.SetVariable("TestPageReceivedAt", DateTime.Now);

            SetVariableWithAlias(context, DefaultStatusCodeVariableName, _saveStatusCodeTo, result.StatusCode ?? 0);
            SetVariableWithAlias(context, DefaultErrorVariableName, _saveErrorTo, string.Empty);
            SetVariableWithAlias(context, DefaultElapsedMsVariableName, _saveElapsedMsTo, (int)elapsed.TotalMilliseconds);
        }

        private void SaveFailure(
            TestContext context,
            string url,
            TestPageRequestStatus status,
            string error,
            int? statusCode,
            TimeSpan elapsed)
        {
            context.SetVariable("TestPageRequestOk", false);
            context.SetVariable("TestPageUrl", url);
            context.SetVariable("TestPageRequestStatus", status.ToString());

            SetVariableWithAlias(context, DefaultStatusCodeVariableName, _saveStatusCodeTo, statusCode ?? 0);
            SetVariableWithAlias(context, DefaultErrorVariableName, _saveErrorTo, error);
            SetVariableWithAlias(context, DefaultElapsedMsVariableName, _saveElapsedMsTo, (int)elapsed.TotalMilliseconds);
        }

        private static void SetVariableWithAlias(
            TestContext context,
            string defaultName,
            string alias,
            object value)
        {
            context.SetVariable(defaultName, value);

            if (!string.Equals(defaultName, alias, StringComparison.Ordinal))
            {
                context.SetVariable(alias, value);
            }
        }

        private static bool IsAuthenticationRequired(HttpRequestResult result)
        {
            if (result.StatusCode is 401 or 403)
            {
                return true;
            }

            return result.ErrorMessage.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
                   result.ErrorMessage.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
                   result.ErrorMessage.Contains("аутентифика", StringComparison.OrdinalIgnoreCase) ||
                   result.ErrorMessage.Contains("204", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTimeout(string error)
        {
            return error.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                   error.Contains("таймаут", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildUrl(string baseUrl, string path)
        {
            var normalizedPath = path.StartsWith("/", StringComparison.Ordinal)
                ? path
                : "/" + path;

            return baseUrl.TrimEnd('/') + normalizedPath;
        }

        private static string NormalizeVariableName(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? fallback
                : value.Trim();
        }
    }
}
