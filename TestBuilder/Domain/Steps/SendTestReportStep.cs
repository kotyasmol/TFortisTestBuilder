using System;
using System.IO;
using System.Net.Http;
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
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serverBaseUrl = serverBaseUrl?.Trim() ?? string.Empty;
            _reportVariableName = string.IsNullOrWhiteSpace(reportVariableName) ? "TestReportJson" : reportVariableName.Trim();
            _endpoint = string.IsNullOrWhiteSpace(endpoint) ? "/api/Api.svc/result.json" : endpoint.Trim();
            _timeoutMs = Math.Max(1, timeoutMs);
            _retryCount = Math.Max(0, retryCount);
            _retryDelayMs = Math.Max(0, retryDelayMs);
            _saveLocalCopy = saveLocalCopy;
            _localReportsDirectory = string.IsNullOrWhiteSpace(localReportsDirectory) ? "reports" : localReportsDirectory.Trim();
            _failOnError = failOnError;
        }

        public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            if (!context.Variables.TryGetValue(_reportVariableName, out var reportValue) ||
                reportValue == null ||
                string.IsNullOrWhiteSpace(reportValue.ToString()))
            {
                return Fail(context, "JSON отчёта не найден в контексте.", string.Empty, 0, string.Empty);
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
                    using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(_timeoutMs) };
                    using var form = new MultipartFormDataContent();
                    using var content = new StringContent(report, Encoding.UTF8, "application/json");
                    form.Add(content, "file", "result.json");

                    using var response = await client.PostAsync(url, form, cancellationToken);
                    raw = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();

                    if (response.IsSuccessStatusCode && raw.StartsWith("Ok", StringComparison.OrdinalIgnoreCase))
                    {
                        context.SetVariable("SendReport.Success", true);
                        context.SetVariable("SendReport.RawResponse", raw);
                        context.SetVariable("SendReport.Attempts", attempt);
                        context.SetVariable("SendReport.Url", url);
                        context.SetVariable("SendReport.LocalPath", localPath);
                        context.SetVariable("SendReport.Error", string.Empty);
                        _logger.Info($"[OK] Отчёт отправлен: {url}");
                        return StepResult.True;
                    }

                    lastError = $"HTTP {(int)response.StatusCode}, response '{raw}'.";
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
            context.SetVariable("SendReport.Error", error);
            _logger.Warning($"[ОШИБКА] Отчёт не отправлен: {error}");
            return _failOnError ? StepResult.False : StepResult.True;
        }

        private async Task<string> SaveLocalCopyAsync(string report, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(_localReportsDirectory);
            var path = Path.Combine(_localReportsDirectory, $"result-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
            await File.WriteAllTextAsync(path, report, Encoding.UTF8, cancellationToken);
            return Path.GetFullPath(path);
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
