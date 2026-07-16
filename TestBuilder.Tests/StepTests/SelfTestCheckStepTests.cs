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

        var step = CreateStep(
            service,
            "init_ok=1..1",
            timeoutMs: 2500,
            url: "http://192.168.0.1/selftest.xml");
        var context = new TestContext(new RegisterState());

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.True(context.GetVariable<bool>("SelfTest.Ok"));
        Assert.Equal(2, context.GetVariable<int>("SelfTest.Attempts"));
        Assert.Equal(2, service.Calls);
    }

    [Fact]
    public async Task SelfTestCheckStep_TriesLegacyTestShtml_WhenLuciPageHasNoXml()
    {
        var service = new QueueHttpRequestService(
            HttpRequestResult.Success(
                200,
                "<html>luci page without hidden xml</html>",
                TimeSpan.FromMilliseconds(10)),
            HttpRequestResult.Success(
                200,
                "<!DOCTYPE settings><settings><init_ok>1</init_ok><default_mac>AC:CC:11:A6:00:00</default_mac></settings>",
                TimeSpan.FromMilliseconds(10)));

        var step = CreateStep(service, "init_ok=1..1");
        var context = new TestContext(new RegisterState());

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal("1", context.GetVariable<string>("Dut.init_ok"));
        Assert.Equal(2, service.Calls);
        Assert.Equal(SelfTestCheckStep.DefaultUrl, service.RequestedUrls[0]);
        Assert.Equal("http://192.168.0.1/test.shtml", service.RequestedUrls[1]);
    }

    [Fact]
    public async Task SelfTestCheckStep_ReturnsTrue_WhenSelfTestIsHtmlEscapedInDom()
    {
        var service = new QueueHttpRequestService(
            HttpRequestResult.Success(
                200,
                "<html><script>window.hidden = '&lt;selftest source=&quot;luci&quot;&gt;&lt;init_ok&gt;1&lt;/init_ok&gt;&lt;default_mac&gt;AC:CC:11:A6:00:00&lt;/default_mac&gt;&lt;/selftest&gt;';</script></html>",
                TimeSpan.FromMilliseconds(10)));

        var step = CreateStep(service, "init_ok=1..1");
        var context = new TestContext(new RegisterState());

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal("1", context.GetVariable<string>("Dut.init_ok"));
        Assert.Equal("AC:CC:11:A6:00:00", context.GetVariable<string>("Dut.default_mac"));
    }

    [Fact]
    public async Task SelfTestCheckStep_ReturnsTrue_WhenLegacySettingsXmlIsReturned()
    {
        var service = new QueueHttpRequestService(
            HttpRequestResult.Success(
                200,
                "<!DOCTYPE html><settings><init_ok>1</init_ok><dev_type>32</dev_type><default_mac>AC:CC:11:A6:00:00</default_mac></settings>",
                TimeSpan.FromMilliseconds(10)));

        var step = CreateStep(service, "init_ok=1..1\ndev_type=0..65535");
        var context = new TestContext(new RegisterState());

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal("32", context.GetVariable<string>("Dut.dev_type"));
    }

    [Fact]
    public async Task SelfTestCheckStep_ReturnsTrue_WhenLegacySettingsXmlIsJsEscaped()
    {
        var service = new QueueHttpRequestService(
            HttpRequestResult.Success(
                200,
                "<html><script>window.hidden = \"\\u003Csettings\\u003E\\u003Cinit_ok\\u003E1\\u003C\\/init_ok\\u003E\\u003Cdefault_mac\\u003EAC:CC:11:A6:00:00\\u003C\\/default_mac\\u003E\\u003C\\/settings\\u003E\";</script></html>",
                TimeSpan.FromMilliseconds(10)));

        var step = CreateStep(service, "init_ok=1..1");
        var context = new TestContext(new RegisterState());

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal("AC:CC:11:A6:00:00", context.GetVariable<string>("Dut.default_mac"));
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
        int timeoutMs = SelfTestCheckStep.DefaultTimeoutMs,
        string? url = null)
    {
        return new SelfTestCheckStep(
            service,
            NullLogger.Instance,
            url ?? SelfTestCheckStep.DefaultUrl,
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
        public List<string> RequestedUrls { get; } = new();

        public Task<HttpRequestResult> GetAsync(
            string url,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Calls++;
            RequestedUrls.Add(url);
            return Task.FromResult(_results.Dequeue());
        }
    }
}
