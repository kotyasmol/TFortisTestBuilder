using System;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    /// <summary>
    /// Step HTTP_REQUEST.
    /// Loads DUT selftest page through a headless browser, extracts XML and writes selftest.txt.
    /// </summary>
    public sealed class HttpRequestStep : ITestStep
    {
        public const string DefaultUrl =
            "http://192.168.0.1/cgi-bin/luci/admin/statistics/deviceinfo?luci_username=admin&luci_password=admin";

        public const int DefaultTimeoutMs = 10000;
        public const string DefaultOutputVariableName = RequestTestPageStep.DefaultOutputVariableName;

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
            _url = string.IsNullOrWhiteSpace(url) ? DefaultUrl : url.Trim();
            _timeoutMs = timeoutMs <= 0 ? DefaultTimeoutMs : timeoutMs;
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

            var (baseUrl, path) = SplitUrl(_url);

            var step = new RequestTestPageStep(
                _httpRequestService,
                _logger,
                baseUrl,
                path,
                _timeoutMs,
                retryCount: 0,
                retryDelayMs: 0,
                outputVariableName: _outputVariableName,
                failOnError: true,
                requireSuccessStatusCode: _requireSuccessStatusCode,
                expectedContentContains: RequestTestPageStep.DefaultExpectedContentContains,
                saveStatusCodeTo: RequestTestPageStep.DefaultStatusCodeVariableName,
                saveErrorTo: RequestTestPageStep.DefaultErrorVariableName,
                saveElapsedMsTo: RequestTestPageStep.DefaultElapsedMsVariableName,
                useBrowser: true);

            return await step.ExecuteAsync(context, cancellationToken);
        }

        private static (string BaseUrl, string Path) SplitUrl(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                var baseUrl = uri.GetLeftPart(UriPartial.Authority);
                var path = string.IsNullOrWhiteSpace(uri.PathAndQuery)
                    ? RequestTestPageStep.DefaultPath
                    : uri.PathAndQuery;

                return (baseUrl, path);
            }

            return (RequestTestPageStep.DefaultBaseUrl, RequestTestPageStep.DefaultPath);
        }
    }
}
