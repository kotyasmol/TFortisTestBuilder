using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Http;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class RequestTestPageStepTests
{
    [Fact]
    public async Task RequestTestPageStep_ReturnsTrue_AndSavesContext_WhenPageLoaded()
    {
        var service = new QueueHttpRequestService(
            HttpRequestResult.Success(
                200,
                "<!DOCTYPE settings><settings></settings>",
                TimeSpan.FromMilliseconds(10)));

        var step = CreateStep(service);
        var context = new TestContext(new RegisterState());

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal("<!DOCTYPE settings><settings></settings>", context.GetVariable<string>("TestPageRaw"));
        Assert.True(context.GetVariable<bool>("TestPageRequestOk"));
        Assert.Equal("http://192.168.0.1/test.shtml", context.GetVariable<string>("TestPageUrl"));
        Assert.Equal(200, context.GetVariable<int>("TestPageStatusCode"));
        Assert.Equal("Success", context.GetVariable<string>("TestPageRequestStatus"));
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task RequestTestPageStep_Retries_WhenFirstResponseIsEmpty()
    {
        var service = new QueueHttpRequestService(
            HttpRequestResult.Success(200, string.Empty, TimeSpan.FromMilliseconds(10)),
            HttpRequestResult.Success(200, "<!DOCTYPE settings><settings></settings>", TimeSpan.FromMilliseconds(10)));

        var step = CreateStep(service, retryCount: 1, retryDelayMs: 1);
        var context = new TestContext(new RegisterState());

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(2, service.Calls);
        Assert.True(context.GetVariable<bool>("TestPageRequestOk"));
    }

    [Fact]
    public async Task RequestTestPageStep_ReturnsFalse_WhenContentIsInvalid()
    {
        var service = new QueueHttpRequestService(
            HttpRequestResult.Success(200, "<html>not test page</html>", TimeSpan.FromMilliseconds(10)));

        var step = CreateStep(service, retryCount: 0);
        var context = new TestContext(new RegisterState());

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.False(context.GetVariable<bool>("TestPageRequestOk"));
        Assert.Equal("InvalidContent", context.GetVariable<string>("TestPageRequestStatus"));
        Assert.Contains("ожидаемую строку", context.GetVariable<string>("TestPageError"));
        Assert.True(context.HasCriticalError);
    }

    [Fact]
    public async Task RequestTestPageStep_ReturnsFalse_WithTimeoutStatus_WhenRequestTimesOut()
    {
        var service = new QueueHttpRequestService(
            HttpRequestResult.Failure(
                "Таймаут HTTP-запроса: 160000 мс.",
                TimeSpan.FromMilliseconds(10)));

        var step = CreateStep(service, retryCount: 0);
        var context = new TestContext(new RegisterState());

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.False(context.GetVariable<bool>("TestPageRequestOk"));
        Assert.Equal("Timeout", context.GetVariable<string>("TestPageRequestStatus"));
        Assert.Equal("Таймаут HTTP-запроса: 160000 мс.", context.GetVariable<string>("TestPageError"));
    }

    private static RequestTestPageStep CreateStep(
        IHttpRequestService service,
        int retryCount = 1,
        int retryDelayMs = 0)
    {
        return new RequestTestPageStep(
            service,
            NullLogger.Instance,
            RequestTestPageStep.DefaultBaseUrl,
            RequestTestPageStep.DefaultPath,
            RequestTestPageStep.DefaultTimeoutMs,
            retryCount,
            retryDelayMs,
            RequestTestPageStep.DefaultOutputVariableName,
            failOnError: true,
            requireSuccessStatusCode: true,
            RequestTestPageStep.DefaultExpectedContentContains,
            RequestTestPageStep.DefaultStatusCodeVariableName,
            RequestTestPageStep.DefaultErrorVariableName,
            RequestTestPageStep.DefaultElapsedMsVariableName);
    }

    private sealed class QueueHttpRequestService : IHttpRequestService
    {
        private readonly Queue<HttpRequestResult> _results;

        public QueueHttpRequestService(params HttpRequestResult[] results)
        {
            _results = new Queue<HttpRequestResult>(results);
        }

        public int Calls { get; private set; }

        public Task<HttpRequestResult> GetAsync(
            string url,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_results.Dequeue());
        }
    }
}
