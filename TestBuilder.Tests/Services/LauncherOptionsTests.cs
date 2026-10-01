using System.Net;
using TestBuilder.Services;
using TestBuilder.Services.Http;

namespace TestBuilder.Tests.Services;

public class LauncherOptionsTests
{
    [Fact]
    public void LegacyArguments_MatchRtlSessionThenUserContract()
    {
        var options = LauncherOptions.Parse(new[] { "test-session-42", "Тестовый оператор" });
        Assert.True(options.IsValid);
        Assert.Equal("test-session-42", options.SessionId);
        Assert.Equal("Тестовый оператор", options.UserName);
    }

    [Fact]
    public void NamedArguments_DoNotBecomeSessionAndName()
    {
        var options = LauncherOptions.Parse(new[] { "--user", "Тест", "--session", "42" });
        Assert.True(options.IsValid);
        Assert.Equal("42", options.SessionId);
        Assert.Equal("Тест", options.UserName);
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--engineer")]
    [InlineData("--operator")]
    [InlineData("--session")]
    [InlineData("session;another=value")]
    [InlineData("session&login=admin")]
    [InlineData("session\r\nanything")]
    public void InvalidArguments_FailClosedWithoutEchoingSession(string argument)
    {
        var options = LauncherOptions.Parse(new[] { argument });
        Assert.False(options.IsValid);
        Assert.Empty(options.SessionId);
    }

    [Fact]
    public void LocalLaunch_HasNoLauncherIdentity()
    {
        var options = LauncherOptions.Parse(Array.Empty<string>());
        Assert.True(options.IsValid);
        Assert.Empty(options.SessionId);
        Assert.Empty(options.UserName);
    }

    [Fact]
    public async Task SerialRequest_UsesLegacyHeadersThroughHttpService()
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        using var service = new HttpRequestService(client, configureRequest: request =>
            LauncherServerClient.ApplyHeaders(request, "test-session", "https://server.example"));
        var result = await service.GetAsync("https://server.example/api/Api.svc/getSerialNum?devType=PSW",
            TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.True(result.IsSuccessStatusCode);
        Assert.Equal("SGUID=session_id=test-session&login=", handler.Cookie);
        Assert.Equal("ftstand", handler.UserAgent);
    }

    [Theory]
    [InlineData("http://192.168.0.1/api/Api.svc/getSerialNum")]
    [InlineData("https://other.example/api/Api.svc/getSerialNum")]
    [InlineData("http://server.example/api/Api.svc/getSerialNum")]
    [InlineData("https://server.example/test.shtml")]
    public void Session_IsNotAttachedToDutOrOtherOriginOrUnrelatedEndpoint(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        LauncherServerClient.ApplyHeaders(request, "test-session", "https://server.example");
        Assert.False(request.Headers.Contains("Cookie"));
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Cookie { get; private set; }
        public string? UserAgent { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Cookie = request.Headers.GetValues("Cookie").Single();
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("12345") });
        }
    }
}
