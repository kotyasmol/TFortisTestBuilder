using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;
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
        var pageState = new SelfTestPageState();
        var context = new TestContext(new RegisterState())
        {
            SelfTestPageState = pageState
        };

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.True(context.GetVariable<bool>("SelfTest.Ok"));
        Assert.Equal("1", context.GetVariable<string>("Dut.init_ok"));
        Assert.Equal("1021", context.GetVariable<string>("Dut.firmvare_vers"));
        Assert.Equal(SelfTestPageLoadState.Loaded, pageState.Current.LoadState);
        Assert.Equal("1", pageState.Current.Fields["init_ok"]);
        Assert.Equal("1021", pageState.Current.Fields["firmvare_vers"]);
        Assert.Equal("AC:CC:11:A6:00:00", pageState.Current.Fields["default_mac"]);
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task SelfTestCheckStep_StoresRawXmlInContextWithoutWritingSelfTestFile()
    {
        var outputFile = Path.Combine(AppContext.BaseDirectory, "selftest.txt");
        if (File.Exists(outputFile))
        {
            File.Delete(outputFile);
        }

        var service = new QueueHttpRequestService(
            HttpRequestResult.Success(
                200,
                "<selftest><init_ok>1</init_ok><default_mac>AC:CC:11:A6:00:00</default_mac></selftest>",
                TimeSpan.FromMilliseconds(10)));

        var step = CreateStep(service, "init_ok=1..1");
        var context = new TestContext(new RegisterState());

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Contains("<selftest>", context.GetVariable<string>(SelfTestCheckStep.DefaultOutputVariableName));
        Assert.False(File.Exists(outputFile));
    }

    [Fact]
    public async Task SelfTestCheckStep_RetriesSameUrlUntilSelfTestAppears()
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
            url: "http://192.168.0.1/selftest.xml",
            pollIntervalMs: 10);
        var context = new TestContext(new RegisterState());

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.True(context.GetVariable<bool>("SelfTest.Ok"));
        Assert.Equal(2, context.GetVariable<int>("SelfTest.Attempts"));
        Assert.Equal(2, service.Calls);
        Assert.All(service.RequestedUrls, requestedUrl => Assert.Equal("http://192.168.0.1/selftest.xml", requestedUrl));
    }

    [Fact]
    public async Task SelfTestCheckStep_RetriesOriginalUrlWithoutTryingLegacyTestShtml()
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

        var step = CreateStep(service, "init_ok=1..1", pollIntervalMs: 10);
        var context = new TestContext(new RegisterState());

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.True(context.GetVariable<bool>("SelfTest.Ok"));
        Assert.Equal(2, service.Calls);
        Assert.Equal(SelfTestCheckStep.DefaultUrl, service.RequestedUrls[0]);
        Assert.Equal(SelfTestCheckStep.DefaultUrl, service.RequestedUrls[1]);
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
    public async Task SelfTestCheckStep_PrefersRootField_WhenNestedFieldHasSameName()
    {
        var service = new QueueHttpRequestService(
            HttpRequestResult.Success(
                200,
                "<selftest><init_ok>1</init_ok><dev_type>32</dev_type><system><dev_type>0</dev_type></system><default_mac>AC:CC:11:A6:00:00</default_mac></selftest>",
                TimeSpan.FromMilliseconds(10)));

        var step = CreateStep(service, "init_ok=1..1\ndev_type=32..32");
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

    [Fact]
    public async Task SelfTestCheckStep_SavesPollIntervalAndValidationSummary()
    {
        var service = new QueueHttpRequestService(
            HttpRequestResult.Success(
                200,
                "<selftest><init_ok>1</init_ok><dev_type>32</dev_type><default_mac>AC:CC:11:A6:00:00</default_mac></selftest>",
                TimeSpan.FromMilliseconds(10)));

        var step = CreateStep(service, "init_ok=1..1\ndev_type=32..32", pollIntervalMs: 2500);
        var context = new TestContext(new RegisterState());

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(2500, context.GetVariable<int>("SelfTest.PollIntervalMs"));
        Assert.Equal(2, context.GetVariable<int>("SelfTest.CheckedRuleCount"));
        Assert.Equal(0, context.GetVariable<int>("SelfTest.FailedRuleCount"));
        Assert.Contains("dev_type=OK", context.GetVariable<string>("SelfTest.ValidationSummary"));
    }

    [Fact]
    public async Task SelfTestCheckStep_LogsValidationDetails()
    {
        var logger = new RecordingLogger();
        var service = new QueueHttpRequestService(
            HttpRequestResult.Success(
                200,
                "<selftest><init_ok>0</init_ok><default_mac>AC:CC:11:A6:00:00</default_mac></selftest>",
                TimeSpan.FromMilliseconds(10)));

        var step = new SelfTestCheckStep(
            service,
            logger,
            "http://192.168.0.1/selftest.xml",
            1000,
            SelfTestCheckStep.DefaultOutputPrefix,
            "init_ok=1..1",
            failOnError: true,
            useBrowser: false,
            pollIntervalMs: 100);
        var context = new TestContext(new RegisterState());

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.Contains(logger.Messages, message => message.Contains("Selftest check init_ok", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("expected 1..1", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("actual 0", StringComparison.Ordinal));
    }

    private static SelfTestCheckStep CreateStep(
        IHttpRequestService service,
        string rules,
        int timeoutMs = SelfTestCheckStep.DefaultTimeoutMs,
        string? url = null,
        int pollIntervalMs = SelfTestCheckStep.DefaultPollIntervalMs)
    {
        return new SelfTestCheckStep(
            service,
            NullLogger.Instance,
            url ?? SelfTestCheckStep.DefaultUrl,
            timeoutMs,
            SelfTestCheckStep.DefaultOutputPrefix,
            rules,
            failOnError: true,
            useBrowser: false,
            pollIntervalMs: pollIntervalMs);
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

    private sealed class RecordingLogger : ILogger
    {
        public string Category => "Test";

        public System.Collections.ObjectModel.ObservableCollection<LogEntry> Entries { get; } = new();

        public List<string> Messages { get; } = new();

        public void Log(LogLevel level, string message)
        {
            Messages.Add(message);
            Entries.Add(new LogEntry(DateTime.UtcNow, level, Category, message));
        }

        public void Trace(string message) => Log(LogLevel.Trace, message);
        public void Debug(string message) => Log(LogLevel.Debug, message);
        public void Info(string message) => Log(LogLevel.Info, message);
        public void Warning(string message) => Log(LogLevel.Warning, message);
        public void Error(string message) => Log(LogLevel.Error, message);
        public void Clear()
        {
            Messages.Clear();
            Entries.Clear();
        }
    }
}
