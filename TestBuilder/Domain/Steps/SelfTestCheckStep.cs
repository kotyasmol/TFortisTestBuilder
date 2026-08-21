using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
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

        public const int DefaultTimeoutMs = 160000;
        public const int DefaultPollIntervalMs = 5000;
        public const string DefaultOutputPrefix = "Dut";
        public const string DefaultOutputVariableName = "SelfTestRaw";
        public const string DefaultValidationRules =
            "init_ok=1..1\n" +
            "dev_type=0..65535\n" +
            "firmvare_vers=0..65535\n" +
            "boot_vers=0..65535";

        private const int MaxPageAttemptTimeoutMs = 20000;
        private const int MinimumPollIntervalMs = 100;
        private const int LegacyShortTimeoutMs = 30000;
        private const int MinimumDeviceReadyTimeoutMs = 160000;
        private const int BrowserDomSettleDelayMs = 10000;
        private const int BrowserLateLoginSettleDelayMs = 3000;
        private const int MaxBrowserProcessTimeoutMs = 15000;
        private const int MinHttpFallbackTimeoutMs = 3000;
        private const int BrowserCleanupTimeoutMs = 2000;
        private const int MaxLoggedBrowserErrorLength = 300;
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
            int pollIntervalMs = DefaultPollIntervalMs)
        {
            _httpRequestService = httpRequestService ?? throw new ArgumentNullException(nameof(httpRequestService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _url = string.IsNullOrWhiteSpace(url) ? DefaultUrl : url.Trim();
            _timeoutMs = NormalizeSelfTestTimeout(_url, timeoutMs);
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

            _logger.Info(
                $"[STEP] Selftest request: {SanitizeUrlForLog(_url)}, " +
                $"timeout {_timeoutMs} ms, poll every {_pollIntervalMs} ms.");
            SaveNetworkDiagnostics(context);

            var fetch = await WaitForSelfTestXmlAsync(
                _url,
                TimeSpan.FromMilliseconds(Math.Max(1, _timeoutMs)),
                context,
                cancellationToken);
            var result = fetch.Result;

            context.SetVariable("SelfTest.Url", _url);
            context.SetVariable("SelfTest.StatusCode", result.StatusCode ?? 0);
            context.SetVariable("SelfTest.ElapsedMs", (int)fetch.Elapsed.TotalMilliseconds);
            context.SetVariable("SelfTest.Attempts", fetch.Attempts);
            context.SetVariable("SelfTest.PollIntervalMs", _pollIntervalMs);

            if (!string.IsNullOrWhiteSpace(fetch.ErrorMessage))
            {
                context.SetVariable(DefaultOutputVariableName, string.Empty);
                return Fail(context, fetch.ErrorMessage);
            }

            var raw = fetch.RawXml;

            context.SetVariable(DefaultOutputVariableName, raw);

            if (!TryParseSelfTest(raw, out var document, out var parseError))
            {
                return Fail(context, parseError);
            }

            var values = ExtractValues(document);
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

        private void SaveNetworkDiagnostics(TestContext context)
        {
            context.SetVariable("SelfTest.DirectConnection", false);
            context.SetVariable("SelfTest.RouteSourceAddress", string.Empty);
            context.SetVariable("SelfTest.RouteDestinationAddress", string.Empty);
            context.SetVariable("SelfTest.RouteError", string.Empty);

            if (!Uri.TryCreate(_url, UriKind.Absolute, out var uri))
            {
                return;
            }

            var directConnection = HttpRequestService.ShouldBypassProxy(uri);
            context.SetVariable("SelfTest.DirectConnection", directConnection);

            if (directConnection)
            {
                _logger.Info("[INFO] Selftest local DUT request: system proxy is disabled.");
            }

            if (!IPAddress.TryParse(uri.Host.Trim('[', ']'), out var destinationAddress))
            {
                return;
            }

            context.SetVariable("SelfTest.RouteDestinationAddress", destinationAddress.ToString());

            if (TryGetRouteSourceAddress(uri, destinationAddress, out var sourceAddress, out var routeError))
            {
                context.SetVariable("SelfTest.RouteSourceAddress", sourceAddress.ToString());
                _logger.Info(
                    $"[INFO] Selftest route selected by OS: {sourceAddress} -> {destinationAddress}:{uri.Port}.");
            }
            else
            {
                context.SetVariable("SelfTest.RouteError", routeError);
                _logger.Warning($"[WARN] Selftest route diagnostics failed: {routeError}");
            }
        }

        private bool IsDirectDutRouteReady(
            string url,
            TestContext context,
            out string error)
        {
            error = string.Empty;

            if (!_useBrowser || !OperatingSystem.IsWindows() ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                !HttpRequestService.ShouldBypassProxy(uri) ||
                !IPAddress.TryParse(uri.Host.Trim('[', ']'), out var destinationAddress) ||
                destinationAddress.AddressFamily != AddressFamily.InterNetwork)
            {
                return true;
            }

            var candidateAddresses = GetDirectLocalAddresses(destinationAddress);
            context.SetVariable(
                "SelfTest.RouteCandidateAddresses",
                string.Join(", ", candidateAddresses.Select(address => address.ToString())));

            // Private networks routed through a gateway are valid too. Enforce the
            // direct route only when Windows actually has local addresses in the
            // DUT subnet (the production stand has 192.168.0.2 and 192.168.0.3).
            if (candidateAddresses.Count == 0)
            {
                return true;
            }

            context.SetVariable("SelfTest.RouteDestinationAddress", destinationAddress.ToString());

            if (!TryGetRouteSourceAddress(uri, destinationAddress, out var sourceAddress, out var routeError))
            {
                context.SetVariable("SelfTest.RouteError", routeError);
                error = $"Не удалось определить маршрут к DUT {destinationAddress}: {routeError}";
                return false;
            }

            context.SetVariable("SelfTest.RouteSourceAddress", sourceAddress.ToString());

            if (candidateAddresses.Contains(sourceAddress))
            {
                context.SetVariable("SelfTest.RouteError", string.Empty);
                return true;
            }

            error =
                $"Маршрут к DUT ещё не готов: Windows выбрала {sourceAddress}, " +
                $"ожидалась одна из локальных карт {string.Join(", ", candidateAddresses)}.";
            context.SetVariable("SelfTest.RouteError", error);
            return false;
        }

        private static IReadOnlyList<IPAddress> GetDirectLocalAddresses(IPAddress destinationAddress)
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
                    .Where(address =>
                        address.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !address.Address.Equals(destinationAddress) &&
                        IsSameIpv4Subnet(
                            address.Address,
                            destinationAddress,
                            address.PrefixLength))
                    .Select(address => address.Address)
                    .Distinct()
                    .ToList();
            }
            catch (NetworkInformationException)
            {
                return Array.Empty<IPAddress>();
            }
        }

        internal static bool IsSameIpv4Subnet(
            IPAddress first,
            IPAddress second,
            int prefixLength)
        {
            if (first.AddressFamily != AddressFamily.InterNetwork ||
                second.AddressFamily != AddressFamily.InterNetwork ||
                prefixLength is < 0 or > 32)
            {
                return false;
            }

            var firstBytes = first.GetAddressBytes();
            var secondBytes = second.GetAddressBytes();
            var wholeBytes = prefixLength / 8;
            var remainingBits = prefixLength % 8;

            for (var index = 0; index < wholeBytes; index++)
            {
                if (firstBytes[index] != secondBytes[index])
                {
                    return false;
                }
            }

            if (remainingBits == 0)
            {
                return true;
            }

            var mask = (byte)(0xFF << (8 - remainingBits));
            return (firstBytes[wholeBytes] & mask) == (secondBytes[wholeBytes] & mask);
        }

        private static bool TryGetRouteSourceAddress(
            Uri uri,
            IPAddress destinationAddress,
            out IPAddress sourceAddress,
            out string error)
        {
            sourceAddress = IPAddress.None;
            error = string.Empty;

            try
            {
                using var socket = new Socket(
                    destinationAddress.AddressFamily,
                    SocketType.Dgram,
                    ProtocolType.Udp);
                socket.Connect(new IPEndPoint(destinationAddress, uri.Port));

                if (socket.LocalEndPoint is not IPEndPoint localEndPoint)
                {
                    error = "ОС не вернула локальный endpoint.";
                    return false;
                }

                sourceAddress = localEndPoint.Address;
                return true;
            }
            catch (Exception ex) when (ex is SocketException or InvalidOperationException)
            {
                error = ex.Message;
                return false;
            }
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
            TestContext context,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var attempts = 0;
            var lastError = "Selftest page was not requested.";
            string? lastRouteError = null;
            var candidateUrls = BuildSelfTestUrlCandidates(url);
            HttpRequestResult lastResult = HttpRequestResult.Failure(lastError, TimeSpan.Zero);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var remaining = timeout - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                if (!IsDirectDutRouteReady(url, context, out var routeError))
                {
                    lastError = routeError;
                    if (!string.Equals(lastRouteError, routeError, StringComparison.Ordinal))
                    {
                        _logger.Warning($"[WARN] {routeError}");
                        lastRouteError = routeError;
                    }

                    var routeDelay = GetRetryDelay(remaining);
                    if (routeDelay <= TimeSpan.Zero)
                    {
                        break;
                    }

                    _logger.Info($"Selftest waits for DUT route; next check in {(int)routeDelay.TotalMilliseconds} ms.");
                    await Task.Delay(routeDelay, cancellationToken);
                    continue;
                }

                if (lastRouteError != null)
                {
                    _logger.Info(
                        $"[OK] Selftest direct DUT route is ready: " +
                        $"{context.GetVariable<string>("SelfTest.RouteSourceAddress")} -> " +
                        $"{context.GetVariable<string>("SelfTest.RouteDestinationAddress")}.");
                    lastRouteError = null;
                }

                foreach (var candidateUrl in candidateUrls)
                {
                    remaining = timeout - stopwatch.Elapsed;
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }

                    attempts++;
                    var attemptTimeout = GetAttemptTimeout(remaining);
                    lastResult = await GetPageAsync(candidateUrl, attemptTimeout, cancellationToken);

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
                        return new SelfTestFetchResult(lastResult, raw, attempts, stopwatch.Elapsed, string.Empty);
                    }

                    _logger.Warning(
                        $"Selftest page is not ready yet (attempt {attempts}, " +
                        $"url {SanitizeUrlForLog(candidateUrl)}): {lastError}");
                }

                var delay = GetRetryDelay(timeout - stopwatch.Elapsed);
                if (delay <= TimeSpan.Zero)
                {
                    break;
                }

                _logger.Info($"Selftest next poll in {(int)delay.TotalMilliseconds} ms.");
                await Task.Delay(delay, cancellationToken);
            }

            var totalError = attempts == 0
                ? $"{lastError} Elapsed: {(int)stopwatch.Elapsed.TotalMilliseconds} ms."
                : $"{lastError} Attempts: {attempts}, elapsed: {(int)stopwatch.Elapsed.TotalMilliseconds} ms.";

            return new SelfTestFetchResult(lastResult, string.Empty, attempts, stopwatch.Elapsed, totalError);
        }

        private static IReadOnlyList<string> BuildSelfTestUrlCandidates(string url)
        {
            var urls = new List<string> { url };

            if (TryBuildLegacyTestPageUrl(url, out var legacyUrl) &&
                !urls.Contains(legacyUrl, StringComparer.OrdinalIgnoreCase))
            {
                urls.Add(legacyUrl);
            }

            return urls;
        }

        private static bool TryBuildLegacyTestPageUrl(string url, out string legacyUrl)
        {
            legacyUrl = string.Empty;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (!uri.AbsolutePath.Contains("/cgi-bin/luci", StringComparison.OrdinalIgnoreCase) &&
                !uri.AbsolutePath.Contains("deviceinfo", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var builder = new UriBuilder(uri)
            {
                Path = "test.shtml",
                Query = string.Empty,
                Fragment = string.Empty
            };

            legacyUrl = builder.Uri.ToString();
            return true;
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
            var attemptMs = remainingMs < 1000
                ? remainingMs
                : Math.Min(remainingMs, MaxPageAttemptTimeoutMs);

            return TimeSpan.FromMilliseconds(attemptMs);
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
            if (_useBrowser && ShouldUseBrowserForUrl(url))
            {
                var browserPath = FindBrowserExecutable();

                if (!string.IsNullOrWhiteSpace(browserPath))
                {
                    var browserTimeout = GetBrowserAttemptTimeout(timeout);
                    if (browserTimeout <= TimeSpan.Zero)
                    {
                        return await _httpRequestService.GetAsync(url, timeout, cancellationToken);
                    }

                    var browserResult = await GetPageWithBrowserAsync(
                        browserPath,
                        url,
                        browserTimeout,
                        cancellationToken);

                    if (string.IsNullOrWhiteSpace(browserResult.ErrorMessage))
                    {
                        if (TryExtractSelfTestXml(browserResult.Body, out _))
                        {
                            return browserResult;
                        }

                        _logger.Warning("Headless browser returned DOM without selftest XML. Falling back to plain HTTP.");
                    }
                    else if (TryExtractSelfTestXml(browserResult.Body, out _))
                    {
                        return HttpRequestResult.Success(0, browserResult.Body, browserResult.Elapsed);
                    }
                    else
                    {
                        _logger.Warning(
                            $"Headless browser failed: {TrimForLog(browserResult.ErrorMessage)}. Falling back to plain HTTP.");
                    }

                    var fallbackTimeout = timeout - browserResult.Elapsed;
                    if (fallbackTimeout <= TimeSpan.Zero)
                    {
                        return browserResult;
                    }

                    var httpResult = await _httpRequestService.GetAsync(url, fallbackTimeout, cancellationToken);
                    return string.IsNullOrWhiteSpace(httpResult.ErrorMessage)
                        ? httpResult
                        : browserResult;
                }

                _logger.Warning("Headless Chrome/Edge was not found. Falling back to plain HTTP.");
            }

            return await _httpRequestService.GetAsync(url, timeout, cancellationToken);
        }

        private static TimeSpan GetBrowserAttemptTimeout(TimeSpan attemptTimeout)
        {
            var attemptMs = Math.Max(1, (int)attemptTimeout.TotalMilliseconds);
            if (attemptMs <= MinHttpFallbackTimeoutMs)
            {
                return TimeSpan.Zero;
            }

            var browserMs = Math.Min(MaxBrowserProcessTimeoutMs, attemptMs - MinHttpFallbackTimeoutMs);
            return TimeSpan.FromMilliseconds(Math.Max(1, browserMs));
        }

        private static bool ShouldUseBrowserForUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return true;
            }

            return !uri.AbsolutePath.EndsWith("/test.shtml", StringComparison.OrdinalIgnoreCase);
        }

        private static string TrimForLog(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var singleLine = Regex.Replace(value.Trim(), @"\s+", " ");
            return singleLine.Length <= MaxLoggedBrowserErrorLength
                ? singleLine
                : singleLine.Substring(0, MaxLoggedBrowserErrorLength) + "...";
        }

        private async Task<HttpRequestResult> GetPageWithBrowserAsync(
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

                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                    HttpRequestService.ShouldBypassProxy(uri))
                {
                    process.StartInfo.ArgumentList.Add("--no-proxy-server");
                }

                process.StartInfo.ArgumentList.Add(url);

                process.Start();

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);

                var webSocketUrl = await WaitForPageWebSocketUrlAsync(
                    debuggingPort,
                    GetRemainingTimeout(timeout, stopwatch.Elapsed),
                    timeoutCts.Token).ConfigureAwait(false);

                var pageSource = await ReadPageSourceWithDevToolsAsync(
                    webSocketUrl,
                    url,
                    timeoutCts.Token).ConfigureAwait(false);
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
                await CleanupBrowserResourcesAsync(process, userDataDir).ConfigureAwait(false);
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

        private async Task<string> ReadPageSourceWithDevToolsAsync(
            string webSocketUrl,
            string pageUrl,
            CancellationToken cancellationToken)
        {
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri(webSocketUrl), cancellationToken).ConfigureAwait(false);

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);

            var commandId = 1;
            var pageSource = await ReadOuterHtmlAsync(
                socket,
                commandId++,
                cancellationToken).ConfigureAwait(false);

            if (TryGetLuciCredentials(pageUrl, out var username, out var password) &&
                LooksLikeLuciLoginPage(pageSource))
            {
                return await SubmitLuciLoginAndReadPageAsync(
                    socket,
                    pageSource,
                    username,
                    password,
                    commandId,
                    BrowserDomSettleDelayMs,
                    cancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(BrowserDomSettleDelayMs),
                cancellationToken).ConfigureAwait(false);

            pageSource = await ReadOuterHtmlAsync(
                socket,
                commandId++,
                cancellationToken).ConfigureAwait(false);

            if (TryGetLuciCredentials(pageUrl, out username, out password) &&
                LooksLikeLuciLoginPage(pageSource))
            {
                return await SubmitLuciLoginAndReadPageAsync(
                    socket,
                    pageSource,
                    username,
                    password,
                    commandId,
                    BrowserLateLoginSettleDelayMs,
                    cancellationToken).ConfigureAwait(false);
            }

            return pageSource;
        }

        private async Task<string> SubmitLuciLoginAndReadPageAsync(
            ClientWebSocket socket,
            string loginPageSource,
            string username,
            string password,
            int commandId,
            int settleDelayMs,
            CancellationToken cancellationToken)
        {
            _logger.Info("[INFO] LuCI login page detected; submitting credentials from Selftest URL.");

            if (!await SubmitLuciLoginFormAsync(
                    socket,
                    commandId++,
                    username,
                    password,
                    cancellationToken).ConfigureAwait(false))
            {
                _logger.Warning("[WARN] LuCI login form was detected but could not be submitted.");
                return loginPageSource;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(settleDelayMs),
                cancellationToken).ConfigureAwait(false);

            var authenticatedPageSource = await ReadOuterHtmlAsync(
                socket,
                commandId,
                cancellationToken).ConfigureAwait(false);

            if (LooksLikeLuciLoginPage(authenticatedPageSource))
            {
                _logger.Warning(
                    "[WARN] LuCI authentication returned the login page again. Check username/password and letter case.");
            }

            return authenticatedPageSource;
        }

        private static async Task<bool> SubmitLuciLoginFormAsync(
            ClientWebSocket socket,
            int commandId,
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            var usernameLiteral = JsonSerializer.Serialize(username);
            var passwordLiteral = JsonSerializer.Serialize(password);
            var expression =
                "(() => {" +
                "const userInput = document.querySelector('input[name=\"luci_username\"]');" +
                "const passwordInput = document.querySelector('input[name=\"luci_password\"]');" +
                "const form = userInput?.form || passwordInput?.form || document.querySelector('form');" +
                "if (!userInput || !passwordInput || !form) return false;" +
                $"userInput.value = {usernameLiteral};" +
                $"passwordInput.value = {passwordLiteral};" +
                "setTimeout(() => form.submit(), 0);" +
                "return true;" +
                "})()";

            await SendDevToolsCommandAsync(
                socket,
                commandId,
                "Runtime.evaluate",
                new Dictionary<string, object>
                {
                    ["expression"] = expression,
                    ["returnByValue"] = true
                },
                cancellationToken).ConfigureAwait(false);

            var response = await ReceiveDevToolsResponseAsync(
                socket,
                commandId,
                cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(response);

            return document.RootElement.TryGetProperty("result", out var result) &&
                   result.TryGetProperty("result", out var runtimeResult) &&
                   runtimeResult.TryGetProperty("value", out var value) &&
                   value.ValueKind == JsonValueKind.True;
        }

        private static async Task<string> ReadOuterHtmlAsync(
            ClientWebSocket socket,
            int commandId,
            CancellationToken cancellationToken)
        {
            await SendDevToolsCommandAsync(
                socket,
                commandId,
                "Runtime.evaluate",
                new Dictionary<string, object>
                {
                    ["expression"] = "document.documentElement.outerHTML",
                    ["returnByValue"] = true
                },
                cancellationToken).ConfigureAwait(false);

            var response = await ReceiveDevToolsResponseAsync(
                socket,
                commandId,
                cancellationToken).ConfigureAwait(false);
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

        internal static bool LooksLikeLuciLoginPage(string pageSource)
        {
            if (string.IsNullOrWhiteSpace(pageSource))
            {
                return false;
            }

            return Regex.IsMatch(
                       pageSource,
                       "name\\s*=\\s*['\"]luci_username['\"]",
                       RegexOptions.IgnoreCase) &&
                   Regex.IsMatch(
                       pageSource,
                       "name\\s*=\\s*['\"]luci_password['\"]",
                       RegexOptions.IgnoreCase);
        }

        internal static bool TryGetLuciCredentials(
            string url,
            out string username,
            out string password)
        {
            username = string.Empty;
            password = string.Empty;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            string? foundUsername = null;
            string? foundPassword = null;

            foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = part.IndexOf('=');
                var rawName = separator >= 0 ? part[..separator] : part;
                var rawValue = separator >= 0 ? part[(separator + 1)..] : string.Empty;
                var name = DecodeQueryValue(rawName);
                var value = DecodeQueryValue(rawValue);

                if (string.Equals(name, "luci_username", StringComparison.OrdinalIgnoreCase))
                {
                    foundUsername = value;
                }
                else if (string.Equals(name, "luci_password", StringComparison.OrdinalIgnoreCase))
                {
                    foundPassword = value;
                }
            }

            if (foundUsername == null || foundPassword == null)
            {
                return false;
            }

            username = foundUsername;
            password = foundPassword;
            return true;
        }

        private static string DecodeQueryValue(string value) =>
            Uri.UnescapeDataString(value.Replace('+', ' '));

        internal static string SanitizeUrlForLog(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            return Regex.Replace(
                url,
                @"([?&](?:luci_username|luci_password)=)[^&#]*",
                "$1***",
                RegexOptions.IgnoreCase);
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

        private static async Task CleanupBrowserResourcesAsync(
            Process? process,
            string userDataDir)
        {
            var cleanupTask = Task.Run(() =>
            {
                if (process != null)
                {
                    TryKill(process);

                    try
                    {
                        process.Dispose();
                    }
                    catch
                    {
                    }
                }

                TryDeleteDirectory(userDataDir);
            });

            try
            {
                await cleanupTask
                    .WaitAsync(TimeSpan.FromMilliseconds(BrowserCleanupTimeoutMs))
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Do not block the test runner or Avalonia UI while Windows/Chrome
                // finishes terminating the transient browser process tree.
            }
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
