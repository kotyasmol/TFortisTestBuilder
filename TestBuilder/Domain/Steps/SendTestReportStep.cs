using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public sealed class SendTestReportStep : ITestStep
    {
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
                () => new HttpClient())
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
            Func<HttpClient> httpClientFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serverBaseUrl = serverBaseUrl?.Trim() ?? string.Empty;
            _reportVariableName = string.IsNullOrWhiteSpace(reportVariableName) ? "TestReportText" : reportVariableName.Trim();
            _endpoint = string.IsNullOrWhiteSpace(endpoint) ? "/api/Api.svc/result.json" : endpoint.Trim();
            _timeoutMs = Math.Max(1, timeoutMs);
            _retryCount = Math.Max(0, retryCount);
            _retryDelayMs = Math.Max(0, retryDelayMs);
            _saveLocalCopy = saveLocalCopy;
            _localReportsDirectory = string.IsNullOrWhiteSpace(localReportsDirectory) ? "reports" : localReportsDirectory.Trim();
            _failOnError = failOnError;
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        }

        public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            context.SetVariable("SendReport.Success", false);
            context.SetVariable("SendReport.RawResponse", string.Empty);
            context.SetVariable("SendReport.Attempts", 0);
            context.SetVariable("SendReport.Url", string.Empty);
            context.SetVariable("SendReport.LocalPath", string.Empty);
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
            string url;

            try
            {
                url = BuildUrl();
            }
            catch (Exception ex)
            {
                return Fail(context, ex.Message, string.Empty, 0, string.Empty);
            }

            var localPath = _saveLocalCopy ? await SaveLocalCopyAsync(report, cancellationToken) : string.Empty;
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
                        context.SetVariable("SendReport.Success", true);
                        context.SetVariable("SendReport.RawResponse", raw);
                        context.SetVariable("SendReport.Attempts", attempt);
                        context.SetVariable("SendReport.Url", url);
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
            var path = Path.Combine(_localReportsDirectory, $"result-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
            await File.WriteAllTextAsync(path, report, Encoding.UTF8, cancellationToken);
            return Path.GetFullPath(path);
        }

        private static MultipartFormDataContent CreateLegacyMultipart(string report)
        {
            var form = new MultipartFormDataContent(
                "---------------------------723690991551375881941828858");

            form.Add(new ByteArrayContent(Array.Empty<byte>()), "action");

            var reportContent = new ByteArrayContent(Encoding.UTF8.GetBytes(report));
            reportContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(reportContent, "updatefile", "result.json");

            var resultContent = new ByteArrayContent(Array.Empty<byte>());
            form.Add(resultContent, "result", "result.json");

            return form;
        }

        private string BuildUrl()
        {
            var serverBaseUrl = ServerBaseUrlResolver.NormalizeForHttp(_serverBaseUrl);
            if (string.IsNullOrWhiteSpace(serverBaseUrl))
            {
                throw new InvalidOperationException(
                    "ServerBaseUrl не задан. Укажи адрес сервера отчетов в ноде или во вкладке Настройки.");
            }

            return serverBaseUrl.TrimEnd('/') + "/" + _endpoint.TrimStart('/');
        }

        private string SafeBuildUrl()
        {
            try
            {
                return BuildUrl();
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
