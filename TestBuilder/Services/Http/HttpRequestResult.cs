using System;

namespace TestBuilder.Services.Http
{
    /// <summary>
    /// Результат выполнения HTTP-запроса.
    /// </summary>
    public sealed class HttpRequestResult
    {
        public bool IsSuccessStatusCode { get; init; }

        public int? StatusCode { get; init; }

        public string Body { get; init; } = string.Empty;

        public string ErrorMessage { get; init; } = string.Empty;

        public TimeSpan Elapsed { get; init; }

        public static HttpRequestResult Success(int statusCode, string body, TimeSpan elapsed)
        {
            return new HttpRequestResult
            {
                IsSuccessStatusCode = statusCode >= 200 && statusCode <= 299,
                StatusCode = statusCode,
                Body = body ?? string.Empty,
                ErrorMessage = string.Empty,
                Elapsed = elapsed
            };
        }

        public static HttpRequestResult Failure(string errorMessage, TimeSpan elapsed, int? statusCode = null, string body = "")
        {
            return new HttpRequestResult
            {
                IsSuccessStatusCode = false,
                StatusCode = statusCode,
                Body = body ?? string.Empty,
                ErrorMessage = errorMessage ?? string.Empty,
                Elapsed = elapsed
            };
        }
    }
}
