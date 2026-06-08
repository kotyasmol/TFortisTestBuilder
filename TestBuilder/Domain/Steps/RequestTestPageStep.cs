using System;
using System.Diagnostics;
using System.IO;
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
        public const string DefaultPath = "/cgi-bin/luci/admin/statistics/deviceinfo?luci_username=admin&luci_password=admin";
        public const int DefaultTimeoutMs = 160000;
        public const int DefaultRetryCount = 1;
        public const int DefaultRetryDelayMs = 2000;
        public const string DefaultOutputVariableName = "TestPageRaw";
        public const string DefaultExpectedContentContains = "default_mac";
        public const string DefaultStatusCodeVariableName = "TestPageStatusCode";
        public const string DefaultErrorVariableName = "TestPageError";
        public const string DefaultElapsedMsVariableName = "TestPageElapsedMs";
        public const string DefaultOutputFileName = "selftest.txt";
        private const string LegacyDefaultPath = "/test.shtml";
        private const string LegacyDefaultExpectedContentContains = "<!DOCTYPE settings>";

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
        private readonly bool _useBrowser;

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
            string saveElapsedMsTo,
            bool useBrowser = true)
        {
            _httpRequestService = httpRequestService ?? throw new ArgumentNullException(nameof(httpRequestService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim();
            _path = NormalizePath(path);
            _timeoutMs = Math.Max(1, timeoutMs);
            _retryCount = Math.Max(0, retryCount);
            _retryDelayMs = Math.Max(0, retryDelayMs);
            _outputVariableName = NormalizeVariableName(outputVariableName, DefaultOutputVariableName);
            _failOnError = failOnError;
            _requireSuccessStatusCode = requireSuccessStatusCode;
            _expectedContentContains = NormalizeExpectedContent(expectedContentContains);
            _saveStatusCodeTo = NormalizeVariableName(saveStatusCodeTo, DefaultStatusCodeVariableName);
            _saveErrorTo = NormalizeVariableName(saveErrorTo, DefaultErrorVariableName);
            _saveElapsedMsTo = NormalizeVariableName(saveElapsedMsTo, DefaultElapsedMsVariableName);
            _useBrowser = useBrowser;
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
            var selfTestXml = string.Empty;

            try
            {
                for (var attempt = 1; attempt <= attempts; attempt++)
                {
                    if (attempt > 1)
                    {
                        _logger.Info($"Повторный запрос тестовой страницы: попытка {attempt} из {attempts}.");
                    }

                    lastResult = await GetPageAsync(
                        url,
                        timeout,
                        cancellationToken);

                    if (TryGetSuccess(lastResult, out lastStatus, out lastError, out selfTestXml))
                    {
                        SaveSelfTestFile(selfTestXml);
                        SaveSuccess(context, url, lastResult, selfTestXml, totalStopwatch.Elapsed);

                        _logger.Info(
                            $"[OK] Тестовая страница была загружена: " +
                            $"{selfTestXml.Length} символов selftest, {totalStopwatch.Elapsed.TotalMilliseconds:0} мс.");

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
            SaveSelfTestFile("invalid testpage");

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
            out string error,
            out string selfTestXml)
        {
            selfTestXml = string.Empty;

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
                error = "Пустой ответ тестовой страницы.";
                return false;
            }

            if (!TryExtractSelfTestXml(result.Body, out selfTestXml))
            {
                status = TestPageRequestStatus.InvalidContent;
                error = "Ответ не содержит XML блок <selftest>...</selftest>.";
                return false;
            }

            if (!string.IsNullOrEmpty(_expectedContentContains) &&
                !selfTestXml.Contains(_expectedContentContains, StringComparison.Ordinal))
            {
                status = TestPageRequestStatus.InvalidContent;
                error = $"XML selftest не содержит ожидаемую строку '{_expectedContentContains}'.";
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
            string selfTestXml,
            TimeSpan elapsed)
        {
            context.SetVariable(_outputVariableName, selfTestXml);
            context.SetVariable("TestPageRequestOk", true);
            context.SetVariable("TestPageUrl", url);
            context.SetVariable("TestPageRequestStatus", TestPageRequestStatus.Success.ToString());
            context.SetVariable("TestPageReceivedAt", DateTime.Now);
            context.SetVariable("TestPageOutputFile", DefaultOutputFileName);

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

        private async Task<HttpRequestResult> GetPageAsync(
            string url,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (_useBrowser)
            {
                var browserPath = FindBrowserExecutable();

                if (!string.IsNullOrWhiteSpace(browserPath))
                {
                    return await GetPageWithBrowserAsync(
                        browserPath,
                        url,
                        timeout,
                        cancellationToken);
                }

                _logger.Warning("Headless Chrome/Edge не найден. Выполняется HTTP fallback без браузерного рендеринга.");
            }

            return await _httpRequestService.GetAsync(url, timeout, cancellationToken);
        }

        private static async Task<HttpRequestResult> GetPageWithBrowserAsync(
            string browserPath,
            string url,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var userDataDir = Path.Combine(Path.GetTempPath(), "TestBuilderHeadlessChrome_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(userDataDir);

                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = browserPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                process.StartInfo.ArgumentList.Add("--headless=new");
                process.StartInfo.ArgumentList.Add("--disable-gpu");
                process.StartInfo.ArgumentList.Add("--no-sandbox");
                process.StartInfo.ArgumentList.Add("--disable-dev-shm-usage");
                process.StartInfo.ArgumentList.Add("--window-size=1920,1080");
                process.StartInfo.ArgumentList.Add("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                process.StartInfo.ArgumentList.Add("--virtual-time-budget=" + Math.Max(1000, (int)timeout.TotalMilliseconds));
                process.StartInfo.ArgumentList.Add("--user-data-dir=" + userDataDir);
                process.StartInfo.ArgumentList.Add("--dump-dom");
                process.StartInfo.ArgumentList.Add(url);

                process.Start();

                var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
                var waitTask = process.WaitForExitAsync(cancellationToken);
                var timeoutTask = Task.Delay(timeout, cancellationToken);

                if (await Task.WhenAny(waitTask, timeoutTask) != waitTask)
                {
                    TryKill(process);
                    return HttpRequestResult.Failure(
                        $"Таймаут headless browser: {(int)timeout.TotalMilliseconds} мс.",
                        stopwatch.Elapsed);
                }

                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                if (process.ExitCode != 0)
                {
                    return HttpRequestResult.Failure(
                        string.IsNullOrWhiteSpace(stderr)
                            ? $"Headless browser завершился с кодом {process.ExitCode}."
                            : stderr.Trim(),
                        stopwatch.Elapsed,
                        process.ExitCode,
                        stdout);
                }

                return HttpRequestResult.Success(0, stdout, stopwatch.Elapsed);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return HttpRequestResult.Failure(ex.Message, stopwatch.Elapsed);
            }
            finally
            {
                TryDeleteDirectory(userDataDir);
            }
        }

        private static string? FindBrowserExecutable()
        {
            var candidates = new[]
            {
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Google",
                    "Chrome",
                    "Application",
                    "chrome.exe"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Google",
                    "Chrome",
                    "Application",
                    "chrome.exe"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Google",
                    "Chrome",
                    "Application",
                    "chrome.exe"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Microsoft",
                    "Edge",
                    "Application",
                    "msedge.exe"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Microsoft",
                    "Edge",
                    "Application",
                    "msedge.exe")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool TryExtractSelfTestXml(string pageSource, out string xml)
        {
            xml = string.Empty;
            const string startTag = "<selftest>";
            const string endTag = "</selftest>";

            var startIndex = pageSource.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
            var endIndex = pageSource.IndexOf(endTag, StringComparison.OrdinalIgnoreCase);

            if (startIndex < 0 || endIndex <= startIndex)
            {
                return false;
            }

            xml = pageSource.Substring(startIndex, endIndex - startIndex + endTag.Length);
            return true;
        }

        private static void SaveSelfTestFile(string content)
        {
            File.WriteAllText(DefaultOutputFileName, content);
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
            }
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

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return DefaultPath;
            }

            var trimmed = path.Trim();
            return string.Equals(trimmed, LegacyDefaultPath, StringComparison.OrdinalIgnoreCase)
                ? DefaultPath
                : trimmed;
        }

        private static string NormalizeExpectedContent(string? expectedContentContains)
        {
            if (string.IsNullOrWhiteSpace(expectedContentContains))
            {
                return DefaultExpectedContentContains;
            }

            var trimmed = expectedContentContains.Trim();
            return string.Equals(trimmed, LegacyDefaultExpectedContentContains, StringComparison.OrdinalIgnoreCase)
                ? DefaultExpectedContentContains
                : trimmed;
        }

        private static string NormalizeVariableName(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? fallback
                : value.Trim();
        }
    }
}
