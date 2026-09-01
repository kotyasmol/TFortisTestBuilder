using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public sealed class GetIrpStatusStep : ITestStep
    {
        private static readonly string[] EndpointPaths =
        {
            "/api/isUPS",
            "/api/isUps"
        };

        private readonly IHttpRequestService _httpRequestService;
        private readonly ILogger _logger;
        private readonly string _baseUrl;
        private readonly int _timeoutMs;
        private readonly string _outputVariableName;
        private readonly bool _failOnError;

        public GetIrpStatusStep(
            IHttpRequestService httpRequestService,
            ILogger logger,
            string baseUrl,
            int timeoutMs,
            string outputVariableName,
            bool failOnError)
        {
            _httpRequestService = httpRequestService ?? throw new ArgumentNullException(nameof(httpRequestService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://192.168.0.1" : baseUrl.Trim();
            _timeoutMs = Math.Max(1, timeoutMs);
            _outputVariableName = string.IsNullOrWhiteSpace(outputVariableName) ? "Dut.ups_det" : outputVariableName.Trim();
            _failOnError = failOnError;
        }

        public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            context.Variables.Remove(_outputVariableName);

            var read = await ReadStatusAsync(
                _httpRequestService,
                _logger,
                _baseUrl,
                _timeoutMs,
                cancellationToken);

            context.SetVariable("GetIrpStatus.Url", read.Url);
            context.SetVariable("GetIrpStatus.StatusCode", read.StatusCode);
            context.SetVariable("GetIrpStatus.Attempts", read.Attempts);
            context.SetVariable("GetIrpStatus.RawResponse", read.RawResponse);

            if (read.Success)
            {
                context.SetVariable(_outputVariableName, read.Value);
                context.SetVariable("GetIrpStatus.Success", true);
                context.SetVariable("GetIrpStatus.Error", string.Empty);
                _logger.Info($"[OK] IRP/UPS detect status: {read.Value}, url {read.Url}.");
                return StepResult.True;
            }

            context.SetVariable("GetIrpStatus.Success", false);
            context.SetVariable("GetIrpStatus.Error", read.Error);
            _logger.Warning($"[ОШИБКА] IRP status не получен: {read.Error}");
            return _failOnError ? StepResult.False : StepResult.True;
        }

        internal static async Task<IrpStatusReadResult> ReadStatusAsync(
            IHttpRequestService httpRequestService,
            ILogger logger,
            string baseUrl,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            var normalizedBaseUrl = string.IsNullOrWhiteSpace(baseUrl)
                ? "http://192.168.0.1"
                : baseUrl.Trim().TrimEnd('/');
            var timeout = TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs));
            var failures = new List<string>();
            var lastUrl = string.Empty;
            var lastRaw = string.Empty;
            var lastStatusCode = 0;

            for (var index = 0; index < EndpointPaths.Length; index++)
            {
                var endpoint = EndpointPaths[index];
                var url = normalizedBaseUrl + endpoint;
                var attempt = index + 1;

                logger.Info($"[HTTP] IRP status request {attempt}/{EndpointPaths.Length}: {url}");

                var result = await httpRequestService.GetAsync(url, timeout, cancellationToken);
                var raw = result.Body.Trim().TrimStart('\uFEFF').Trim();

                lastUrl = url;
                lastRaw = raw;
                lastStatusCode = result.StatusCode ?? 0;

                if (string.IsNullOrWhiteSpace(result.ErrorMessage) &&
                    result.IsSuccessStatusCode &&
                    int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
                    value is 0 or 1)
                {
                    return new IrpStatusReadResult(
                        true,
                        value,
                        raw,
                        string.Empty,
                        url,
                        lastStatusCode,
                        attempt);
                }

                failures.Add(DescribeFailure(endpoint, result, raw));

                if (index == 0 && ShouldTryLegacyEndpoint(result, raw))
                {
                    logger.Warning(
                        "[HTTP] /api/isUPS не найден; пробуем legacy-вариант /api/isUps.");
                    continue;
                }

                break;
            }

            return new IrpStatusReadResult(
                false,
                0,
                lastRaw,
                string.Join("; ", failures),
                lastUrl,
                lastStatusCode,
                failures.Count);
        }

        private static bool ShouldTryLegacyEndpoint(HttpRequestResult result, string raw)
        {
            return result.StatusCode == 404 ||
                   raw.Contains("not found", StringComparison.OrdinalIgnoreCase);
        }

        private static string DescribeFailure(
            string endpoint,
            HttpRequestResult result,
            string raw)
        {
            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                return $"{endpoint}: {result.ErrorMessage}";
            }

            var status = result.StatusCode.HasValue
                ? $"HTTP {result.StatusCode.Value}"
                : "HTTP status отсутствует";
            var preview = CreateResponsePreview(raw);

            return string.IsNullOrWhiteSpace(preview)
                ? $"{endpoint}: {status}, пустой ответ"
                : $"{endpoint}: {status}, ответ '{preview}'";
        }

        private static string CreateResponsePreview(string raw)
        {
            var normalized = Regex.Replace(raw ?? string.Empty, @"\s+", " ").Trim();
            return normalized.Length <= 240
                ? normalized
                : normalized[..240] + "…";
        }

        internal sealed record IrpStatusReadResult(
            bool Success,
            int Value,
            string RawResponse,
            string Error,
            string Url,
            int StatusCode,
            int Attempts);
    }
}
