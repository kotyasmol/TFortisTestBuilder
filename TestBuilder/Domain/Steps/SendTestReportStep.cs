using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public sealed class SendTestReportStep : ITestStep
    {
        private const string DefaultEndpoint = "/api/Api.svc/result.json";
        private static readonly Regex SessionLine = new(@"^session=[^\r\n]*",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        private readonly ILogger _logger;
        private readonly string _serverBaseUrl;
        private readonly string _reportVariableName;
        private readonly string _endpoint;
        private readonly int _timeoutMs;
        private readonly int _retryCount;
        private readonly int _retryDelayMs;
        private readonly bool _saveLocalCopy;
        private readonly string _localReportsDirectory;
        private readonly bool _failOnError;
        private readonly Func<HttpClient> _httpClientFactory;
        private readonly string? _trustedServerBaseUrl;

        public SendTestReportStep(
            ILogger logger,
            string serverBaseUrl,
            string reportVariableName,
            string endpoint,
            int timeoutMs,
            int retryCount,
            int retryDelayMs,
            bool saveLocalCopy,
            string localReportsDirectory,
            bool failOnError)
            : this(
                logger,
                serverBaseUrl,
                reportVariableName,
                endpoint,
                timeoutMs,
                retryCount,
                retryDelayMs,
                saveLocalCopy,
                localReportsDirectory,
                failOnError,
                () => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }))
        {
        }

        internal SendTestReportStep(
            ILogger logger,
            string serverBaseUrl,
            string reportVariableName,
            string endpoint,
            int timeoutMs,
            int retryCount,
            int retryDelayMs,
            bool saveLocalCopy,
            string localReportsDirectory,
            bool failOnError,
            Func<HttpClient> httpClientFactory,
            string? trustedServerBaseUrl = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serverBaseUrl = serverBaseUrl?.Trim() ?? string.Empty;
            _reportVariableName = string.IsNullOrWhiteSpace(reportVariableName) ? "TestReportText" : reportVariableName.Trim();
            _endpoint = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint.Trim();
            _timeoutMs = Math.Max(1, timeoutMs);
            _retryCount = Math.Max(0, retryCount);
            _retryDelayMs = Math.Max(0, retryDelayMs);
            _saveLocalCopy = saveLocalCopy;
            _localReportsDirectory = string.IsNullOrWhiteSpace(localReportsDirectory) ? "reports" : localReportsDirectory.Trim();
            _failOnError = failOnError;
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _trustedServerBaseUrl = trustedServerBaseUrl;
        }

        public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            context.SetVariable("ReportDelivery.Status", "Sending");
            context.SetVariable("ReportDelivery.Message", "Отправка отчёта не завершена.");
            context.SetVariable("SendReport.Success", false);
            context.SetVariable("SendReport.RawResponse", string.Empty);
            context.SetVariable("SendReport.Attempts", 0);
            context.SetVariable("SendReport.Url", string.Empty);
            context.SetVariable("SendReport.LocalPath", string.Empty);
            context.SetVariable("SendReport.LocalCopyError", string.Empty);
            context.SetVariable("SendReport.StatusCode", 0);
            context.SetVariable("SendReport.Format", "QTstand legacy multipart");
            context.SetVariable("SendReport.Error", string.Empty);

            if (!context.Variables.TryGetValue(_reportVariableName, out var reportValue) ||
                reportValue == null ||
                string.IsNullOrWhiteSpace(reportValue.ToString()))
            {
                return Fail(context, "Текст отчёта не найден в контексте.", string.Empty, 0, string.Empty);
            }

            var report = reportValue.ToString() ?? string.Empty;
            if (context.ProductionModel?.SerialSeries?.CheckReport(report, context.IssuedSerialNumber) is { } serialError)
                return Fail(context, serialError, string.Empty, 0, string.Empty);
            var localPath = string.Empty;
            if (_saveLocalCopy)
            {
                try
                {
                    localPath = await SaveLocalCopyAsync(report, cancellationToken);
                    context.SetVariable("SendReport.LocalPath", localPath);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    context.SetVariable("SendReport.LocalCopyError", ex.Message);
                    _logger.Warning($"Локальная копия отчёта не сохранена: {ex.Message}");
                }
            }

            Uri url;
            try
            {
                url = BuildUrl();
                if (!ServerBaseUrlResolver.IsSameHttpOrigin(url, _trustedServerBaseUrl ?? AppSettings.Instance.ServerBaseUrl) &&
                    ContainsLauncherSession(report))
                {
                    return Fail(context, "Отчёт с сессией лаунчера можно отправить только на сервер из настроек приложения.",
                        string.Empty, 0, localPath);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Fail(context, ex.Message, string.Empty, 0, localPath);
            }
            var attempts = _retryCount + 1;
            string raw = string.Empty;
            string lastError = string.Empty;

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    using var client = _httpClientFactory();
                    client.Timeout = TimeSpan.FromMilliseconds(_timeoutMs);
                    using var form = CreateLegacyMultipart(report);

                    using var response = await client.PostAsync(url, form, cancellationToken);
                    raw = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();

                    if (response.IsSuccessStatusCode && raw.StartsWith("Ok", StringComparison.Ordinal))
                    {
                        context.SetVariable("ReportDelivery.Status", "Sent");
                        context.SetVariable("ReportDelivery.Message", _saveLocalCopy && localPath.Length == 0
                            ? "Отчёт принят сервером. Локальная копия не сохранена."
                            : "Отчёт принят сервером.");
                        context.SetVariable("SendReport.Success", true);
                        context.SetVariable("SendReport.RawResponse", raw);
                        context.SetVariable("SendReport.Attempts", attempt);
                        context.SetVariable("SendReport.Url", url.AbsoluteUri);
                        context.SetVariable("SendReport.LocalPath", localPath);
                        context.SetVariable("SendReport.StatusCode", (int)response.StatusCode);
                        context.SetVariable("SendReport.Format", "QTstand legacy multipart");
                        context.SetVariable("SendReport.Error", string.Empty);
                        _logger.Info($"[OK] Отчёт отправлен: {url}");
                        return StepResult.True;
                    }

                    lastError = $"HTTP {(int)response.StatusCode}, response '{raw}'.";
                    context.SetVariable("SendReport.StatusCode", (int)response.StatusCode);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    lastError = $"Таймаут отправки отчёта: {_timeoutMs} мс.";
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastError = ex.Message;
                }

                if (attempt < attempts && _retryDelayMs > 0)
                {
                    _logger.Warning($"Отчёт не отправлен: {lastError}. Повтор через {_retryDelayMs} мс.");
                    await Task.Delay(_retryDelayMs, cancellationToken);
                }
            }

            return Fail(context, lastError, raw, attempts, localPath);
        }

        private StepResult Fail(
            TestContext context,
            string error,
            string raw,
            int attempts,
            string localPath)
        {
            context.SetVariable("ReportDelivery.Status", "Failed");
            context.SetVariable("ReportDelivery.Message", $"Отчёт НЕ отправлен: {error}" +
                (string.IsNullOrWhiteSpace(localPath) ? " Локальная копия не сохранена." : $" Копия: {localPath}"));
            context.SetVariable("SendReport.Success", false);
            context.SetVariable("SendReport.RawResponse", raw);
            context.SetVariable("SendReport.Attempts", attempts);
            context.SetVariable("SendReport.Url", SafeBuildUrl());
            context.SetVariable("SendReport.LocalPath", localPath);
            context.SetVariable("SendReport.Format", "QTstand legacy multipart");
            context.SetVariable("SendReport.Error", error);
            _logger.Warning($"[ОШИБКА] Отчёт не отправлен: {error}");
            return _failOnError ? StepResult.False : StepResult.True;
        }

        private async Task<string> SaveLocalCopyAsync(string report, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(_localReportsDirectory);
            var path = Path.Combine(_localReportsDirectory, $"result-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(path, RedactLocalReport(report), Encoding.UTF8, cancellationToken);
            return Path.GetFullPath(path);
        }

        private static bool ContainsLauncherSession(string report) => SessionLine.IsMatch(report) ||
            (App.StartupSessionId.Length > 0 && report.Contains(App.StartupSessionId, StringComparison.Ordinal));

        internal static string RedactLocalReport(string report)
        {
            var redacted = SessionLine.Replace(report, "session=true=<redacted>");
            return App.StartupSessionId.Length > 0
                ? redacted.Replace(App.StartupSessionId, "<redacted>", StringComparison.Ordinal)
                : redacted;
        }

        private static HttpContent CreateLegacyMultipart(string report)
        {
            const string boundary = "---------------------------723690991551375881941828858";

            // Reproduce the byte layout from QTstand_old exactly, including the
            // duplicated boundary before the empty result part and the missing
            // conventional closing "--" suffix.
            var body =
                $"--{boundary}\r\n" +
                "Content-Disposition: form-data; name=\"action\"\r\n\r\n" +
                "\r\n" +
                $"--{boundary}\r\n" +
                "Content-Disposition: form-data; name=\"updatefile\"; filename=\"result.json\"\r\n" +
                "Content-Type: application/octet-stream;\r\n\r\n" +
                report +
                "\r\n" +
                $"--{boundary}\r\n" +
                $"--{boundary}\r\n" +
                "Content-Disposition: form-data; name=\"result\"; filename=\"result.json\"\r\n" +
                "\r\n";

            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            content.Headers.TryAddWithoutValidation(
                "Content-Type",
                $"multipart/form-data; boundary={boundary}");
            return content;
        }

        private Uri BuildUrl()
        {
            if (!Uri.TryCreate(ServerBaseUrlResolver.NormalizeForHttp(_serverBaseUrl), UriKind.Absolute, out var server) ||
                server.Scheme is not ("http" or "https") || server.UserInfo.Length != 0)
            {
                throw new InvalidOperationException(
                    "Укажите корректный HTTP(S)-адрес сервера отчётов в ноде или в настройках.");
            }

            var path = server.GetLeftPart(UriPartial.Path).TrimEnd('/');
            var endpoint = _endpoint;
            if (string.Equals(endpoint.TrimStart('/'), DefaultEndpoint.TrimStart('/'), StringComparison.OrdinalIgnoreCase))
            {
                var basePath = server.AbsolutePath.TrimEnd('/');
                if (basePath.EndsWith("/api.svc", StringComparison.OrdinalIgnoreCase))
                    endpoint = "result.json";
                else if (basePath.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
                    endpoint = "Api.svc/result.json";
            }

            return new Uri(path + "/" + endpoint.TrimStart('/'));
        }

        private string SafeBuildUrl()
        {
            try
            {
                return BuildUrl().AbsoluteUri;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
