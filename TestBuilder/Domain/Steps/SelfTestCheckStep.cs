using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public sealed class SelfTestCheckStep : ITestStep
    {
        public const string DefaultUrl =
            "http://192.168.0.1/cgi-bin/luci/admin/statistics/deviceinfo?luci_username=admin&luci_password=admin";

        public const int DefaultTimeoutMs = 300000;
        public const int DefaultPollIntervalMs = 5000;
        public const string DefaultOutputPrefix = "Dut";
        public const string DefaultOutputVariableName = "SelfTestRaw";
        public const string DefaultValidationRules =
            "init_ok=1..1\n" +
            "dev_type=0..65535\n" +
            "firmvare_vers=0..65535\n" +
            "boot_vers=0..65535";

        private const int MinimumPollIntervalMs = 100;
        private const int LegacyShortTimeoutMs = 240000;
        private const int MinimumDeviceReadyTimeoutMs = 300000;
        private const int MaxPageAttemptTimeoutMs = 30000;
        private const int BrowserDomSettleDelayMs = 10000;
        private const int MaxExtractionCandidates = 64;

        private readonly IHttpRequestService _httpRequestService;
        private readonly ILogger _logger;
        private readonly string _url;
        private readonly int _timeoutMs;
        private readonly string _outputPrefix;
        private readonly string _validationRules;
        private readonly bool _failOnError;
        private readonly bool _useBrowser;
        private readonly int _pollIntervalMs;

        public SelfTestCheckStep(
            IHttpRequestService httpRequestService,
            ILogger logger,
            string url,
            int timeoutMs,
            string outputPrefix,
            string validationRules,
            bool failOnError,
            bool useBrowser = true,
            int pollIntervalMs = DefaultPollIntervalMs,
            bool enforceMinimumDeviceReadyTimeout = true)
        {
            _httpRequestService = httpRequestService ?? throw new ArgumentNullException(nameof(httpRequestService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _url = string.IsNullOrWhiteSpace(url) ? DefaultUrl : url.Trim();
            _timeoutMs = enforceMinimumDeviceReadyTimeout
                ? NormalizeSelfTestTimeout(_url, timeoutMs)
                : Math.Max(1, timeoutMs);
            _outputPrefix = string.IsNullOrWhiteSpace(outputPrefix)
                ? DefaultOutputPrefix
                : outputPrefix.Trim().TrimEnd('.');
            _validationRules = string.IsNullOrWhiteSpace(validationRules)
                ? DefaultValidationRules
                : validationRules;
            _failOnError = failOnError;
            _useBrowser = useBrowser;
            _pollIntervalMs = NormalizePollInterval(pollIntervalMs);
        }

        private static int NormalizeSelfTestTimeout(string url, int timeoutMs)
        {
            var normalized = timeoutMs <= 0 ? DefaultTimeoutMs : timeoutMs;
            return normalized <= LegacyShortTimeoutMs && IsDeviceSelfTestUrl(url)
                ? MinimumDeviceReadyTimeoutMs
                : normalized;
        }

        private static int NormalizePollInterval(int pollIntervalMs)
        {
            if (pollIntervalMs <= 0)
            {
                return DefaultPollIntervalMs;
            }

            return Math.Max(MinimumPollIntervalMs, pollIntervalMs);
        }

        public async Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            _logger.Info($"[STEP] Selftest request: {_url}, timeout {_timeoutMs} ms, poll every {_pollIntervalMs} ms.");
            context.SelfTestPageState?.BeginLoading(_url, _outputPrefix);

            SelfTestFetchResult fetch;
            try
            {
                fetch = await WaitForSelfTestXmlAsync(
                    _url,
                    TimeSpan.FromMilliseconds(Math.Max(1, _timeoutMs)),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                context.SelfTestPageState?.SetError(_url, _outputPrefix, "Загрузка отменена.");
                throw;
            }
            catch (Exception ex)
            {
                context.SelfTestPageState?.SetError(_url, _outputPrefix, ex.Message);
                throw;
            }

            var result = fetch.Result;

            context.SetVariable("SelfTest.Url", _url);
            context.SetVariable("SelfTest.StatusCode", result.StatusCode ?? 0);
            context.SetVariable("SelfTest.ElapsedMs", (int)fetch.Elapsed.TotalMilliseconds);
            context.SetVariable("SelfTest.Attempts", fetch.Attempts);
            context.SetVariable("SelfTest.PollIntervalMs", _pollIntervalMs);

            if (!string.IsNullOrWhiteSpace(fetch.ErrorMessage))
            {
                context.SetVariable(DefaultOutputVariableName, string.Empty);
                context.SelfTestPageState?.SetError(_url, _outputPrefix, fetch.ErrorMessage);
                return Fail(context, fetch.ErrorMessage);
            }

            var raw = fetch.RawXml;

            context.SetVariable(DefaultOutputVariableName, raw);

            if (!TryParseSelfTest(raw, out var document, out var parseError))
            {
                context.SelfTestPageState?.SetError(_url, _outputPrefix, parseError);
                return Fail(context, parseError);
            }

            var values = ExtractValues(document);
            context.SelfTestPageState?.SetLoaded(_url, _outputPrefix, values);
            foreach (var item in values)
            {
                context.SetVariable(BuildContextName(item.Key), item.Value);

                foreach (var alias in GetLegacyAliases(item.Key))
                {
                    context.SetVariable(BuildContextName(alias), item.Value);
                }
            }

            context.SetVariable("SelfTest.ParsedFieldCount", values.Count);
            LogParsedFieldSnapshot(values);

            var checks = Validate(values);
            LogValidationChecks(checks);
            SaveValidationSummary(context, checks);

            var errors = checks
                .Where(check => !check.Passed)
                .Select(check => check.Error)
                .ToList();
            if (errors.Count > 0)
            {
                return Fail(context, string.Join("; ", errors));
            }

            context.SetVariable("SelfTest.Ok", true);
            context.SetVariable("SelfTest.Error", string.Empty);
            _logger.Info($"[OK] Selftest passed: {values.Count} fields, {checks.Count} rules.");
            return StepResult.True;
        }

        private StepResult Fail(TestContext context, string error)
        {
            SetError(context, error);

            if (_failOnError)
            {
                context.HasCriticalError = true;
            }

            _logger.Warning($"[ERROR] Selftest failed: {error}");
            return StepResult.False;
        }

        private static void SetError(TestContext context, string error)
        {
            context.SetVariable("SelfTest.Ok", false);
            context.SetVariable("SelfTest.Error", error ?? string.Empty);
        }

        private List<ValidationCheckResult> Validate(Dictionary<string, string> values)
        {
            var results = new List<ValidationCheckResult>();

            foreach (var rule in GetRules())
            {
                if (!values.TryGetValue(rule.FieldName, out var rawValue))
                {
                    results.Add(ValidationCheckResult.Fail(
                        rule,
                        string.Empty,
                        $"{rule.FieldName}: field not found"));
                    continue;
                }

                if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var actual) &&
                    !double.TryParse(rawValue, NumberStyles.Float, CultureInfo.CurrentCulture, out actual))
                {
                    results.Add(ValidationCheckResult.Fail(
                        rule,
                        rawValue,
                        $"{rule.FieldName}: '{rawValue}' is not a number"));
                    continue;
                }

                if (actual < rule.Min || actual > rule.Max)
                {
                    results.Add(ValidationCheckResult.Fail(
                        rule,
                        rawValue,
                        $"{rule.FieldName}: {actual.ToString(CultureInfo.InvariantCulture)} outside " +
                        $"{rule.Min.ToString(CultureInfo.InvariantCulture)}..{rule.Max.ToString(CultureInfo.InvariantCulture)}"));
                    continue;
                }

                results.Add(ValidationCheckResult.Pass(rule, actual));
            }

            return results;
        }

        private void LogParsedFieldSnapshot(Dictionary<string, string> values)
        {
            var keyFields = GetRules()
                .Select(rule => rule.FieldName)
                .Concat(new[] { "default_mac", "cpu_id" })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(values.ContainsKey)
                .Select(field => $"{field}={values[field]}")
                .ToList();

            var preview = keyFields.Count == 0
                ? "no validation fields found"
                : string.Join(", ", keyFields);

            _logger.Info($"[INFO] Selftest fields parsed: {values.Count}. Key values: {preview}.");
        }

        private void LogValidationChecks(IReadOnlyList<ValidationCheckResult> checks)
        {
            foreach (var check in checks)
            {
                var expected = check.Rule.FormatRange();
                if (check.Passed)
                {
                    _logger.Info(
                        $"[OK] Selftest check {check.Rule.FieldName}: actual {check.ActualValue}, expected {expected}.");
                }
                else
                {
                    var actual = string.IsNullOrWhiteSpace(check.ActualValue)
                        ? "<missing>"
                        : check.ActualValue;
                    _logger.Warning(
                        $"[ERROR] Selftest check {check.Rule.FieldName}: actual {actual}, expected {expected}. {check.Error}");
                }
            }
        }

        private static void SaveValidationSummary(
            TestContext context,
            IReadOnlyList<ValidationCheckResult> checks)
        {
            var failed = checks.Where(check => !check.Passed).ToList();
            context.SetVariable("SelfTest.CheckedRuleCount", checks.Count);
            context.SetVariable("SelfTest.FailedRuleCount", failed.Count);
            context.SetVariable(
                "SelfTest.ValidationSummary",
                string.Join(
                    "; ",
                    checks.Select(check =>
                        check.Passed
                            ? $"{check.Rule.FieldName}=OK({check.ActualValue} in {check.Rule.FormatRange()})"
                            : $"{check.Rule.FieldName}=FAIL({check.Error})")));
        }

        private IEnumerable<ValidationRule> GetRules()
        {
            return _validationRules
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseRule)
                .Where(rule => rule != null)
                .Cast<ValidationRule>();
        }

        private static ValidationRule? ParseRule(string line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                return null;
            }

            var separator = trimmed.IndexOf('=');
            if (separator < 0)
            {
                separator = trimmed.IndexOf(':');
            }

            if (separator <= 0 || separator >= trimmed.Length - 1)
            {
                return null;
            }

            var field = trimmed.Substring(0, separator).Trim();
            var range = trimmed.Substring(separator + 1).Trim();
            var parts = range.Contains("..", StringComparison.Ordinal)
                ? range.Split(new[] { ".." }, StringSplitOptions.None)
                : range.Split(',');

            if (parts.Length != 2 ||
                !double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var min) ||
                !double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var max))
            {
                return null;
            }

            return new ValidationRule(field, min, max);
        }

        private string BuildContextName(string field)
        {
            return string.IsNullOrWhiteSpace(_outputPrefix)
                ? field
                : $"{_outputPrefix}.{field}";
        }

        private static bool TryParseSelfTest(
            string raw,
            out XDocument document,
            out string error)
        {
            document = new XDocument();
            error = string.Empty;
            raw = RepairLegacySelfTestXml(raw);

            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "Empty selftest.";
                return false;
            }

            try
            {
                document = XDocument.Parse(raw, LoadOptions.PreserveWhitespace);
            }
            catch (XmlException ex)
            {
                error = $"Invalid selftest XML: {ex.Message}";
                return false;
            }

            if (!IsSupportedSelfTestRoot(document.Root?.Name.LocalName))
            {
                error = "XML root is not <selftest> or <settings>.";
                return false;
            }

            return true;
        }

        private async Task<SelfTestFetchResult> WaitForSelfTestXmlAsync(
            string url,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var attempts = 0;
            var lastError = "Selftest page was not requested.";
            HttpRequestResult lastResult = HttpRequestResult.Failure(lastError, TimeSpan.Zero);

            while (stopwatch.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var remaining = timeout - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                attempts++;
                lastResult = await GetPageAsync(
                    url,
                    GetAttemptTimeout(remaining),
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(lastResult.ErrorMessage))
                {
                    lastError = lastResult.ErrorMessage;
                }
                else if (!lastResult.IsSuccessStatusCode && lastResult.StatusCode != 0)
                {
                    lastError = $"HTTP {lastResult.StatusCode}.";
                }
                else if (!TryExtractSelfTestXml(lastResult.Body, out var raw))
                {
                    lastError = "Response does not contain <selftest>...</selftest> or <settings>...</settings>.";
                }
                else if (!raw.Contains("default_mac", StringComparison.OrdinalIgnoreCase))
                {
                    lastError = "Selftest XML does not contain default_mac.";
                }
                else
                {
                    return new SelfTestFetchResult(
                        lastResult,
                        raw,
                        attempts,
                        stopwatch.Elapsed,
                        string.Empty);
                }

                _logger.Warning(
                    $"Selftest page is not ready yet (attempt {attempts}, url {url}): {lastError}");

                var delay = GetRetryDelay(timeout - stopwatch.Elapsed);
                if (delay <= TimeSpan.Zero)
                {
                    break;
                }

                _logger.Info($"Selftest next browser attempt in {(int)delay.TotalMilliseconds} ms.");
                await Task.Delay(delay, cancellationToken);
            }

            return new SelfTestFetchResult(
                lastResult,
                string.Empty,
                attempts,
                stopwatch.Elapsed,
                $"{lastError} Attempts: {attempts}, elapsed: {(int)stopwatch.Elapsed.TotalMilliseconds} ms.");
        }

        private static bool IsDeviceSelfTestUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            return uri.AbsolutePath.EndsWith("/test.shtml", StringComparison.OrdinalIgnoreCase) ||
                   uri.AbsolutePath.Contains("/cgi-bin/luci", StringComparison.OrdinalIgnoreCase) ||
                   uri.AbsolutePath.Contains("deviceinfo", StringComparison.OrdinalIgnoreCase);
        }

        private static TimeSpan GetAttemptTimeout(TimeSpan remaining)
        {
            var remainingMs = Math.Max(1, (int)remaining.TotalMilliseconds);
            return TimeSpan.FromMilliseconds(Math.Min(remainingMs, MaxPageAttemptTimeoutMs));
        }

        private TimeSpan GetRetryDelay(TimeSpan remaining)
        {
            var remainingMs = (int)remaining.TotalMilliseconds;
            if (remainingMs <= 0)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromMilliseconds(Math.Min(_pollIntervalMs, remainingMs));
        }

        private async Task<HttpRequestResult> GetPageAsync(
            string url,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (!_useBrowser)
            {
                return await _httpRequestService.GetAsync(url, timeout, cancellationToken);
            }

            var browserPath = FindBrowserExecutable();
            if (string.IsNullOrWhiteSpace(browserPath))
            {
                return HttpRequestResult.Failure(
                    "Headless Chrome/Edge was not found.",
                    TimeSpan.Zero);
            }

            _logger.Info(
                $"[INFO] Selftest opens one headless browser, waits for page load and then waits {BrowserDomSettleDelayMs} ms before one PageSource snapshot.");

            return await GetPageWithBrowserAsync(
                browserPath,
                url,
                timeout,
                cancellationToken);
        }

        private static async Task<HttpRequestResult> GetPageWithBrowserAsync(
            string browserPath,
            string url,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var userDataDir = Path.Combine(Path.GetTempPath(), "TestBuilderHeadlessChrome_" + Guid.NewGuid().ToString("N"));
            var debuggingPort = GetAvailableTcpPort();
            Process? process = null;

            try
            {
                Directory.CreateDirectory(userDataDir);

                process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = browserPath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                process.StartInfo.ArgumentList.Add("--headless");
                process.StartInfo.ArgumentList.Add("--disable-gpu");
                process.StartInfo.ArgumentList.Add("--no-sandbox");
                process.StartInfo.ArgumentList.Add("--disable-dev-shm-usage");
                process.StartInfo.ArgumentList.Add("--window-size=1920,1080");
                process.StartInfo.ArgumentList.Add("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                process.StartInfo.ArgumentList.Add("--remote-debugging-port=" + debuggingPort);
                process.StartInfo.ArgumentList.Add("--remote-allow-origins=*");
                process.StartInfo.ArgumentList.Add("--user-data-dir=" + userDataDir);
                process.StartInfo.ArgumentList.Add(url);

                process.Start();

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);

                var webSocketUrl = await WaitForPageWebSocketUrlAsync(
                    debuggingPort,
                    GetRemainingTimeout(timeout, stopwatch.Elapsed),
                    timeoutCts.Token);

                var pageSource = await ReadPageSourceLikeLegacyHelperAsync(
                    webSocketUrl,
                    timeoutCts.Token);
                return HttpRequestResult.Success(0, pageSource, stopwatch.Elapsed);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return HttpRequestResult.Failure(
                    $"Headless browser timeout: {(int)timeout.TotalMilliseconds} ms.",
                    stopwatch.Elapsed);
            }
            catch (TimeoutException ex)
            {
                return HttpRequestResult.Failure(ex.Message, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                return HttpRequestResult.Failure(ex.Message, stopwatch.Elapsed);
            }
            finally
            {
                // Chrome is only a transient DOM reader for this one attempt.
                // Keep the extracted XML in TestContext, not in browser state or files.
                if (process != null)
                {
                    TryKill(process);
                    process.Dispose();
                }

                TryDeleteDirectory(userDataDir);
            }
        }

        private static TimeSpan GetRemainingTimeout(TimeSpan timeout, TimeSpan elapsed)
        {
            var remaining = timeout - elapsed;
            return remaining <= TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(1)
                : remaining;
        }

        private static int GetAvailableTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static async Task<string> WaitForPageWebSocketUrlAsync(
            int port,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromMilliseconds(1000)
            };

            while (stopwatch.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var json = await client.GetStringAsync(
                        $"http://127.0.0.1:{port}/json",
                        cancellationToken);

                    if (TryGetPageWebSocketUrl(json, out var webSocketUrl))
                    {
                        return webSocketUrl;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                }

                await Task.Delay(100, cancellationToken);
            }

            throw new TimeoutException("Headless browser DevTools endpoint was not ready.");
        }

        private static bool TryGetPageWebSocketUrl(string json, out string webSocketUrl)
        {
            webSocketUrl = string.Empty;

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var target in document.RootElement.EnumerateArray())
            {
                var type = target.TryGetProperty("type", out var typeProperty)
                    ? typeProperty.GetString()
                    : string.Empty;

                if (!string.Equals(type, "page", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (target.TryGetProperty("webSocketDebuggerUrl", out var urlProperty))
                {
                    webSocketUrl = urlProperty.GetString() ?? string.Empty;
                    return !string.IsNullOrWhiteSpace(webSocketUrl);
                }
            }

            return false;
        }

        private static async Task<string> ReadPageSourceLikeLegacyHelperAsync(
            string webSocketUrl,
            CancellationToken cancellationToken)
        {
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri(webSocketUrl), cancellationToken);

            var commandId = 1;
            while (true)
            {
                try
                {
                    var readyState = await EvaluateStringAsync(
                        socket,
                        commandId++,
                        "document.readyState",
                        cancellationToken);

                    if (string.Equals(readyState, "complete", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }
                catch (InvalidOperationException)
                {
                    // Selenium's GoToUrl also waits through redirects/navigation.
                    // A CDP execution context can disappear briefly during that transition.
                }

                await Task.Delay(100, cancellationToken);
            }

            // Match the legacy helper: Selenium GoToUrl waits for page load,
            // then the program sleeps for a full ten seconds before PageSource.
            await Task.Delay(BrowserDomSettleDelayMs, cancellationToken);

            return await EvaluateStringAsync(
                socket,
                commandId,
                "document.documentElement.outerHTML",
                cancellationToken);
        }

        private static async Task<string> EvaluateStringAsync(
            ClientWebSocket socket,
            int commandId,
            string expression,
            CancellationToken cancellationToken)
        {
            await SendDevToolsCommandAsync(
                socket,
                commandId,
                "Runtime.evaluate",
                new Dictionary<string, object>
                {
                    ["expression"] = expression,
                    ["returnByValue"] = true
                },
                cancellationToken);

            var response = await ReceiveDevToolsResponseAsync(socket, commandId, cancellationToken);
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException(error.ToString());
            }

            if (!root.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("result", out var runtimeResult) ||
                !runtimeResult.TryGetProperty("value", out var value))
            {
                throw new InvalidOperationException("Headless browser did not return page source.");
            }

            return value.GetString() ?? string.Empty;
        }

        private static async Task SendDevToolsCommandAsync(
            ClientWebSocket socket,
            int commandId,
            string method,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["id"] = commandId,
                ["method"] = method,
                ["params"] = parameters
            });
            var bytes = Encoding.UTF8.GetBytes(payload);

            await socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);
        }

        private static async Task<string> ReceiveDevToolsResponseAsync(
            ClientWebSocket socket,
            int commandId,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                var message = await ReceiveWebSocketMessageAsync(socket, cancellationToken);
                using var document = JsonDocument.Parse(message);

                if (document.RootElement.TryGetProperty("id", out var id) &&
                    id.TryGetInt32(out var value) &&
                    value == commandId)
                {
                    return message;
                }
            }
        }

        private static async Task<string> ReceiveWebSocketMessageAsync(
            ClientWebSocket socket,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            using var stream = new MemoryStream();

            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new WebSocketException("Headless browser closed DevTools connection.");
                }

                stream.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static string? FindBrowserExecutable()
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe")
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

            foreach (var candidate in BuildExtractionCandidates(pageSource))
            {
                if (TryExtractXmlElement(candidate, "selftest", out xml) ||
                    TryExtractXmlElement(candidate, "settings", out xml))
                {
                    xml = RepairLegacySelfTestXml(xml.Trim());
                    return true;
                }
            }

            return false;
        }

        private static bool TryExtractXmlElement(
            string source,
            string rootName,
            out string xml)
        {
            xml = string.Empty;

            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            var rootPattern = Regex.Escape(rootName);
            var match = Regex.Match(
                source,
                $@"<\s*{rootPattern}\b[^>]*>.*?</\s*{rootPattern}\s*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

            if (!match.Success)
            {
                return false;
            }

            xml = match.Value;
            return true;
        }

        private static IEnumerable<string> BuildExtractionCandidates(string pageSource)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();

            AddCandidate(pageSource);

            var count = 0;
            while (queue.Count > 0 && count < MaxExtractionCandidates)
            {
                var current = queue.Dequeue();
                count++;
                yield return current;

                AddCandidate(DecodeHtmlRepeated(current));
                AddCandidate(TryUriDecode(current));
                AddCandidate(TryRegexUnescape(current));
                AddCandidate(current.Replace(@"\/", "/", StringComparison.Ordinal));
            }

            void AddCandidate(string? value)
            {
                if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
                {
                    return;
                }

                queue.Enqueue(value);
            }
        }

        private static string DecodeHtmlRepeated(string value)
        {
            var current = value;

            for (var i = 0; i < 3; i++)
            {
                var decoded = WebUtility.HtmlDecode(current);
                if (string.Equals(decoded, current, StringComparison.Ordinal))
                {
                    break;
                }

                current = decoded;
            }

            return current;
        }

        private static string? TryUriDecode(string value)
        {
            try
            {
                return Uri.UnescapeDataString(value);
            }
            catch (UriFormatException)
            {
                return null;
            }
        }

        private static string? TryRegexUnescape(string value)
        {
            try
            {
                return Regex.Unescape(value);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static bool IsSupportedSelfTestRoot(string? rootName)
        {
            return string.Equals(rootName, "selftest", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(rootName, "settings", StringComparison.OrdinalIgnoreCase);
        }

        private static string RepairLegacySelfTestXml(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return xml;
            }

            return Regex.Replace(
                xml,
                @"(<adc_2_5>[^<]*)<adc_2_5>",
                "$1</adc_2_5>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static Dictionary<string, string> ExtractValues(XDocument document)
        {
            return document
                .Descendants()
                .Where(x => !x.HasElements)
                .GroupBy(x => x.Name.LocalName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => SelectSelfTestValue(document.Root, group).Trim(),
                    StringComparer.OrdinalIgnoreCase);
        }

        private static string SelectSelfTestValue(XElement? root, IEnumerable<XElement> elements)
        {
            var lastRootField = elements.LastOrDefault(element => element.Parent == root);
            return (lastRootField ?? elements.Last()).Value;
        }

        private static IEnumerable<string> GetLegacyAliases(string fieldName)
        {
            var link = Regex.Match(fieldName, @"^link_(\d+)$", RegexOptions.IgnoreCase);
            if (link.Success && int.TryParse(link.Groups[1].Value, out var linkNumber))
            {
                yield return $"link[{Math.Max(0, linkNumber - 1)}]";
            }

            foreach (var alias in GetIndexedAliases(fieldName, @"^poe_([ab])_(\d+)_(state|st|v|c)$"))
            {
                yield return alias;
            }

            foreach (var alias in GetIndexedAliases(fieldName, @"^poe_([ab])_(state|st|v|c)_(\d+)$", swapped: true))
            {
                yield return alias;
            }

            var sfp = Regex.Match(fieldName, @"^sfp_(\d+)_(pres|sd|id)$", RegexOptions.IgnoreCase);
            if (sfp.Success && int.TryParse(sfp.Groups[1].Value, out var sfpIndex))
            {
                yield return $"sfp_{sfp.Groups[2].Value.ToLowerInvariant()}[{sfpIndex}]";
            }

            var sfpSwapped = Regex.Match(fieldName, @"^sfp_(pres|sd|id)_(\d+)$", RegexOptions.IgnoreCase);
            if (sfpSwapped.Success && int.TryParse(sfpSwapped.Groups[2].Value, out var swappedSfpIndex))
            {
                yield return $"sfp_{sfpSwapped.Groups[1].Value.ToLowerInvariant()}[{swappedSfpIndex}]";
            }
        }

        private static IEnumerable<string> GetIndexedAliases(
            string fieldName,
            string pattern,
            bool swapped = false)
        {
            var match = Regex.Match(fieldName, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                yield break;
            }

            var side = match.Groups[1].Value.ToLowerInvariant();
            var numberText = swapped ? match.Groups[3].Value : match.Groups[2].Value;
            var kind = swapped ? match.Groups[2].Value : match.Groups[3].Value;

            if (!int.TryParse(numberText, out var number))
            {
                yield break;
            }

            kind = kind.Equals("state", StringComparison.OrdinalIgnoreCase)
                ? "st"
                : kind.ToLowerInvariant();

            yield return $"poe_{side}_{kind}[{Math.Max(0, number - 1)}]";
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

        private sealed record SelfTestFetchResult(
            HttpRequestResult Result,
            string RawXml,
            int Attempts,
            TimeSpan Elapsed,
            string ErrorMessage);

        private sealed record ValidationRule(string FieldName, double Min, double Max)
        {
            public string FormatRange()
            {
                return $"{Min.ToString(CultureInfo.InvariantCulture)}..{Max.ToString(CultureInfo.InvariantCulture)}";
            }
        }

        private sealed record ValidationCheckResult(
            ValidationRule Rule,
            string ActualValue,
            bool Passed,
            string Error)
        {
            public static ValidationCheckResult Pass(
                ValidationRule rule,
                double actual)
            {
                return new ValidationCheckResult(
                    rule,
                    actual.ToString(CultureInfo.InvariantCulture),
                    true,
                    string.Empty);
            }

            public static ValidationCheckResult Fail(
                ValidationRule rule,
                string actualValue,
                string error)
            {
                return new ValidationCheckResult(
                    rule,
                    actualValue,
                    false,
                    error);
            }
        }
    }
}
