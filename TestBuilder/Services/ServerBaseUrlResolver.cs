using System;

namespace TestBuilder.Services
{
    public static class ServerBaseUrlResolver
    {
        public static string ResolveFromSettings(string serverBaseUrl)
        {
            var trimmed = serverBaseUrl?.Trim() ?? string.Empty;
            return string.IsNullOrWhiteSpace(trimmed) || IsPlaceholder(trimmed)
                ? AppSettings.Instance.ServerBaseUrl.Trim()
                : trimmed;
        }

        public static string NormalizeForHttp(string serverBaseUrl)
        {
            var trimmed = serverBaseUrl?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed) || IsPlaceholder(trimmed))
            {
                return string.Empty;
            }

            return trimmed.Contains("://", StringComparison.Ordinal)
                ? trimmed
                : "http://" + trimmed;
        }

        public static bool IsPlaceholder(string serverBaseUrl)
        {
            var token = ExtractHostToken(serverBaseUrl);
            return string.Equals(token, "SERVER_BASE_URL", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(token, "server-address", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractHostToken(string serverBaseUrl)
        {
            var trimmed = serverBaseUrl?.Trim() ?? string.Empty;
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                return uri.Host.Trim();
            }

            var withoutScheme = trimmed;
            var schemeSeparator = withoutScheme.IndexOf("://", StringComparison.Ordinal);
            if (schemeSeparator >= 0)
            {
                withoutScheme = withoutScheme.Substring(schemeSeparator + 3);
            }

            return withoutScheme
                .Trim()
                .Trim('/')
                .Split('/', '?', '#')[0]
                .Trim();
        }
    }
}
