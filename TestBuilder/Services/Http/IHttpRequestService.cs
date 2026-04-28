using System;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Services.Http
{
    /// <summary>
    /// Сервис выполнения HTTP-запросов к тестируемому устройству.
    /// </summary>
    public interface IHttpRequestService
    {
        Task<HttpRequestResult> GetAsync(
            string url,
            TimeSpan timeout,
            CancellationToken cancellationToken);
    }
}
