using System;
using System.Net.Http;

namespace TestBuilder.Services.Http;

public static class LauncherServerClient
{
    public static HttpRequestService Create(string sessionId, string serverBaseUrl) =>
        new(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }), true,
            request => ApplyHeaders(request, sessionId, serverBaseUrl));

    internal static void ApplyHeaders(HttpRequestMessage request, string sessionId, string serverBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        var validated = LauncherOptions.Parse(new[] { sessionId });
        if (!validated.IsValid) return;
        var normalized = ServerBaseUrlResolver.NormalizeForHttp(serverBaseUrl);
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var server) || request.RequestUri is not { } target)
            return;
        // Never send launcher credentials to the DUT or to an alternative server from a profile.
        if (!string.Equals(target.GetLeftPart(UriPartial.Authority), server.GetLeftPart(UriPartial.Authority),
                StringComparison.OrdinalIgnoreCase) ||
            !target.AbsolutePath.TrimEnd('/').EndsWith("/getSerialNum", StringComparison.OrdinalIgnoreCase)) return;
        request.Headers.TryAddWithoutValidation("User-Agent", "ftstand");
        request.Headers.TryAddWithoutValidation("Cookie", $"SGUID=session_id={validated.SessionId}&login=");
    }
}
