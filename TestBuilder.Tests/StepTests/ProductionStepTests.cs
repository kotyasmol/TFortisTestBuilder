using System.Net;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.StepTests;

[Collection(AppSettingsTestCollection.Name)]
public class ProductionStepTests
{
    [Fact]
    public async Task GetSerialNumberFromServerStep_EncodesDeviceTypeAndSavesSerial()
    {
        var service = new CapturingHttpService(HttpRequestResult.Success(200, "12345", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        context.SetVariable("Dut.cpu_id", "CPU 1");

        var step = new GetSerialNumberFromServerStep(
            service,
            NullLogger.Instance,
            "http://server",
            "PSW+UPS-Box 8x2Pro",
            "Dut.cpu_id",
            1000,
            0,
            0,
            "SerialNumber",
            failOnError: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(12345, context.GetVariable<int>("SerialNumber"));
        Assert.Equal(12345, context.GetVariable<int>("NetTest.SerialNumber"));
        Assert.Equal("12345", context.GetVariable<string>("SerialNumberText"));
        Assert.True(context.GetVariable<bool>("SerialNumberReceived"));
        Assert.Equal(1, context.GetVariable<int>("SerialNumberAttempts"));
        Assert.Equal(200, context.GetVariable<int>("SerialNumberStatusCode"));
        Assert.Equal("CPU 1", context.GetVariable<string>("SerialNumberCpuId"));
        Assert.Contains("devType=PSW%2BUPS-Box%208x2Pro", service.LastUrl);
        Assert.Contains("cpuId=CPU%201", service.LastUrl);
    }

    [Fact]
    public async Task GetSerialNumberFromServerStep_AcceptsBareHostLikeLegacyQtCode()
    {
        var service = new CapturingHttpService(HttpRequestResult.Success(200, "321", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());

        var step = new GetSerialNumberFromServerStep(
            service,
            NullLogger.Instance,
            "stand-server.local",
            "PSW+UPS-Box 8x2Pro",
            string.Empty,
            1000,
            0,
            0,
            "SerialNumber",
            failOnError: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(321, context.GetVariable<int>("SerialNumber"));
        Assert.Equal("http://stand-server.local/api/api.svc/getSerialNum?devType=PSW%2BUPS-Box%208x2Pro", service.LastUrl);
    }

    [Fact]
    public async Task GetSerialNumberFromServerStep_RequiresConfiguredCpuIdVariable()
    {
        var service = new CapturingHttpService(HttpRequestResult.Success(200, "321", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        context.SetVariable("SerialNumber", 999);

        var step = CreateSerialStep(service, cpuIdVariableName: "Dut.cpu_id");

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.Equal(0, service.Calls);
        Assert.False(context.Variables.ContainsKey("SerialNumber"));
        Assert.False(context.GetVariable<bool>("SerialNumberReceived"));
        Assert.Contains("CPU ID", context.GetVariable<string>("SerialNumberError"));
    }

    [Fact]
    public async Task GetSerialNumberFromServerStep_ResolvesCpuIdCaseInsensitively()
    {
        var service = new CapturingHttpService(HttpRequestResult.Success(200, "3200001", TimeSpan.FromMilliseconds(2)));
        var context = new TestContext(new RegisterState());
        context.SetVariable("Dut.CPU_ID", "ABC/123");

        var step = CreateSerialStep(service, cpuIdVariableName: "Dut.cpu_id");

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(3200001, context.GetVariable<int>("SerialNumber"));
        Assert.Contains("cpuId=ABC%2F123", service.LastUrl);
    }

    [Theory]
    [InlineData("http://server/api", "http://server/api/api.svc/getSerialNum?devType=PSW%2BUPS-Box%208x2Pro")]
    [InlineData("http://server/api/Api.svc", "http://server/api/Api.svc/getSerialNum?devType=PSW%2BUPS-Box%208x2Pro")]
    [InlineData("http://server/api/api.svc/getSerialNum/", "http://server/api/api.svc/getSerialNum?devType=PSW%2BUPS-Box%208x2Pro")]
    public async Task GetSerialNumberFromServerStep_AcceptsCommonServerUrlForms(
        string serverUrl,
        string expectedUrl)
    {
        var service = new CapturingHttpService(HttpRequestResult.Success(200, "3200002", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        var step = CreateSerialStep(service, serverUrl, cpuIdVariableName: string.Empty);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(expectedUrl, service.LastUrl);
    }

    [Fact]
    public async Task GetSerialNumberFromServerStep_RetriesAndSavesLastHttpDiagnostics()
    {
        var service = new QueueHttpService(
            HttpRequestResult.Success(503, "temporarily unavailable", TimeSpan.FromMilliseconds(12)),
            HttpRequestResult.Success(200, "\"3200003\"", TimeSpan.FromMilliseconds(7)));
        var context = new TestContext(new RegisterState());
        context.SetVariable("Dut.cpu_id", "CPU-42");
        var step = new GetSerialNumberFromServerStep(
            service,
            NullLogger.Instance,
            "http://server",
            "PSW+UPS-Box 8x2Pro",
            "Dut.cpu_id",
            1000,
            1,
            0,
            "SerialNumber",
            failOnError: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(3200003, context.GetVariable<int>("SerialNumber"));
        Assert.Equal(2, context.GetVariable<int>("SerialNumberAttempts"));
        Assert.Equal(200, context.GetVariable<int>("SerialNumberStatusCode"));
        Assert.Equal(7, context.GetVariable<int>("SerialNumberElapsedMs"));
        Assert.Equal(2, service.RequestedUrls.Count);
    }

    [Fact]
    public async Task GetSerialNumberFromServerStep_ReusesFullEndpointUrl()
    {
        var service = new CapturingHttpService(HttpRequestResult.Success(200, "\uFEFF654\r\n", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        context.SetVariable("Dut.cpu_id", "ABC+123");

        var step = new GetSerialNumberFromServerStep(
            service,
            NullLogger.Instance,
            "https://server/api/api.svc/getSerialNum",
            "PSW+UPS-Box 8x2Pro",
            "Dut.cpu_id",
            1000,
            0,
            0,
            "ServerSerial",
            failOnError: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(654, context.GetVariable<int>("ServerSerial"));
        Assert.Equal(654, context.GetVariable<int>("SerialNumber"));
        Assert.Equal("https://server/api/api.svc/getSerialNum?devType=PSW%2BUPS-Box%208x2Pro&cpuId=ABC%2B123", service.LastUrl);
    }

    [Fact]
    public async Task GetSerialNumberFromServerStep_RejectsPlaceholderWithoutDnsRequest()
    {
        var service = new CapturingHttpService(HttpRequestResult.Success(200, "123", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());

        var step = new GetSerialNumberFromServerStep(
            service,
            NullLogger.Instance,
            "http://SERVER_BASE_URL",
            "PSW+UPS-Box 8x2Pro",
            "Dut.cpu_id",
            1000,
            0,
            0,
            "SerialNumber",
            failOnError: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.Equal(0, service.Calls);
        Assert.False(context.GetVariable<bool>("SerialNumberReceived"));
        Assert.Contains("ServerBaseUrl", context.GetVariable<string>("SerialNumberError"));
    }

    [Fact]
    public async Task GetSerialNumberNode_UsesSettingsServerForPlaceholder()
    {
        var previousServerBaseUrl = AppSettings.Instance.ServerBaseUrl;

        try
        {
            AppSettings.Instance.ServerBaseUrl = "serial-server.local";
            var service = new CapturingHttpService(HttpRequestResult.Success(200, "777", TimeSpan.FromMilliseconds(1)));
            var context = new TestContext(new RegisterState());
            context.SetVariable("Dut.cpu_id", "CPU-777");
            var node = new GetSerialNumberFromServerNodeViewModel
            {
                ServerBaseUrl = "http://SERVER_BASE_URL",
                RetryCount = 0
            };

            var result = await node
                .CreateStep(service, NullLogger.Instance)
                .ExecuteAsync(context, CancellationToken.None);

            Assert.Equal(StepResult.True, result);
            Assert.Equal(777, context.GetVariable<int>("SerialNumber"));
            Assert.Equal(
                "http://serial-server.local/api/api.svc/getSerialNum?devType=PSW%2BUPS-Box%208x2Pro&cpuId=CPU-777",
                service.LastUrl);
        }
        finally
        {
            AppSettings.Instance.ServerBaseUrl = previousServerBaseUrl;
        }
    }

    [Fact]
    public async Task GetSerialNumberNode_ExecutesRealHttpRequestServicePipeline()
    {
        var handler = new RecordingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("3200004")
            });
        using var client = new HttpClient(handler);
        using var service = new HttpRequestService(client);
        var context = new TestContext(new RegisterState());
        context.SetVariable("Dut.cpu_id", "CPU 4");
        var node = new GetSerialNumberFromServerNodeViewModel
        {
            ServerBaseUrl = "http://serial-server.local/api/Api.svc",
            RetryCount = 0
        };

        var result = await node
            .CreateStep(service, NullLogger.Instance)
            .ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(3200004, context.GetVariable<int>("SerialNumber"));
        Assert.Equal(HttpMethod.Get, handler.LastRequest?.Method);
        Assert.Equal(
            "http://serial-server.local/api/Api.svc/getSerialNum?devType=PSW%2BUPS-Box%208x2Pro&cpuId=CPU%204",
            handler.LastRequest?.RequestUri?.AbsoluteUri);
    }

    private static GetSerialNumberFromServerStep CreateSerialStep(
        IHttpRequestService service,
        string serverUrl = "http://server",
        string cpuIdVariableName = "Dut.cpu_id") =>
        new(
            service,
            NullLogger.Instance,
            serverUrl,
            "PSW+UPS-Box 8x2Pro",
            cpuIdVariableName,
            1000,
            0,
            0,
            "SerialNumber",
            failOnError: true);

    [Fact]
    public async Task GetUpsStatusStep_ParsesIntegerResponse()
    {
        var service = new CapturingHttpService(HttpRequestResult.Success(200, "1", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        var step = new GetUpsStatusStep(service, NullLogger.Instance, "http://192.168.0.1", 1000, "Dut.ups_rez", true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(1, context.GetVariable<int>("Dut.ups_rez"));
        Assert.Equal("http://192.168.0.1/api/getUpsStatus", service.LastUrl);
    }

    [Fact]
    public async Task GetIrpStatusStep_UsesOfficialUppercaseEndpoint()
    {
        var service = new QueueHttpService(
            HttpRequestResult.Success(200, "1", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        var step = new GetIrpStatusStep(
            service,
            NullLogger.Instance,
            "http://192.168.0.1/",
            1000,
            "Dut.ups_det",
            true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(1, context.GetVariable<int>("Dut.ups_det"));
        Assert.Equal(new[] { "http://192.168.0.1/api/isUPS" }, service.RequestedUrls);
        Assert.Equal("http://192.168.0.1/api/isUPS", context.GetVariable<string>("GetIrpStatus.Url"));
        Assert.Equal(200, context.GetVariable<int>("GetIrpStatus.StatusCode"));
        Assert.Equal(1, context.GetVariable<int>("GetIrpStatus.Attempts"));
        Assert.True(context.GetVariable<bool>("GetIrpStatus.Success"));
    }

    [Fact]
    public async Task GetIrpStatusStep_FallsBackToLegacyEndpointAfter404()
    {
        var service = new QueueHttpService(
            HttpRequestResult.Success(
                404,
                "<h1>Not Found</h1><p>The requested URL /api/isUPS was not found on this server.</p>",
                TimeSpan.FromMilliseconds(1)),
            HttpRequestResult.Success(200, "0", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        var step = new GetIrpStatusStep(
            service,
            NullLogger.Instance,
            "http://192.168.0.1",
            1000,
            "Dut.ups_det",
            true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(0, context.GetVariable<int>("Dut.ups_det"));
        Assert.Equal(
            new[]
            {
                "http://192.168.0.1/api/isUPS",
                "http://192.168.0.1/api/isUps"
            },
            service.RequestedUrls);
        Assert.Equal("http://192.168.0.1/api/isUps", context.GetVariable<string>("GetIrpStatus.Url"));
        Assert.Equal(2, context.GetVariable<int>("GetIrpStatus.Attempts"));
    }

    [Fact]
    public async Task GetIrpStatusStep_RemovesStaleOutputWhenBothEndpointsAreMissing()
    {
        var notFound = HttpRequestResult.Success(
            404,
            "<h1>Not Found</h1><p>The requested URL was not found on this server.</p>",
            TimeSpan.FromMilliseconds(1));
        var service = new QueueHttpService(notFound, notFound);
        var context = new TestContext(new RegisterState());
        context.SetVariable("Dut.ups_det", 1);
        var step = new GetIrpStatusStep(
            service,
            NullLogger.Instance,
            "http://192.168.0.1",
            1000,
            "Dut.ups_det",
            true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.False(context.Variables.ContainsKey("Dut.ups_det"));
        Assert.False(context.GetVariable<bool>("GetIrpStatus.Success"));
        Assert.Equal(2, context.GetVariable<int>("GetIrpStatus.Attempts"));
        Assert.Contains("/api/isUPS", context.GetVariable<string>("GetIrpStatus.Error"));
        Assert.Contains("/api/isUps", context.GetVariable<string>("GetIrpStatus.Error"));
    }

    [Fact]
    public async Task WaitVariableUntilStep_GetIrpStatusUsesCompatibleEndpointFallback()
    {
        var service = new QueueHttpService(
            HttpRequestResult.Success(404, "<h1>Not Found</h1>", TimeSpan.FromMilliseconds(1)),
            HttpRequestResult.Success(200, "1", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        var step = new WaitVariableUntilStep(
            service,
            NullLogger.Instance,
            "Dut.ups_det",
            "1",
            VariableComparisonType.Number,
            "GetIrpStatus",
            "http://192.168.0.1",
            "/api/isUPS",
            HttpResponseValueType.Integer,
            1000,
            1000,
            10,
            true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(1, context.GetVariable<int>("Dut.ups_det"));
        Assert.Equal(1, context.GetVariable<int>("WaitVariable.Attempts"));
        Assert.Equal(2, service.RequestedUrls.Count);
        Assert.Equal("http://192.168.0.1/api/isUps", context.GetVariable<string>("WaitVariable.Url"));
    }

    [Fact]
    public async Task ReadHttpVariableStep_ReplacesStaleValueWithParsedInteger()
    {
        var service = new QueueHttpService(
            HttpRequestResult.Success(200, "1", TimeSpan.FromMilliseconds(3)));
        var context = new TestContext(new RegisterState());
        context.SetVariable("Dut.ups_rez", 99);
        var step = new ReadHttpVariableStep(
            service,
            NullLogger.Instance,
            "http://192.168.0.1/",
            "/api/getUpsStatus",
            HttpResponseValueType.Integer,
            1000,
            "Dut.ups_rez",
            true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(1, context.GetVariable<int>("Dut.ups_rez"));
        Assert.Equal("http://192.168.0.1/api/getUpsStatus", service.RequestedUrls.Single());
        Assert.True(context.GetVariable<bool>("HttpRead.Success"));
        Assert.Equal("Integer", context.GetVariable<string>("HttpRead.ResponseType"));
        Assert.Equal(200, context.GetVariable<int>("HttpRead.StatusCode"));
    }

    [Fact]
    public async Task ReadHttpVariableStep_RemovesStaleValueOnParseFailure()
    {
        var service = new QueueHttpService(
            HttpRequestResult.Success(200, "<html>not a number</html>", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        context.SetVariable("Dut.akb_voltage", 24.0);
        var step = new ReadHttpVariableStep(
            service,
            NullLogger.Instance,
            "http://192.168.0.1",
            "/api/getUpsVoltage",
            HttpResponseValueType.Number,
            1000,
            "Dut.akb_voltage",
            true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.False(context.Variables.ContainsKey("Dut.akb_voltage"));
        Assert.False(context.GetVariable<bool>("HttpRead.Success"));
        Assert.Contains("Number", context.GetVariable<string>("HttpRead.Error"));
    }

    [Fact]
    public async Task WaitVariableUntilStep_HttpGetPollsFreshValuesUntilExpected()
    {
        var service = new QueueHttpService(
            HttpRequestResult.Success(200, "0", TimeSpan.FromMilliseconds(1)),
            HttpRequestResult.Success(200, "1", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        context.SetVariable("Dut.ups_rez", 1);
        var step = new WaitVariableUntilStep(
            service,
            NullLogger.Instance,
            "Dut.ups_rez",
            "1",
            VariableComparisonType.Number,
            "HttpGet",
            "http://192.168.0.1",
            "/api/getUpsStatus",
            HttpResponseValueType.Integer,
            1000,
            1000,
            1,
            true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(2, service.RequestedUrls.Count);
        Assert.Equal(2, context.GetVariable<int>("WaitVariable.Attempts"));
        Assert.Equal(1, context.GetVariable<int>("Dut.ups_rez"));
        Assert.True(context.GetVariable<bool>("WaitVariable.Success"));
        Assert.Equal("http://192.168.0.1/api/getUpsStatus", context.GetVariable<string>("WaitVariable.Url"));
    }

    [Fact]
    public async Task WaitVariableUntilStep_HttpGetRetriesWhileDutIsBooting()
    {
        var service = new QueueHttpService(
            HttpRequestResult.Failure("DUT unavailable", TimeSpan.FromMilliseconds(1)),
            HttpRequestResult.Success(200, "1", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        var step = new WaitVariableUntilStep(
            service,
            NullLogger.Instance,
            "Dut.ups_rez",
            "1",
            VariableComparisonType.Number,
            "HttpGet",
            "http://192.168.0.1",
            "/api/getUpsStatus",
            HttpResponseValueType.Integer,
            1000,
            1000,
            1,
            true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(2, service.RequestedUrls.Count);
        Assert.Equal(2, context.GetVariable<int>("WaitVariable.Attempts"));
        Assert.Equal(1, context.GetVariable<int>("Dut.ups_rez"));
    }

    [Fact]
    public async Task WaitVariableUntilStep_HttpReachableRetriesUntilEndpointReturns2xx()
    {
        var service = new QueueHttpService(
            HttpRequestResult.Success(503, "starting", TimeSpan.FromMilliseconds(1)),
            HttpRequestResult.Success(200, "<html>ready</html>", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        var step = new WaitVariableUntilStep(
            service,
            NullLogger.Instance,
            "Dut.http_ready",
            "true",
            VariableComparisonType.Boolean,
            "HttpReachable",
            "http://192.168.0.1",
            "/",
            HttpResponseValueType.Boolean,
            1000,
            1000,
            1,
            true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(2, service.RequestedUrls.Count);
        Assert.Equal(2, context.GetVariable<int>("WaitVariable.Attempts"));
        Assert.True(context.GetVariable<bool>("Dut.http_ready"));
        Assert.Equal(200, context.GetVariable<int>("WaitVariable.StatusCode"));
        Assert.True(context.GetVariable<bool>("WaitVariable.Success"));
    }

    [Fact]
    public async Task WaitVariableUntilStep_HttpGetDoesNotPassOnStaleExpectedValue()
    {
        var service = new CapturingHttpService(
            HttpRequestResult.Failure("DUT unavailable", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        context.SetVariable("Dut.ups_rez", 1);
        var step = new WaitVariableUntilStep(
            service,
            NullLogger.Instance,
            "Dut.ups_rez",
            "1",
            VariableComparisonType.Number,
            "HttpGet",
            "http://192.168.0.1",
            "/api/getUpsStatus",
            HttpResponseValueType.Integer,
            1000,
            5,
            1,
            true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.False(context.Variables.ContainsKey("Dut.ups_rez"));
        Assert.False(context.GetVariable<bool>("WaitVariable.Passed"));
        Assert.Contains("DUT unavailable", context.GetVariable<string>("WaitVariable.Error"));
        Assert.True(service.Calls >= 1);
    }

    [Fact]
    public async Task WaitVariableUntilStep_SelftestSnapshotRefreshesPageUntilExpectedValue()
    {
        static string Snapshot(int upsResult) => $"""
            <selftest>
              <default_mac>00:11:22:33:44:55</default_mac>
              <init_ok>1</init_ok>
              <dev_type>0</dev_type>
              <firmvare_vers>1119</firmvare_vers>
              <boot_vers>0</boot_vers>
              <ups_rez>{upsResult}</ups_rez>
            </selftest>
            """;

        var service = new QueueHttpService(
            HttpRequestResult.Success(200, Snapshot(0), TimeSpan.FromMilliseconds(1)),
            HttpRequestResult.Success(200, Snapshot(1), TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        context.SetVariable("Dut.ups_rez", 1);
        var step = new WaitVariableUntilStep(
            service,
            NullLogger.Instance,
            "Dut.ups_rez",
            "1",
            VariableComparisonType.Number,
            "SelftestSnapshot",
            "http://192.168.0.1",
            "/cgi-bin/luci/admin/statistics/deviceinfo?luci_username=admin&luci_password=admin",
            HttpResponseValueType.String,
            1000,
            1000,
            1,
            true,
            useBrowserForSelftest: false);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(2, service.RequestedUrls.Count);
        Assert.Equal(2, context.GetVariable<int>("WaitVariable.Attempts"));
        Assert.Equal("1", context.GetVariable<string>("Dut.ups_rez"));
        Assert.True(context.GetVariable<bool>("WaitVariable.Passed"));
    }

    [Fact]
    public async Task WaitVariableUntilStep_SelftestSnapshotWaitsUntilNumberEntersRange()
    {
        static string Snapshot(string batteryVoltage) => $"""
            <selftest>
              <default_mac>00:11:22:33:44:55</default_mac>
              <init_ok>1</init_ok>
              <dev_type>0</dev_type>
              <firmvare_vers>1119</firmvare_vers>
              <boot_vers>0</boot_vers>
              <akb_voltage>{batteryVoltage}</akb_voltage>
            </selftest>
            """;

        var service = new QueueHttpService(
            HttpRequestResult.Success(200, Snapshot("19.9"), TimeSpan.FromMilliseconds(1)),
            HttpRequestResult.Success(200, Snapshot("24.5"), TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        var step = new WaitVariableUntilStep(
            service,
            NullLogger.Instance,
            "Dut.akb_voltage",
            "20..27",
            VariableComparisonType.Number,
            "SelftestSnapshot",
            "http://192.168.0.1",
            "/cgi-bin/luci/admin/statistics/deviceinfo?luci_username=admin&luci_password=admin",
            HttpResponseValueType.String,
            1000,
            1000,
            1,
            true,
            useBrowserForSelftest: false);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(2, service.RequestedUrls.Count);
        Assert.Equal(2, context.GetVariable<int>("WaitVariable.Attempts"));
        Assert.Equal("24.5", context.GetVariable<string>("Dut.akb_voltage"));
        Assert.True(context.GetVariable<bool>("WaitVariable.Passed"));
    }

    [Fact]
    public async Task GetUpsVoltageStep_ParsesDoubleResponse()
    {
        var service = new CapturingHttpService(HttpRequestResult.Success(200, "24.7", TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        var step = new GetUpsVoltageStep(service, NullLogger.Instance, "http://192.168.0.1", 1000, "Dut.akb_voltage", true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(24.7, context.GetVariable<double>("Dut.akb_voltage"));
        Assert.Equal("http://192.168.0.1/api/getUpsVoltage", service.LastUrl);
    }

    [Fact]
    public void SendUdpSetMacPacketStep_BuildsLegacyPacket()
    {
        Assert.True(SendUdpSetMacPacketStep.TryParseMac("AA:BB:CC:DD:EE:FF", out var mac, out var normalized));

        var packet = SendUdpSetMacPacketStep.BuildPacket(mac);

        Assert.Equal("AA:BB:CC:DD:EE:FF", normalized);
        Assert.Equal(21, packet.Length);
        Assert.Equal("434F4E464947000000006D77AABBCCDDEEFF4B7232", Convert.ToHexString(packet));
    }

    [Fact]
    public void RunDataTestStep_BuildsLegacyEthernetUdpPacket()
    {
        var sourceMac = new byte[] { 0x10, 0xFF, 0xE0, 0x68, 0xFE, 0x24 };
        var destinationMac = new byte[] { 0x00, 0xFF, 0x03, 0x0A, 0xDC, 0x84 };

        var packet = RunDataTestStep.BuildPacket(
            sourceMac,
            destinationMac,
            IPAddress.Parse("192.168.0.3"),
            IPAddress.Parse("192.168.0.2"),
            1514,
            43962);

        Assert.Equal(1514, packet.Length);
        Assert.Equal("00FF030ADC84", Convert.ToHexString(packet[0..6]));
        Assert.Equal("10FFE068FE24", Convert.ToHexString(packet[6..12]));
        Assert.Equal("0800", Convert.ToHexString(packet[12..14]));
        Assert.Equal(0x45, packet[14]);
        Assert.Equal("05DC", Convert.ToHexString(packet[16..18]));
        Assert.Equal(17, packet[23]);
        Assert.Equal("C0A80003", Convert.ToHexString(packet[26..30]));
        Assert.Equal("C0A80002", Convert.ToHexString(packet[30..34]));
        Assert.Equal("ABBAABBA05C80000", Convert.ToHexString(packet[34..42]));
        Assert.All(packet[42..], value => Assert.Equal(0x41, value));
    }

    [Fact]
    public void RunDataTestStep_Calculates100MbitPacketCount()
    {
        var packets = RunDataTestStep.CalculateExpectedPackets(100, 1514, 5000);

        Assert.Equal(40637, packets);
    }

    [Fact]
    public void RunDataTestStep_Calculates1GbitPacketCount()
    {
        var packets = RunDataTestStep.CalculateExpectedPackets(1000, 1514, 5000);

        Assert.Equal(406372, packets);
    }

    [Fact]
    public void RunDataTestStep_AccountsForEthernetWireOverhead()
    {
        Assert.Equal(1538, RunDataTestStep.CalculateWireSizeBytes(1514));
    }

    [Fact]
    public void RunDataTestStep_CalculatesGeneratorDeficitFromActualWireSpeed()
    {
        Assert.Equal(36.5, RunDataTestStep.CalculateTxDeficitPercent(100, 63.5), precision: 3);
    }

    [Fact]
    public void RunDataTestStep_RecognizesOnlyCurrentNumberedProbe()
    {
        var packet = RunDataTestStep.BuildPacket(
            new byte[] { 0x10, 0xFF, 0xE0, 0x68, 0xFE, 0x24 },
            new byte[] { 0x00, 0xFF, 0x03, 0x0A, 0xDC, 0x84 },
            IPAddress.Parse("192.168.0.3"),
            IPAddress.Parse("192.168.0.2"),
            1514,
            43962);

        RunDataTestStep.WriteProbeIdentity(packet, 123456789UL, 42);

        Assert.True(RunDataTestStep.TryReadProbeSequence(packet, 123456789UL, 100, out var sequence));
        Assert.Equal(42, sequence);
        Assert.False(RunDataTestStep.TryReadProbeSequence(packet, 987654321UL, 100, out _));
        Assert.False(RunDataTestStep.TryReadProbeSequence(packet, 123456789UL, 40, out _));
    }

    [Fact]
    public async Task RunDataTestStep_RejectsUnsupportedModeBeforeOpeningPcap()
    {
        var context = new TestContext(new RegisterState());
        var step = new RunDataTestStep(
            NullLogger.Instance,
            "Bercut",
            10000,
            1514,
            43962,
            15000,
            100,
            5000,
            500,
            5000,
            1.0,
            2.0,
            true,
            new[] { new DataTestPortConfig("port0-1", "192.168.0.2", "192.168.0.3") },
            "DataTest",
            failOnError: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.False(context.GetVariable<bool>("DataTest.Passed"));
        Assert.Contains("SoftwarePcap", context.GetVariable<string>("DataTest.Error"));
    }

    private sealed class CapturingHttpService : IHttpRequestService
    {
        private readonly HttpRequestResult _result;

        public CapturingHttpService(HttpRequestResult result)
        {
            _result = result;
        }

        public string LastUrl { get; private set; } = string.Empty;
        public int Calls { get; private set; }

        public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls++;
            LastUrl = url;
            return Task.FromResult(_result);
        }
    }

    private sealed class QueueHttpService : IHttpRequestService
    {
        private readonly Queue<HttpRequestResult> _results;

        public QueueHttpService(params HttpRequestResult[] results)
        {
            _results = new Queue<HttpRequestResult>(results);
        }

        public List<string> RequestedUrls { get; } = new();

        public Task<HttpRequestResult> GetAsync(
            string url,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            RequestedUrls.Add(url);
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public RecordingHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_response);
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AppSettingsTestCollection
{
    public const string Name = "AppSettings-dependent tests";
}
