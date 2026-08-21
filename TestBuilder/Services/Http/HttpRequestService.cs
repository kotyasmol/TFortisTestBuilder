using System;
using System.Diagnostics;
using System.Net;
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
        private readonly HttpClient? _directHttpClient;
        private readonly bool _disposeHttpClient;
        private readonly bool _disposeDirectHttpClient;

        public HttpRequestService()
            : this(
                new HttpClient(),
                new HttpClient(new SocketsHttpHandler
                {
                    UseProxy = false
                }),
                disposeHttpClient: true,
                disposeDirectHttpClient: true)
        {
        }

        public HttpRequestService(HttpClient httpClient, bool disposeClient = false)
            : this(
                httpClient,
                directHttpClient: null,
                disposeHttpClient: disposeClient,
                disposeDirectHttpClient: false)
        {
        }

        internal HttpRequestService(
            HttpClient httpClient,
            HttpClient directHttpClient,
            bool disposeClients)
            : this(
                httpClient,
                directHttpClient,
                disposeHttpClient: disposeClients,
                disposeDirectHttpClient: disposeClients)
        {
        }

        private HttpRequestService(
            HttpClient httpClient,
            HttpClient? directHttpClient,
            bool disposeHttpClient,
            bool disposeDirectHttpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _directHttpClient = directHttpClient;
            _disposeHttpClient = disposeHttpClient;
            _disposeDirectHttpClient = disposeDirectHttpClient;
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
                var client = ShouldBypassProxy(uri)
                    ? _directHttpClient ?? _httpClient
                    : _httpClient;

                using var response = await client.SendAsync(
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

        internal static bool ShouldBypassProxy(Uri uri)
        {
            if (uri == null)
            {
                throw new ArgumentNullException(nameof(uri));
            }

            var host = uri.Host.Trim('[', ']');

            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!IPAddress.TryParse(host, out var address))
            {
                return false;
            }

            if (IPAddress.IsLoopback(address))
            {
                return true;
            }

            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            var bytes = address.GetAddressBytes();
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return bytes[0] == 10 ||
                       (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                       (bytes[0] == 192 && bytes[1] == 168) ||
                       (bytes[0] == 169 && bytes[1] == 254);
            }

            return address.IsIPv6LinkLocal ||
                   (bytes.Length == 16 && (bytes[0] & 0xFE) == 0xFC);
        }

        public void Dispose()
        {
            if (_disposeHttpClient)
            {
                _httpClient.Dispose();
            }

            if (_disposeDirectHttpClient &&
                _directHttpClient != null &&
                !ReferenceEquals(_directHttpClient, _httpClient))
            {
                _directHttpClient.Dispose();
            }
        }
    }
}
