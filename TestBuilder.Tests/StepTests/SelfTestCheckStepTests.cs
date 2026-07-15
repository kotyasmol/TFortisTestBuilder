using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Http;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class SelfTestCheckStepTests
{
    [Fact]
    public async Task SelfTestCheckStep_ReturnsTrue_AndSavesFields_WhenRulesPass()
    {
        var service = new QueueHttpRequestService(
            HttpRequestResult.Success(
                200,
                "<html><selftest><init_ok>1</init_ok><firmvare_vers>1021</firmvare_vers><default_mac>AC:CC:11:A6:00:00</default_mac></selftest></html>",
                TimeSpan.FromMilliseconds(10)));

        var step = CreateStep(service, "init_ok=1..1\nfirmvare_vers=1000..2000");
        var context = new TestContext(new RegisterState());

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.True(context.GetVariable<bool>("SelfTest.Ok"));
        Assert.Equal("1", context.GetVariable<string>("Dut.init_ok"));
        Assert.Equal("1021", context.GetVariable<string>("Dut.firmvare_vers"));
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task SelfTestCheckStep_RetriesUntilSelfTestAppears()
    {
        var service = new QueueHttpRequestService(
            HttpRequestResult.Success(
                200,
                "<html>booting</html>",
                TimeSpan.FromMilliseconds(10)),
            HttpRequestResult.Success(
                200,
                "<html><selftest><init_ok>1</init_ok><default_mac>AC:CC:11:A6:00:00</default_mac></selftest></html>",
                TimeSpan.FromMilliseconds(10)));

        var step = CreateStep(service, "init_ok=1..1", timeoutMs: 2500);
        var context = new TestContext(new RegisterState());

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.True(context.GetVariable<bool>("SelfTest.Ok"));
        Assert.Equal(2, context.GetVariable<int>("SelfTest.Attempts"));
        Assert.Equal(2, service.Calls);
    }

    [Fact]
    public async Task SelfTestCheckStep_ReturnsFalse_WhenRuleFails()
    {
        var service = new QueueHttpRequestService(
            HttpRequestResult.Success(
                200,
                "<selftest><init_ok>0</init_ok><default_mac>AC:CC:11:A6:00:00</default_mac></selftest>",
                TimeSpan.FromMilliseconds(10)));

        var step = CreateStep(service, "init_ok=1..1");
        var context = new TestContext(new RegisterState());

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.False(context.GetVariable<bool>("SelfTest.Ok"));
        Assert.Contains("init_ok", context.GetVariable<string>("SelfTest.Error"));
        Assert.True(context.HasCriticalError);
    }

    private static SelfTestCheckStep CreateStep(
        IHttpRequestService service,
        string rules,
        int timeoutMs = SelfTestCheckStep.DefaultTimeoutMs)
    {
        return new SelfTestCheckStep(
            service,
            NullLogger.Instance,
            SelfTestCheckStep.DefaultUrl,
            timeoutMs,
            SelfTestCheckStep.DefaultOutputPrefix,
            rules,
            failOnError: true,
            useBrowser: false);
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
