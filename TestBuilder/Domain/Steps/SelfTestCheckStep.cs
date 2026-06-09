using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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

        public const int DefaultTimeoutMs = 10000;
        public const string DefaultOutputPrefix = "Dut";
        public const string DefaultOutputVariableName = "SelfTestRaw";
        public const string DefaultOutputFileName = "selftest.txt";
        public const string DefaultValidationRules =
            "init_ok=1..1\n" +
            "dev_type=0..65535\n" +
            "firmvare_vers=0..65535\n" +
            "boot_vers=0..65535";

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
            _timeoutMs = timeoutMs <= 0 ? DefaultTimeoutMs : timeoutMs;
            _outputPrefix = string.IsNullOrWhiteSpace(outputPrefix)
                ? DefaultOutputPrefix
                : outputPrefix.Trim().TrimEnd('.');
            _validationRules = string.IsNullOrWhiteSpace(validationRules)
                ? DefaultValidationRules
                : validationRules;
            _failOnError = failOnError;
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

            _logger.Info($"[STEP] Selftest request: {_url}, timeout {_timeoutMs} ms.");

            var result = await GetPageAsync(
                _url,
                TimeSpan.FromMilliseconds(Math.Max(1, _timeoutMs)),
                cancellationToken);

            context.SetVariable("SelfTest.Url", _url);
            context.SetVariable("SelfTest.StatusCode", result.StatusCode ?? 0);
            context.SetVariable("SelfTest.ElapsedMs", (int)result.Elapsed.TotalMilliseconds);

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                SaveSelfTestFile("invalid testpage");
                return Fail(context, result.ErrorMessage);
            }

            if (!result.IsSuccessStatusCode && result.StatusCode != 0)
            {
                SaveSelfTestFile("invalid testpage");
                return Fail(context, $"HTTP {result.StatusCode}.");
            }

            if (!TryExtractSelfTestXml(result.Body, out var raw))
            {
                SaveSelfTestFile("invalid testpage");
                return Fail(context, "Response does not contain <selftest>...</selftest>.");
            }

            if (!raw.Contains("default_mac", StringComparison.Ordinal))
            {
                SaveSelfTestFile("invalid testpage");
                return Fail(context, "Selftest XML does not contain default_mac.");
            }

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

            if (!string.Equals(document.Root?.Name.LocalName, "selftest", StringComparison.OrdinalIgnoreCase))
            {
                error = "XML root is not <selftest>.";
                return false;
            }

            return true;
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

                _logger.Warning("Headless Chrome/Edge was not found. Falling back to plain HTTP.");
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
                        $"Headless browser timeout: {(int)timeout.TotalMilliseconds} ms.",
                        stopwatch.Elapsed);
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

        private sealed record ValidationRule(string FieldName, double Min, double Max);
    }
}
