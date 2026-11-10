using System;
using System.Net.Http;

namespace TestBuilder.Services.Http;

public static class LauncherServerClient
{
    public static HttpRequestService Create(string sessionId, string serverBaseUrl) =>
        Create(sessionId, () => serverBaseUrl);

    internal static HttpRequestService Create(string sessionId, Func<string> serverBaseUrl) =>
        new(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }), true,
            request => ApplyHeaders(request, sessionId, serverBaseUrl()));

    internal static void ApplyHeaders(HttpRequestMessage request, string sessionId, string serverBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        var validated = LauncherOptions.Parse(new[] { sessionId });
        if (!validated.IsValid) return;
        if (request.RequestUri is not { IsAbsoluteUri: true } target)
            return;
        // Never send launcher credentials to the DUT or to an alternative server from a profile.
        if (!ServerBaseUrlResolver.IsSameHttpOrigin(target, serverBaseUrl) ||
            !(target.AbsolutePath.TrimEnd('/').EndsWith("/getSerialNum", StringComparison.OrdinalIgnoreCase) ||
              target.AbsolutePath.TrimEnd('/').EndsWith("/getdevicehistorylist", StringComparison.OrdinalIgnoreCase))) return;
        request.Headers.TryAddWithoutValidation("User-Agent", "ftstand");
        request.Headers.TryAddWithoutValidation("Cookie", $"SGUID=session_id={validated.SessionId}&login=");
    }
}
