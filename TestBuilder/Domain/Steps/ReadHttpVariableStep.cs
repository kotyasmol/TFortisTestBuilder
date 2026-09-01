using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public enum HttpResponseValueType
    {
        Integer,
        Number,
        Boolean,
        String
    }

    /// <summary>
    /// Универсальное чтение одного значения через HTTP GET.
    /// </summary>
    public sealed class ReadHttpVariableStep : ITestStep
    {
        private readonly IHttpRequestService _httpRequestService;
        private readonly ILogger _logger;
        private readonly string _baseUrl;
        private readonly string _endpoint;
        private readonly HttpResponseValueType _responseType;
        private readonly int _timeoutMs;
        private readonly string _outputVariableName;
        private readonly bool _failOnError;

        public ReadHttpVariableStep(
            IHttpRequestService httpRequestService,
            ILogger logger,
            string baseUrl,
            string endpoint,
            HttpResponseValueType responseType,
            int timeoutMs,
            string outputVariableName,
            bool failOnError)
        {
            _httpRequestService = httpRequestService ?? throw new ArgumentNullException(nameof(httpRequestService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://192.168.0.1" : baseUrl.Trim();
            _endpoint = endpoint?.Trim() ?? string.Empty;
            _responseType = responseType;
            _timeoutMs = Math.Max(1, timeoutMs);
            _outputVariableName = string.IsNullOrWhiteSpace(outputVariableName)
                ? "Dut.value"
                : outputVariableName.Trim();
            _failOnError = failOnError;
        }

        public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            context.Variables.Remove(_outputVariableName);

            var read = await ReadAsync(
                _httpRequestService,
                _logger,
                _baseUrl,
                _endpoint,
                _responseType,
                _timeoutMs,
                cancellationToken);

            SaveDiagnostics(context, "HttpRead", read, _outputVariableName, _responseType);

            if (read.Success)
            {
                context.SetVariable(_outputVariableName, read.Value!);
                _logger.Info(
                    $"[OK] HTTP variable {_outputVariableName} = {ToInvariantString(read.Value)}, url {read.Url}.");
                return StepResult.True;
            }

            _logger.Warning($"[ОШИБКА] HTTP variable {_outputVariableName} не получена: {read.Error}");
            return _failOnError ? StepResult.False : StepResult.True;
        }

        internal static async Task<HttpVariableReadResult> ReadAsync(
            IHttpRequestService httpRequestService,
            ILogger logger,
            string baseUrl,
            string endpoint,
            HttpResponseValueType responseType,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            var url = BuildUrl(baseUrl, endpoint);
            if (string.IsNullOrWhiteSpace(url))
            {
                return HttpVariableReadResult.Failure(
                    string.Empty,
                    0,
                    string.Empty,
                    "Endpoint HTTP-запроса не задан.",
                    0);
            }

            logger.Info($"[HTTP] GET {url}, response={responseType}.");
            var result = await httpRequestService.GetAsync(
                url,
                TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs)),
                cancellationToken);
            var raw = result.Body.Trim().TrimStart('\uFEFF').Trim();
            var elapsedMs = (int)Math.Clamp(result.Elapsed.TotalMilliseconds, 0, int.MaxValue);

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                return HttpVariableReadResult.Failure(
                    url,
                    result.StatusCode ?? 0,
                    raw,
                    result.ErrorMessage,
                    elapsedMs);
            }

            if (!result.IsSuccessStatusCode)
            {
                var preview = CreateResponsePreview(raw);
                var detail = string.IsNullOrWhiteSpace(preview)
                    ? $"HTTP {result.StatusCode}, пустой ответ."
                    : $"HTTP {result.StatusCode}, ответ '{preview}'.";
                return HttpVariableReadResult.Failure(
                    url,
                    result.StatusCode ?? 0,
                    raw,
                    detail,
                    elapsedMs);
            }

            if (!TryParseValue(raw, responseType, out var value, out var parseError))
            {
                return HttpVariableReadResult.Failure(
                    url,
                    result.StatusCode ?? 0,
                    raw,
                    parseError,
                    elapsedMs);
            }

            return new HttpVariableReadResult(
                true,
                value,
                raw,
                string.Empty,
                url,
                result.StatusCode ?? 0,
                elapsedMs);
        }

        internal static void SaveDiagnostics(
            TestContext context,
            string prefix,
            HttpVariableReadResult read,
            string outputVariableName,
            HttpResponseValueType responseType)
        {
            context.SetVariable($"{prefix}.Url", read.Url);
            context.SetVariable($"{prefix}.StatusCode", read.StatusCode);
            context.SetVariable($"{prefix}.ElapsedMs", read.ElapsedMs);
            context.SetVariable($"{prefix}.RawResponse", read.RawResponse);
            context.SetVariable($"{prefix}.OutputVariable", outputVariableName);
            context.SetVariable($"{prefix}.ResponseType", responseType.ToString());
            context.SetVariable($"{prefix}.Success", read.Success);
            context.SetVariable($"{prefix}.Error", read.Error);
        }

        internal static string BuildUrl(string baseUrl, string endpoint)
        {
            var trimmedEndpoint = endpoint?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedEndpoint))
            {
                return string.Empty;
            }

            if (Uri.TryCreate(trimmedEndpoint, UriKind.Absolute, out var absoluteUri) &&
                absoluteUri.Scheme is "http" or "https")
            {
                return absoluteUri.ToString();
            }

            var normalizedBaseUrl = string.IsNullOrWhiteSpace(baseUrl)
                ? "http://192.168.0.1"
                : baseUrl.Trim();
            return normalizedBaseUrl.TrimEnd('/') + "/" + trimmedEndpoint.TrimStart('/');
        }

        internal static bool TryParseValue(
            string raw,
            HttpResponseValueType responseType,
            out object? value,
            out string error)
        {
            value = null;
            error = string.Empty;

            switch (responseType)
            {
                case HttpResponseValueType.Integer:
                    if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                    {
                        value = integer;
                        return true;
                    }
                    break;

                case HttpResponseValueType.Number:
                    if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    {
                        value = number;
                        return true;
                    }
                    break;

                case HttpResponseValueType.Boolean:
                    if (TryParseBoolean(raw, out var boolean))
                    {
                        value = boolean;
                        return true;
                    }
                    break;

                case HttpResponseValueType.String:
                    value = raw;
                    return true;
            }

            error = $"Ответ '{CreateResponsePreview(raw)}' не соответствует типу {responseType}.";
            return false;
        }

        private static bool TryParseBoolean(string raw, out bool value)
        {
            if (bool.TryParse(raw, out value))
            {
                return true;
            }

            switch (raw.Trim().ToLowerInvariant())
            {
                case "1":
                case "yes":
                case "on":
                    value = true;
                    return true;
                case "0":
                case "no":
                case "off":
                    value = false;
                    return true;
                default:
                    value = false;
                    return false;
            }
        }

        private static string CreateResponsePreview(string raw)
        {
            var normalized = Regex.Replace(raw ?? string.Empty, @"\s+", " ").Trim();
            return normalized.Length <= 240
                ? normalized
                : normalized[..240] + "…";
        }

        private static string ToInvariantString(object? value)
        {
            return value switch
            {
                null => string.Empty,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty
            };
        }

        internal sealed record HttpVariableReadResult(
            bool Success,
            object? Value,
            string RawResponse,
            string Error,
            string Url,
            int StatusCode,
            int ElapsedMs)
        {
            public static HttpVariableReadResult Failure(
                string url,
                int statusCode,
                string rawResponse,
                string error,
                int elapsedMs) =>
                new(false, null, rawResponse, error, url, statusCode, elapsedMs);
        }
    }
}
