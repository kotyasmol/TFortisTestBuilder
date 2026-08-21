using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Services.Http
{
    /// <summary>
    /// Реализация HTTP-клиента для тестовых шагов.
    /// </summary>
    public sealed class HttpRequestService : IHttpRequestService, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly bool _disposeClient;

        public HttpRequestService()
            : this(new HttpClient(), disposeClient: true)
        {
        }

        public HttpRequestService(HttpClient httpClient, bool disposeClient = false)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _disposeClient = disposeClient;
        }

        public async Task<HttpRequestResult> GetAsync(
            string url,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();

            if (string.IsNullOrWhiteSpace(url))
            {
                return HttpRequestResult.Failure(
                    "URL не задан.",
                    stopwatch.Elapsed);
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return HttpRequestResult.Failure(
                    $"Некорректный HTTP URL: {url}",
                    stopwatch.Elapsed);
            }

            var safeTimeout = timeout <= TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(1)
                : timeout;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(safeTimeout);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseContentRead,
                    timeoutCts.Token);

                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                return HttpRequestResult.Success(
                    (int)response.StatusCode,
                    body,
                    stopwatch.Elapsed);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return HttpRequestResult.Failure(
                    $"Таймаут HTTP-запроса: {safeTimeout.TotalMilliseconds:0} мс.",
                    stopwatch.Elapsed);
            }
            catch (HttpRequestException ex)
            {
                return HttpRequestResult.Failure(
                    $"Ошибка HTTP-запроса: {ex.Message}",
                    stopwatch.Elapsed);
            }
            catch (InvalidOperationException ex)
            {
                return HttpRequestResult.Failure(
                    $"Ошибка HTTP-запроса: {ex.Message}",
                    stopwatch.Elapsed);
            }
        }

        public void Dispose()
        {
            if (_disposeClient)
            {
                _httpClient.Dispose();
            }
        }
    }
}
