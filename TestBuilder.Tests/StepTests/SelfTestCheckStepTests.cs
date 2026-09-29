using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class SelfTestCheckStepTests
{
    [Theory]
    [InlineData("20d", "105", "525..525", true)]
    [InlineData("208", "105", "520..520", true)]
    [InlineData("20d", "105", "526..600", false)]
    [InlineData("oops", "105", "0..65535", false)]
    public async Task LegacyVersionsUseHexadecimalWithoutChangingRawValues(string firmware, string boot, string range, bool passed)
    {
        var service = new QueueHttpRequestService(HttpRequestResult.Success(200,
            $"<selftest><init_ok>1</init_ok><default_mac>00:11:22:33:44:55</default_mac><firmvare_vers>{firmware}</firmvare_vers><boot_vers>{boot}</boot_vers></selftest>", TimeSpan.Zero));
        var context = new TestContext(new RegisterState());
        var step = CreateStep(service, $"init_ok=1..1\nfirmvare_vers={range}\nboot_vers=261..261", url: "http://192.168.0.1/test.shtml");
        Assert.Equal(passed ? StepResult.True : StepResult.False, await step.ExecuteAsync(context, CancellationToken.None));
        Assert.Equal(firmware, context.GetVariable<string>("Dut.firmvare_vers"));
        Assert.True(context.GetVariable<bool>("SelfTest.Parsed"));
        Assert.Equal("Dut", context.GetVariable<string>("SelfTest.OutputPrefix"));
    }

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
    public async Task SelfTestCheckStep_ProbesDutBeforeStartingBrowser()
    {
        var probeResults = new Queue<bool>(new[] { false, false, true });
        var probeCalls = 0;
        var browserCalls = 0;
        var step = new SelfTestCheckStep(
            new QueueHttpRequestService(),
            NullLogger.Instance,
            SelfTestCheckStep.DefaultUrl,
            timeoutMs: 2000,
            SelfTestCheckStep.DefaultOutputPrefix,
            "init_ok=1..1",
            failOnError: true,
            useBrowser: true,
            pollIntervalMs: 100,
            enforceMinimumDeviceReadyTimeout: false,
            endpointProbe: (_, _, _) =>
            {
                probeCalls++;
                return Task.FromResult(probeResults.Dequeue());
            },
            browserPageLoader: (_, _, _) =>
            {
                browserCalls++;
                return Task.FromResult(HttpRequestResult.Success(
                    0,
                    "<selftest><init_ok>1</init_ok><default_mac>AC:CC:11:A6:00:00</default_mac></selftest>",
                    TimeSpan.FromMilliseconds(10)));
            });
        var context = new TestContext(new RegisterState());

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(3, probeCalls);
        Assert.Equal(1, browserCalls);
        Assert.Equal(3, context.GetVariable<int>("SelfTest.ProbeAttempts"));
        Assert.Equal(1, context.GetVariable<int>("SelfTest.Attempts"));
    }

    [Fact]
    public void SelfTestCheckStep_StartsBrowserOnRequestedUrlUsingNormalNetworkSettings()
    {
        const string url =
            "http://192.168.0.1/cgi-bin/luci/admin/statistics/deviceinfo?luci_username=admin&luci_password=admin";

        var startInfo = SelfTestCheckStep.CreateBrowserStartInfo(
            "chrome.exe",
            url,
            debuggingPort: 9222,
            userDataDir: @"C:\Temp\TestBuilderHeadlessChrome_test");

        Assert.Equal(url, startInfo.ArgumentList[^1]);
        Assert.DoesNotContain("about:blank", startInfo.ArgumentList);
        Assert.DoesNotContain("--no-proxy-server", startInfo.ArgumentList);
    }

    [Fact]
    public async Task SelfTestCheckStep_DoesNotStartBrowserIfProbeExhaustedDeadline()
    {
        var browserCalls = 0;
        var step = new SelfTestCheckStep(
            new QueueHttpRequestService(), NullLogger.Instance,
            SelfTestCheckStep.DefaultUrl, timeoutMs: 100, "Dut", "init_ok=1..1",
            failOnError: true, useBrowser: true, pollIntervalMs: 100,
            enforceMinimumDeviceReadyTimeout: false,
            endpointProbe: async (_, timeout, token) =>
            {
                await Task.Delay(timeout + TimeSpan.FromMilliseconds(20), token);
                return true;
            },
            browserPageLoader: (_, _, _) =>
            {
                browserCalls++;
                return Task.FromResult(HttpRequestResult.Failure("Unexpected browser", TimeSpan.Zero));
            });
        var context = new TestContext(new RegisterState());

        Assert.Equal(StepResult.False, await step.ExecuteAsync(context, CancellationToken.None));
        Assert.Equal(0, browserCalls);
        Assert.Equal(0, context.GetVariable<int>("SelfTest.Attempts"));
    }

    [Fact]
    public async Task BrowserAttempt_CancelledBeforeStartDoesNotTouchBrowser()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var stages = new List<string>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SelfTestCheckStep.GetPageWithBrowserAsync(
            "nonexistent-browser.exe", SelfTestCheckStep.DefaultUrl,
            TimeSpan.FromSeconds(1), stop.Token, stages.Add));

        Assert.Empty(stages);
    }

    [Fact]
    public async Task BrowserAttempt_StartFailurePreservesErrorInsteadOfRunningCleanupOnUnstartedProcess()
    {
        // Deliberately nonexistent path: this test never starts Chrome or contacts the DUT.
        var missingBrowser = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing-browser.exe");
        var stages = new List<string>();

        var result = await SelfTestCheckStep.GetPageWithBrowserAsync(
            missingBrowser, SelfTestCheckStep.DefaultUrl, TimeSpan.FromSeconds(1),
            CancellationToken.None, stages.Add);

        Assert.Contains("missing-browser.exe", result.ErrorMessage);
        Assert.Contains("starting Chrome", result.ErrorMessage);
        Assert.DoesNotContain(stages, stage => stage.Contains("closing Chrome", StringComparison.Ordinal));
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

    private static string PswHardwareXml() => "<selftest><init_ok>1</init_ok><dev_type>6</dev_type>" +
        "<default_mac>C0:11:A6:06:30:9F</default_mac><cpu_id>20353852584850023a0020</cpu_id>" +
        "<adc_1_2>1531</adc_1_2><adc_1_5>1906</adc_1_5><adc_2_5>3074</adc_2_5>" +
        "<sfp_7_pres>1</sfp_7_pres><sfp_7_sd>1</sfp_7_sd><sfp_7_id>3</sfp_7_id>" +
        "<sfp_8_pres>1</sfp_8_pres><sfp_8_sd>1</sfp_8_sd><sfp_8_id>3</sfp_8_id>" +
        "<poe_a_1_state>1</poe_a_1_state><poe_a_2_state>1</poe_a_2_state><poe_a_3_state>1</poe_a_3_state>" +
        "<poe_a_6_state>0</poe_a_6_state><sensor_0>0</sensor_0><sensor_2>0</sensor_2></selftest>";

    [Theory]
    [InlineData("3")]
    [InlineData("255")]
    public async Task PswHardwareAcceptsBoardSnapshotWithoutExtraSensorsOrInventedPoeFlags(string sfpId)
    {
        var service = new QueueHttpRequestService(HttpRequestResult.Success(200, PswHardwareXml().Replace("_id>3<", "_id>" + sfpId + "<"), TimeSpan.Zero));
        var context = new TestContext(new RegisterState());
        var step = CreateStep(service, "init_ok=1..1", psw2G6FHardwareChecks: true);
        Assert.Equal(StepResult.True, await step.ExecuteAsync(context, CancellationToken.None));
        Assert.False(context.HasCriticalError);
        Assert.All(context.ReportEntries, entry => Assert.True(entry.IsSuccess));
        Assert.Contains(context.ReportEntries, entry => entry.Name.EndsWith("adc_1_2") && entry.Value.StartsWith("1543.48"));
        Assert.DoesNotContain(context.ReportEntries, entry => entry.Name.Contains("sensor_"));
    }

    [Theory]
    [InlineData("adc_1_2", "1300")]
    [InlineData("adc_1_5", "2200")]
    [InlineData("adc_2_5", "3400")]
    [InlineData("adc_2_5", "0")]
    [InlineData("adc_2_5", "NaN")]
    [InlineData("adc_1_2", "Infinity")]
    [InlineData("sfp_7_pres", "0")]
    [InlineData("sfp_8_sd", "0")]
    [InlineData("poe_a_2_state", "0")]
    [InlineData("dev_type", "32")]
    [InlineData("sfp_7_pres", null)]
    [InlineData("adc_1_5", null)]
    public async Task PswHardwareRejectsBadOrMissingFieldsEvenIfContextContainsOldGoodValues(string field, string? value)
    {
        var xml = System.Xml.Linq.XDocument.Parse(PswHardwareXml());
        var element = xml.Root!.Element(field)!;
        if (value == null) element.Remove(); else element.Value = value;
        var service = new QueueHttpRequestService(HttpRequestResult.Success(200, xml.ToString(), TimeSpan.Zero));
        var context = new TestContext(new RegisterState());
        context.SetVariable("Dut." + field, "1");
        var step = CreateStep(service, "init_ok=1..1", psw2G6FHardwareChecks: true);
        Assert.Equal(StepResult.False, await step.ExecuteAsync(context, CancellationToken.None));
        Assert.True(context.HasCriticalError);
        Assert.False(context.GetVariable<bool>("SelfTest.Ok"));
        Assert.Contains(context.ReportEntries, entry => entry.Name.EndsWith(field) && !entry.IsSuccess);
        Assert.Contains(field, context.GetVariable<string>("SelfTest.Error"));
    }

    [Theory]
    [InlineData(1364, true)]
    [InlineData(1627, true)]
    [InlineData(1363, false)]
    [InlineData(1628, false)]
    public async Task PswAdcUsesInclusiveQtBoundsAfterReferenceCorrection(int corrected, bool passed)
    {
        var xml = System.Xml.Linq.XDocument.Parse(PswHardwareXml());
        xml.Root!.Element("adc_2_5")!.Value = "3200";
        xml.Root.Element("adc_1_2")!.Value = (corrected + 48).ToString();
        var service = new QueueHttpRequestService(HttpRequestResult.Success(200, xml.ToString(), TimeSpan.Zero));
        var context = new TestContext(new RegisterState());
        var step = CreateStep(service, "init_ok=1..1", psw2G6FHardwareChecks: true);
        Assert.Equal(passed ? StepResult.True : StepResult.False, await step.ExecuteAsync(context, CancellationToken.None));
    }

    private static SelfTestCheckStep CreateStep(
        IHttpRequestService service,
        string rules,
        int timeoutMs = SelfTestCheckStep.DefaultTimeoutMs,
        string? url = null,
        int pollIntervalMs = SelfTestCheckStep.DefaultPollIntervalMs,
        bool psw2G6FHardwareChecks = false)
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
            pollIntervalMs: pollIntervalMs,
            psw2G6FHardwareChecks: psw2G6FHardwareChecks);
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
