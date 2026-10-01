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
    public async Task GetSerialNumberFromServerStep_FixedDebugSerialSkipsServerRequest()
    {
        var service = new CapturingHttpService(
            HttpRequestResult.Failure("HTTP request must not run", TimeSpan.Zero));
        var context = new TestContext(new RegisterState());
        context.SetVariable("Dut.cpu_id", "CPU-DEBUG");
        var step = new GetSerialNumberFromServerStep(
            service,
            NullLogger.Instance,
            string.Empty,
            "PSW+UPS-Box 8x2Pro",
            "Dut.cpu_id",
            1000,
            1,
            0,
            "SerialNumber",
            failOnError: true,
            fixedSerialNumber: 3200428);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(0, service.Calls);
        Assert.Equal(3200428, context.GetVariable<int>("SerialNumber"));
        Assert.Equal(3200428, context.GetVariable<int>("NetTest.SerialNumber"));
        Assert.Equal("FixedDebug", context.GetVariable<string>("SerialNumberSource"));
        Assert.Equal("CPU-DEBUG", context.GetVariable<string>("SerialNumberCpuId"));
        Assert.Equal(string.Empty, context.GetVariable<string>("SerialNumberRequestUrl"));
        Assert.Equal(0, context.GetVariable<int>("SerialNumberAttempts"));
        Assert.Equal(0, context.GetVariable<int>("SerialNumberStatusCode"));
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
    public async Task BuildTestReportStep_BuildsOriginalQtstandTextFormat()
    {
        var context = new TestContext(new RegisterState());
        context.SetVariable("SerialNumber", 3200123);
        context.SetVariable("SerialShort", 123);
        context.SetVariable("Dut.akb_voltage", 24.5);
        context.SetVariable("LastCheck.Passed", false);
        context.AddReportEntry("самотестирование", true, "true");

        var step = new BuildTestReportStep(
            NullLogger.Instance,
            "TestReportText",
            "APK03-01",
            "SerialNumber",
            "session-42",
            "production",
            includeAllVariables: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);
        var report = context.GetVariable<string>("TestReportText");

        Assert.Equal(StepResult.True, result);
        Assert.NotNull(report);
        Assert.StartsWith(
            "test_result=true=1\r\n" +
            "stand_id=true=APK03-01\r\n" +
            "serial_num=true=3200123\r\n" +
            "session=true=session-42\r\n" +
            "Тип проверки=true=production\r\n",
            report);
        Assert.Contains("самотестирование=true=true\r\n", report);
        Assert.Contains("Dut.akb_voltage=true=24.5\r\n", report);
        Assert.Contains("LastCheck.Passed=true=false\r\n", report);
        Assert.DoesNotContain("serial_num=true=123\r\n", report);
        Assert.True(context.GetVariable<bool>("BuildReport.Success"));
    }

    [Fact]
    public async Task BuildTestReportStep_RejectsMissingStandIdAndStaleReport()
    {
        var context = new TestContext(new RegisterState());
        context.SetVariable("SerialNumber", 3200123);
        context.SetVariable("TestReportText", "stale");
        var step = new BuildTestReportStep(
            NullLogger.Instance,
            "TestReportText",
            string.Empty,
            "SerialNumber",
            string.Empty,
            "production",
            includeAllVariables: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.False(context.Variables.ContainsKey("TestReportText"));
        Assert.False(context.GetVariable<bool>("BuildReport.Success"));
        Assert.Contains("Stand ID", context.GetVariable<string>("BuildReport.Error"));
    }

    [Fact]
    public async Task SendTestReportStep_UsesOriginalQtstandMultipartFields()
    {
        var handler = new RecordingReportHandler(HttpStatusCode.OK, "Ok saved");
        var context = new TestContext(new RegisterState());
        context.SetVariable("TestReportText", "test_result=true=1\r\nserial_num=true=3200123\r\n");
        var step = new SendTestReportStep(
            NullLogger.Instance,
            "http://report-server.local",
            "TestReportText",
            "/api/Api.svc/result.json",
            1000,
            0,
            0,
            false,
            "reports",
            true,
            () => new HttpClient(handler, disposeHandler: false));

        var result = await step.ExecuteAsync(context, CancellationToken.None);
        var multipart = handler.Body.Replace("\"", string.Empty, StringComparison.Ordinal);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("http://report-server.local/api/Api.svc/result.json", handler.Url);
        const string boundary = "---------------------------723690991551375881941828858";
        Assert.Equal($"multipart/form-data; boundary={boundary}", handler.ContentType);
        Assert.Contains("name=action", multipart);
        Assert.Contains("name=updatefile; filename=result.json", multipart);
        Assert.Contains("Content-Type: application/octet-stream", multipart);
        Assert.Contains("test_result=true=1\r\nserial_num=true=3200123\r\n", handler.Body);
        Assert.Contains("name=result; filename=result.json", multipart);
        Assert.DoesNotContain("name=file", multipart);
        Assert.Equal(
            $"--{boundary}\r\n" +
            "Content-Disposition: form-data; name=\"action\"\r\n\r\n" +
            "\r\n" +
            $"--{boundary}\r\n" +
            "Content-Disposition: form-data; name=\"updatefile\"; filename=\"result.json\"\r\n" +
            "Content-Type: application/octet-stream;\r\n\r\n" +
            "test_result=true=1\r\nserial_num=true=3200123\r\n" +
            "\r\n" +
            $"--{boundary}\r\n" +
            $"--{boundary}\r\n" +
            "Content-Disposition: form-data; name=\"result\"; filename=\"result.json\"\r\n" +
            "\r\n",
            handler.Body);
        Assert.True(context.GetVariable<bool>("SendReport.Success"));
        Assert.Equal("Ok saved", context.GetVariable<string>("SendReport.RawResponse"));
        Assert.Equal("Sent", context.GetVariable<string>("ReportDelivery.Status"));
    }

    [Fact]
    public async Task SendTestReportStep_RetriesOnceAndAcceptsOnlyOkResponse()
    {
        var handler = new SequencedReportHandler(
            (HttpStatusCode.InternalServerError, "failed"),
            (HttpStatusCode.OK, "Ok saved"));
        var context = new TestContext(new RegisterState());
        context.SetVariable("TestReportText", "test_result=true=1\r\n");
        var step = new SendTestReportStep(
            NullLogger.Instance,
            "http://report-server.local",
            "TestReportText",
            "/api/Api.svc/result.json",
            1000,
            1,
            0,
            false,
            "reports",
            true,
            () => new HttpClient(handler, disposeHandler: false));

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(2, handler.Calls);
        Assert.Equal(2, context.GetVariable<int>("SendReport.Attempts"));
        Assert.Equal(200, context.GetVariable<int>("SendReport.StatusCode"));
        Assert.Equal("Ok saved", context.GetVariable<string>("SendReport.RawResponse"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReportDeliveryFailurePreservesLocalReportWithoutRejectingDevice(bool failOnError)
    {
        var directory = Path.Combine(Path.GetTempPath(), "TestBuilder-report-" + Guid.NewGuid());
        try
        {
            var handler = new SequencedReportHandler((HttpStatusCode.ServiceUnavailable, "down"), (HttpStatusCode.OK, "not accepted"));
            var context = new TestContext(new RegisterState());
            const string report = "test_result=true=1\r\nserial_num=true=612447\r\n";
            context.SetVariable("TestReportText", report);
            var step = new SendTestReportStep(NullLogger.Instance, "http://report-server.local", "TestReportText",
                "/api/Api.svc/result.json", 1000, 1, 0, true, directory, failOnError,
                () => new HttpClient(handler, disposeHandler: false));
            Assert.Equal(failOnError ? StepResult.False : StepResult.True, await step.ExecuteAsync(context, CancellationToken.None));
            Assert.Equal(2, handler.Calls);
            Assert.Equal("Failed", context.GetVariable<string>("ReportDelivery.Status"));
            Assert.False(context.GetVariable<bool>("SendReport.Success"));
            Assert.False(context.HasCriticalError);
            Assert.Empty(context.ReportEntries);
            Assert.Equal(report, await File.ReadAllTextAsync(context.GetVariable<string>("SendReport.LocalPath")!));
            Assert.Contains("НЕ отправлен", context.GetVariable<string>("ReportDelivery.Message"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ReportLocalWriteFailureIsReportedWithoutPostingOrThrowing()
    {
        var path = Path.GetTempFileName();
        try
        {
            var handler = new RecordingReportHandler(HttpStatusCode.OK, "Ok");
            var context = new TestContext(new RegisterState());
            context.SetVariable("TestReportText", "test_result=true=1\r\n");
            var step = new SendTestReportStep(NullLogger.Instance, "http://report-server.local", "TestReportText",
                "/api/Api.svc/result.json", 1000, 0, 0, true, path, true,
                () => new HttpClient(handler, disposeHandler: false));
            Assert.Equal(StepResult.False, await step.ExecuteAsync(context, CancellationToken.None));
            Assert.Equal("Failed", context.GetVariable<string>("ReportDelivery.Status"));
            Assert.Equal(0, context.GetVariable<int>("SendReport.Attempts"));
            Assert.False(context.HasCriticalError);
            Assert.Contains("не сохранена", context.GetVariable<string>("ReportDelivery.Message"));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0, "6", "CPU-test", "612447", 1, true)]
    [InlineData(0, "6", "CPU-test", "600000", 1, true)]
    [InlineData(0, "6", "CPU-test", "665535", 1, true)]
    [InlineData(612447, "6", "CPU-test", "665536", 0, true)]
    [InlineData(0, "32", "CPU-test", "612447", 0, false)]
    [InlineData(0, "6", "", "612447", 0, false)]
    [InlineData(0, "6", "CPU-test", "599999", 1, false)]
    [InlineData(0, "6", "CPU-test", "665536", 1, false)]
    [InlineData(0, "6", "CPU-test", "CPU-test", 1, false)]
    public async Task RealFailureReportGraphRecoversSerialOnlyForIdentifiedDevice(
        int existingSerial, string model, string cpu, string reply, int calls, bool buildsReport)
    {
        var previousStandId = AppSettings.Instance.StandId;
        try
        {
            AppSettings.Instance.StandId = "TEST-STAND";
            using var modbus = new TestBuilder.Services.Modbus.ModbusService();
            var vm = new TestBuilder.ViewModels.TestViewModel(modbus, new TestBuilder.Domain.Modbus.SlaveManager(modbus));
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "profiles", "PSW_2G6F_plus_full_algorithm.json"));
            GraphSerializer.Deserialize(File.ReadAllText(path), vm);
            var recovery = vm.RootGraph.Nodes.OfType<SubtestNodeViewModel>()
                .Single(n => n.RunOnFailure && n.BodyGraph.Nodes.OfType<BuildTestReportNodeViewModel>().Any());
            recovery.BodyGraph.Nodes.OfType<GetSerialNumberFromServerNodeViewModel>().Single().RetryCount = 0;
            var service = new CapturingHttpService(HttpRequestResult.Success(200, reply, TimeSpan.Zero));
            var graph = new GraphCompiler(modbus, service, NullLogger.Instance).Compile(recovery.BodyGraph);
            var prompted = false;
            var context = new TestContext(new RegisterState()) { HasCriticalError = true,
                OperatorPrompt = _ => { prompted = true; return Task.FromResult(false); } };
            context.SetVariable("Dut.dev_type", model);
            context.SetVariable("Dut.cpu_id", cpu);
            if (existingSerial > 0) context.SetVariable("SerialNumber", existingSerial);
            Assert.Equal(ExecutionStatus.Completed, await new TestExecutor().ExecuteAsync(graph.StartNode, context, CancellationToken.None));
            Assert.Equal(calls, service.Calls);
            Assert.Equal(buildsReport, prompted);
            Assert.Equal(buildsReport, context.GetVariable<bool>("BuildReport.Success"));
            Assert.False(context.Variables.ContainsKey("SendReport.Attempts")); // operator declined: no POST
            if (buildsReport)
            {
                var report = context.GetVariable<string>("TestReportText")!;
                Assert.Contains("test_result=true=0", report);
                Assert.DoesNotContain("serial_num=true=CPU-test", report);
                Assert.Equal("Pending", context.GetVariable<string>("ReportDelivery.Status"));
            }
        }
        finally { AppSettings.Instance.StandId = previousStandId; }
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

    [Theory]
    [InlineData("sensor_1")]
    [InlineData("sensor_2")]
    public async Task WaitVariableUntilStep_SelftestSnapshotAcceptsHexFirmwareWhileWaitingForSensor(string sensorName)
    {
        string Snapshot(int sensorValue) => $"""
            <selftest>
              <default_mac>c0:11:a6:05:00:00</default_mac>
              <init_ok>1</init_ok>
              <dev_type>6</dev_type>
              <firmvare_vers>20c</firmvare_vers>
              <boot_vers>10a</boot_vers>
              <{sensorName}>{sensorValue}</{sensorName}>
            </selftest>
            """;

        var service = new QueueHttpService(
            HttpRequestResult.Success(200, Snapshot(0), TimeSpan.FromMilliseconds(1)),
            HttpRequestResult.Success(200, Snapshot(1), TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        context.SetVariable($"Dut.{sensorName}", "1");
        var step = new WaitVariableUntilStep(
            service,
            NullLogger.Instance,
            $"Dut.{sensorName}",
            "1",
            VariableComparisonType.Number,
            "SelftestSnapshot",
            "http://192.168.0.1",
            "/test.shtml",
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
        Assert.Equal("1", context.GetVariable<string>($"Dut.{sensorName}"));
        Assert.Equal("20c", context.GetVariable<string>("Dut.firmvare_vers"));
        Assert.Equal(2, context.GetVariable<int>("SelfTest.CheckedRuleCount"));
        Assert.True(context.GetVariable<bool>("WaitVariable.Passed"));
    }

    [Fact]
    public async Task WaitVariableUntilStep_SelftestSnapshotRejectsInvalidInitializationDespiteMatchingSensor()
    {
        const string snapshot = """
            <selftest>
              <default_mac>c0:11:a6:05:00:00</default_mac>
              <init_ok>0</init_ok>
              <dev_type>6</dev_type>
              <firmvare_vers>20c</firmvare_vers>
              <sensor_1>1</sensor_1>
            </selftest>
            """;
        var service = new CapturingHttpService(
            HttpRequestResult.Success(200, snapshot, TimeSpan.FromMilliseconds(1)));
        var context = new TestContext(new RegisterState());
        var step = new WaitVariableUntilStep(
            service,
            NullLogger.Instance,
            "Dut.sensor_1",
            "1",
            VariableComparisonType.Number,
            "SelftestSnapshot",
            "http://192.168.0.1",
            "/test.shtml",
            HttpResponseValueType.String,
            1000,
            25,
            10,
            true,
            useBrowserForSelftest: false);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.True(service.Calls > 0);
        Assert.Equal(1, context.GetVariable<int>("SelfTest.FailedRuleCount"));
        Assert.False(context.GetVariable<bool>("WaitVariable.Passed"));
        Assert.Contains("init_ok", context.GetVariable<string>("WaitVariable.Error"));
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

    [Theory]
    [InlineData(false, 100)]
    [InlineData(true, 1000)]
    public async Task RunDataTestStep_RuntimeUsesExplicitGigabitLimit(bool allowGigabit, int limit)
    {
        var context = new TestContext(new RegisterState());
        // An unsupported mode exits before any NIC/pcap access.
        var step = new RunDataTestStep(NullLogger.Instance, "Unsupported", 10000, 1514,
            43962, 15000, 1000, 5000, 500, 5000, 1, 2, true,
            new[] { new DataTestPortConfig("sfp", "192.168.0.8", "192.168.0.9", 1000) },
            "DataTest", true, allowGigabit);
        Assert.Equal(StepResult.False, await step.ExecuteAsync(context, CancellationToken.None));
        Assert.Equal(limit, context.GetVariable<int>("DataTest.TargetBandwidthMbps"));
        Assert.Equal(limit, context.GetVariable<int>("DataTest.BandwidthLimitMbps"));
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

    private sealed class RecordingReportHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public RecordingReportHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        public HttpMethod? Method { get; private set; }
        public string Url { get; private set; } = string.Empty;
        public string ContentType { get; private set; } = string.Empty;
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Url = request.RequestUri?.AbsoluteUri ?? string.Empty;
            ContentType = request.Content?.Headers.ContentType?.ToString() ?? string.Empty;
            Body = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody)
            };
        }
    }

    private sealed class SequencedReportHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode StatusCode, string Body)> _responses;

        public SequencedReportHandler(params (HttpStatusCode StatusCode, string Body)[] responses)
        {
            _responses = new Queue<(HttpStatusCode StatusCode, string Body)>(responses);
        }

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            var response = _responses.Dequeue();
            return Task.FromResult(new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Body)
            });
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AppSettingsTestCollection
{
    public const string Name = "AppSettings-dependent tests";
}
