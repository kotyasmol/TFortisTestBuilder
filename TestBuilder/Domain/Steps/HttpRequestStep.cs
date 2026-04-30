using System;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    /// <summary>
    /// Шаг HTTP_REQUEST.
    /// Выполняет GET-запрос к тестируемому устройству и сохраняет тело ответа в переменную контекста.
    /// </summary>
    public sealed class HttpRequestStep : ITestStep
    {
        public const string DefaultOutputVariableName = "testPageHtml";

        private readonly IHttpRequestService _httpRequestService;
        private readonly ILogger _logger;
        private readonly string _url;
        private readonly int _timeoutMs;
        private readonly string _outputVariableName;
        private readonly bool _requireSuccessStatusCode;

        public HttpRequestStep(
            IHttpRequestService httpRequestService,
            ILogger logger,
            string url,
            int timeoutMs,
            string outputVariableName,
            bool requireSuccessStatusCode)
        {
            _httpRequestService = httpRequestService ?? throw new ArgumentNullException(nameof(httpRequestService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _url = url ?? string.Empty;
            _timeoutMs = timeoutMs;
            _outputVariableName = string.IsNullOrWhiteSpace(outputVariableName)
                ? DefaultOutputVariableName
                : outputVariableName.Trim();
            _requireSuccessStatusCode = requireSuccessStatusCode;
        }

        public async Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var timeout = TimeSpan.FromMilliseconds(Math.Max(1, _timeoutMs));

            _logger.Info($"[ШАГ] HTTP запрос → {_url}, таймаут {timeout.TotalMilliseconds:0} мс.");

            var result = await _httpRequestService.GetAsync(
                _url,
                timeout,
                cancellationToken);

            context.SetVariable(_outputVariableName, result.Body);
            context.SetVariable($"{_outputVariableName}.statusCode", result.StatusCode ?? 0);
            context.SetVariable($"{_outputVariableName}.isSuccess", result.IsSuccessStatusCode);
            context.SetVariable($"{_outputVariableName}.error", result.ErrorMessage);
            context.SetVariable($"{_outputVariableName}.elapsedMs", (int)result.Elapsed.TotalMilliseconds);

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                _logger.Warning($"[ОШИБКА] HTTP запрос не выполнен: {result.ErrorMessage}");
                return StepResult.False;
            }

            if (_requireSuccessStatusCode && !result.IsSuccessStatusCode)
            {
                _logger.Warning(
                    $"HTTP_REQUEST вернул HTTP {(result.StatusCode?.ToString() ?? "unknown")}. " +
                    $"Ответ сохранен в переменную '{_outputVariableName}'.");

                return StepResult.False;
            }

            _logger.Info(
                $"HTTP_REQUEST OK: HTTP {result.StatusCode}, " +
                $"{result.Body.Length} символов, " +
                $"{result.Elapsed.TotalMilliseconds:0} мс. " +
                $"Ответ сохранен в '{_outputVariableName}'.");

            return StepResult.True;
        }
    }
}