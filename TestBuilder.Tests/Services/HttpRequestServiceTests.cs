using System.Net;
using TestBuilder.Services.Http;

namespace TestBuilder.Tests.Services;

public class HttpRequestServiceTests
{
    [Theory]
    [InlineData("http://localhost/status", true)]
    [InlineData("http://127.0.0.1/status", true)]
    [InlineData("http://10.20.30.40/status", true)]
    [InlineData("http://172.16.0.1/status", true)]
    [InlineData("http://172.31.255.254/status", true)]
    [InlineData("http://192.168.0.1/status", true)]
    [InlineData("http://169.254.10.20/status", true)]
    [InlineData("http://[::1]/status", true)]
    [InlineData("http://[fd00::1]/status", true)]
    [InlineData("http://172.15.0.1/status", false)]
    [InlineData("http://172.32.0.1/status", false)]
    [InlineData("http://192.167.0.1/status", false)]
    [InlineData("https://example.com/status", false)]
    public void ShouldBypassProxy_RecognizesOnlyLocalAndPrivateDestinations(
        string url,
        bool expected)
    {
        Assert.Equal(expected, HttpRequestService.ShouldBypassProxy(new Uri(url)));
    }

    [Fact]
    public async Task GetAsync_UsesDirectClientForPrivateAddress_AndPrimaryForPublicHost()
    {
        using var primaryClient = new HttpClient(new StaticResponseHandler("primary"));
        using var directClient = new HttpClient(new StaticResponseHandler("direct"));
        using var service = new HttpRequestService(primaryClient, directClient, disposeClients: false);

        var localResult = await service.GetAsync(
            "http://192.168.0.1/status",
            TimeSpan.FromSeconds(1),
            CancellationToken.None);
        var publicResult = await service.GetAsync(
            "https://example.com/status",
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal("direct", localResult.Body);
        Assert.Equal("primary", publicResult.Body);
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly string _body;

        public StaticResponseHandler(string body)
        {
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body),
                RequestMessage = request
            });
        }
    }
}
