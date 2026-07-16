using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
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
        public const string DefaultOutputPrefix = "Dut";
        public const string DefaultOutputVariableName = "SelfTestRaw";
        public const string DefaultOutputFileName = "selftest.txt";
        public const string DefaultValidationRules =
            "init_ok=1..1\n" +
            "dev_type=0..65535\n" +
            "firmvare_vers=0..65535\n" +
            "boot_vers=0..65535";

        private const int MaxPageAttemptTimeoutMs = 5000;
        private const int RetryDelayMs = 1000;
        private const int LegacyShortTimeoutMs = 30000;
        private const int MinimumDeviceReadyTimeoutMs = 160000;
        private const int MaxBrowserVirtualTimeBudgetMs = 5000;
        private const int MaxBrowserProcessTimeoutMs = 2500;
        private const int MinHttpFallbackTimeoutMs = 1000;
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

        public SelfTestCheckStep(
            IHttpRequestService httpRequestService,
            ILogger logger,
            string url,
            int timeoutMs,
            string outputPrefix,
            string validationRules,
            bool failOnError,
            bool useBrowser = true)
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
        }

        private static int NormalizeSelfTestTimeout(string url, int timeoutMs)
        {
            var normalized = timeoutMs <= 0 ? DefaultTimeoutMs : timeoutMs;
            return normalized <= LegacyShortTimeoutMs && IsDeviceSelfTestUrl(url)
                ? MinimumDeviceReadyTimeoutMs
                : normalized;
        }

        public async Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            _logger.Info($"[STEP] Selftest request: {_url}, timeout {_timeoutMs} ms.");

            var fetch = await WaitForSelfTestXmlAsync(
                _url,
                TimeSpan.FromMilliseconds(Math.Max(1, _timeoutMs)),
                cancellationToken);
            var result = fetch.Result;

            context.SetVariable("SelfTest.Url", _url);
            context.SetVariable("SelfTest.StatusCode", result.StatusCode ?? 0);
            context.SetVariable("SelfTest.ElapsedMs", (int)fetch.Elapsed.TotalMilliseconds);
            context.SetVariable("SelfTest.Attempts", fetch.Attempts);

            if (!string.IsNullOrWhiteSpace(fetch.ErrorMessage))
            {
                SaveSelfTestFile("invalid testpage");
                return Fail(context, fetch.ErrorMessage);
            }

            var raw = fetch.RawXml;

            SaveSelfTestFile(raw);
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

            var errors = Validate(values);
            if (errors.Count > 0)
            {
                return Fail(context, string.Join("; ", errors));
            }

            context.SetVariable("SelfTest.Ok", true);
            context.SetVariable("SelfTest.Error", string.Empty);
            _logger.Info($"[OK] Selftest passed: {values.Count} fields, {GetRules().Count()} rules.");
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

        private List<string> Validate(Dictionary<string, string> values)
        {
            var errors = new List<string>();

            foreach (var rule in GetRules())
            {
                if (!values.TryGetValue(rule.FieldName, out var rawValue))
                {
                    errors.Add($"{rule.FieldName}: field not found");
                    continue;
                }

                if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var actual) &&
                    !double.TryParse(rawValue, NumberStyles.Float, CultureInfo.CurrentCulture, out actual))
                {
                    errors.Add($"{rule.FieldName}: '{rawValue}' is not a number");
                    continue;
                }

                if (actual < rule.Min || actual > rule.Max)
                {
                    errors.Add(
                        $"{rule.FieldName}: {actual.ToString(CultureInfo.InvariantCulture)} outside " +
                        $"{rule.Min.ToString(CultureInfo.InvariantCulture)}..{rule.Max.ToString(CultureInfo.InvariantCulture)}");
                }
            }

            return errors;
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

                    _logger.Warning($"Selftest page is not ready yet (attempt {attempts}, url {candidateUrl}): {lastError}");
                }

                var delay = GetRetryDelay(timeout - stopwatch.Elapsed);
                if (delay <= TimeSpan.Zero)
                {
                    break;
                }

                await Task.Delay(delay, cancellationToken);
            }

            var totalError = attempts == 0
                ? $"Selftest timeout: {(int)timeout.TotalMilliseconds} ms."
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

        private static TimeSpan GetRetryDelay(TimeSpan remaining)
        {
            var remainingMs = (int)remaining.TotalMilliseconds;
            if (remainingMs <= 0)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromMilliseconds(Math.Min(RetryDelayMs, remainingMs));
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
                        return browserResult;
                    }

                    if (TryExtractSelfTestXml(browserResult.Body, out _))
                    {
                        return HttpRequestResult.Success(0, browserResult.Body, browserResult.Elapsed);
                    }

                    _logger.Warning(
                        $"Headless browser failed: {TrimForLog(browserResult.ErrorMessage)}. Falling back to plain HTTP.");
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
                process.StartInfo.ArgumentList.Add("--virtual-time-budget=" + GetBrowserVirtualTimeBudgetMs(timeout));
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
                    var timeoutStdout = await ReadCompletedOrEmptyAsync(stdoutTask);
                    var timeoutStderr = await ReadCompletedOrEmptyAsync(stderrTask);
                    return HttpRequestResult.Failure(
                        string.IsNullOrWhiteSpace(timeoutStderr)
                            ? $"Headless browser timeout: {(int)timeout.TotalMilliseconds} ms."
                            : $"Headless browser timeout: {(int)timeout.TotalMilliseconds} ms. {timeoutStderr.Trim()}",
                        stopwatch.Elapsed,
                        body: timeoutStdout);
                }

                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                if (process.ExitCode != 0)
                {
                    return HttpRequestResult.Failure(
                        string.IsNullOrWhiteSpace(stderr)
                            ? $"Headless browser exited with code {process.ExitCode}."
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

        private static int GetBrowserVirtualTimeBudgetMs(TimeSpan timeout)
        {
            var timeoutMs = Math.Max(1, (int)timeout.TotalMilliseconds);
            var budgetMs = Math.Max(1000, timeoutMs / 2);
            return Math.Min(budgetMs, MaxBrowserVirtualTimeBudgetMs);
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

        private static async Task<string> ReadCompletedOrEmptyAsync(Task<string> readTask)
        {
            try
            {
                return await readTask;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static Dictionary<string, string> ExtractValues(XDocument document)
        {
            return document
                .Descendants()
                .Where(x => !x.HasElements)
                .GroupBy(x => x.Name.LocalName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last().Value.Trim(),
                    StringComparer.OrdinalIgnoreCase);
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

        private sealed record SelfTestFetchResult(
            HttpRequestResult Result,
            string RawXml,
            int Attempts,
            TimeSpan Elapsed,
            string ErrorMessage);

        private sealed record ValidationRule(string FieldName, double Min, double Max);
    }
}
