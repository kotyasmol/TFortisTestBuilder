using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Services.Graph;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.StepTests;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

[Collection(AppSettingsTestCollection.Name)]
public class SwitchQualificationProfileTests
{
    // Independent expectations: catalog series and port count, not values read back from generated files.
    public static IEnumerable<object[]> Models()
    {
        yield return ["PSW-1G4F", 4, 4, 4, 1];
        yield return ["PSW-2G4F", 5, 5, 4, 2];
        yield return ["PSW-2G6F+", 6, 6, 6, 2];
        yield return ["PSW-2G8F+", 7, 7, 8, 2];
        yield return ["PSW-2G4F-Box", 21, 5, 4, 2];
        yield return ["PSW-2G6F+Box", 25, 6, 6, 2];
        yield return ["PSW-2G8F+Box", 26, 7, 8, 2];
        yield return ["PSW-2G8F+Ex", 28, 7, 8, 2];
        yield return ["PSW+UPS-Box 8x2Pro", 32, 0, 8, 2];
        yield return ["PSW+Box 8x2Pro", 33, 0, 8, 2];
    }

    public static IEnumerable<object[]> ReportCases() => Models().SelectMany(model =>
        new[] { "passed", "device-failed", "server-failed", "early-failed", "stand-missing" }.Select(scenario => model.Concat([scenario]).ToArray()));

    static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
    static string FileFor(string model) => Path.Combine(Root, "examples", "profiles", "Проверка 10 моделей",
        model + " · Диагностика и отчёт · 13.11.26.json");
    static IEnumerable<NodeViewModel> All(GraphWorkspaceViewModel graph) => graph.Nodes.SelectMany(n =>
        new[] { n }.Concat(n is ICompositeNodeViewModel composite ? All(composite.BodyGraph) : []));
    static GraphWorkspaceViewModel Containing(GraphWorkspaceViewModel graph, NodeViewModel node) => graph.Nodes.Contains(node)
        ? graph : graph.Nodes.OfType<ICompositeNodeViewModel>().Select(c => ContainingOrNull(c.BodyGraph, node)).First(g => g != null)!;
    static GraphWorkspaceViewModel? ContainingOrNull(GraphWorkspaceViewModel graph, NodeViewModel node) => graph.Nodes.Contains(node)
        ? graph : graph.Nodes.OfType<ICompositeNodeViewModel>().Select(c => ContainingOrNull(c.BodyGraph, node)).FirstOrDefault(g => g != null);

    [Theory, MemberData(nameof(Models))]
    public void ProfilesLoadCompileAndRoundTripWithoutLosingConnections(string model, int code, int boardType, int copper, int optical)
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        var name = GraphSerializer.Deserialize(File.ReadAllText(FileFor(model)), vm);
        Assert.Equal(Path.GetFileNameWithoutExtension(FileFor(model)), name);
        Assert.True(vm.RootGraph.UseCatalogSerials);
        var product = EquipmentCatalog.Load(Path.GetDirectoryName(FileFor(model))!).Resolve(vm.RootGraph.DeviceModelId, null)!;
        Assert.Equal(model, product.Name);
        Assert.Equal(code, product.LabelCode);
        Assert.Equal(model, product.EffectiveServerDeviceType);
        Assert.Equal(code * 100000, product.SerialSeries!.SerialOffset);
        Assert.Null(vm.RootGraph.ReportTemplate!.Validate());
        Assert.False(vm.RootGraph.ReportTemplate.IncludeAutomaticEntries);
        var before = ConnectionSignatures(vm.RootGraph).ToArray();
        GraphCompiler.ValidateRunConnections(vm.RootGraph, vm.RootGraph.Hardware, "QUALIFICATION-STAND");
        using var compiler = new GraphCompiler(bus, NullLogger.Instance);
        Assert.NotNull(compiler.Compile(vm.RootGraph));
        GraphSerializer.Deserialize(GraphSerializer.Serialize(vm, name), vm);
        Assert.NotNull(compiler.Compile(vm.RootGraph));
        Assert.Equal(before, ConnectionSignatures(vm.RootGraph).ToArray());
        Assert.Equal(14, vm.RootGraph.Nodes.Count);
        var nodes = All(vm.RootGraph).ToArray();
        Assert.Single(nodes.OfType<GetSerialNumberFromServerNodeViewModel>());
        Assert.DoesNotContain(nodes, n => n is SetProMacNodeViewModel or SetPswMacNodeViewModel or UpdatePswFirmwareNodeViewModel);
        var serial = Assert.Single(nodes.OfType<GetSerialNumberFromServerNodeViewModel>());
        Assert.False(serial.UseFixedSerialNumber);
        Assert.Empty(serial.DeviceType);
        Assert.Empty(serial.ServerBaseUrl);
        Assert.Equal(0, serial.RetryCount);
        Assert.All(nodes.OfType<SendTestReportNodeViewModel>(), send =>
        {
            Assert.Empty(send.ServerBaseUrl);
            Assert.True(send.SaveLocalCopy);
            Assert.Equal(0, send.RetryCount);
            Assert.Equal("/api/Api.svc/result.json", send.Endpoint);
            Assert.False(send.FailOnError);
        });
        Assert.All(nodes.OfType<BuildTestReportNodeViewModel>(), build =>
        {
            Assert.Equal("diagnostic", build.TestType);
            Assert.False(build.IncludeAllVariables);
        });
        var selftest = nodes.OfType<SelfTestCheckNodeViewModel>().ToArray();
        Assert.Equal(3, selftest.Length); // CPU, type, then actual selftest, after assignment.
        Assert.All(selftest, n => Assert.Contains(code >= 32 ? "/cgi-bin/luci/" : "/test.shtml", n.Url));
        var stages = vm.RootGraph.Nodes.OfType<SubtestNodeViewModel>().ToArray();
        var path = new List<SubtestNodeViewModel>();
        var cursor = vm.RootGraph.Nodes.OfType<StartNodeViewModel>().Single() as NodeViewModel;
        while (cursor is not EndNodeViewModel)
        {
            cursor = Assert.Single(vm.RootGraph.Connections.Where(c => ReferenceEquals(c.Source.Parent, cursor))).Target.Parent!;
            if (cursor is SubtestNodeViewModel s) path.Add(s);
            Assert.True(path.Count <= 10);
        }
        Assert.Equal(Enumerable.Range(1, 10).Select(i => i.ToString("D2")), path.Select(s => s.Name[..2]));
        Assert.True(stages[0].RunOnlyOnFirstRun);
        Assert.Equal(new[] { "cpu_id", "dev_type" }, stages[2].BodyGraph.Nodes.OfType<SelfTestCheckNodeViewModel>().Select(n => n.ReadField));
        var data = Assert.Single(nodes.OfType<RunDataTestNodeViewModel>());
        var pairs = data.PortsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(copper / 2 + 1, pairs.Length);
        Assert.True(data.Bidirectional);
        Assert.Equal(1, data.AllowedLossPercent);
        Assert.Equal(2, data.AllowedTxDeficitPercent);
        Assert.True(data.AllowGigabit);
        Assert.EndsWith("192.168.0.8,192.168.0.9,1000", pairs[^1]);
        var printStage = stages.Single(s => s.Name.StartsWith("10."));
        Assert.False(printStage.IsEnabled);
        var print = Assert.Single(printStage.BodyGraph.Nodes.OfType<PrintLabelNodeViewModel>());
        Assert.True(print.UseDefaultPrinter);
        Assert.True(print.UseQtProZplFormat);
        Assert.Equal("Dut.default_mac", print.MacVariableName);
        // The ordinary and emergency paths both switch off AC1 and AC2, including after a write failure.
        foreach (var s in stages.Where(s => s.Name.StartsWith("08.") || s.Name == "Аварийное выключение"))
        {
            Assert.Equal(new ushort[] { 1200, 1201 }, s.BodyGraph.Nodes.OfType<ModbusWriteNodeViewModel>().Select(n => n.Address));
            Assert.Equal(2, s.BodyGraph.Connections.Count(c => c.Source.Title == "False"));
        }
    }

    [Theory, MemberData(nameof(Models))]
    public async Task DeviceTypeGateRejectsAnotherBoardBeforeSerialAllocation(string model, int code, int boardType, int copper, int optical)
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        GraphSerializer.Deserialize(File.ReadAllText(FileFor(model)), vm);
        var gate = Assert.Single(All(vm.RootGraph).OfType<ParseResponseNodeViewModel>());
        var ctx = new TestContext(new RegisterState());
        foreach (var allowed in new[] { code, boardType }.Distinct())
        {
            ctx.SetVariable("Dut.dev_type", allowed.ToString());
            Assert.Equal(StepResult.True, await gate.CreateStep(NullLogger.Instance).ExecuteAsync(ctx, default));
        }
        ctx.SetVariable("Dut.dev_type", "999");
        Assert.Equal(StepResult.False, await gate.CreateStep(NullLogger.Instance).ExecuteAsync(ctx, default));
        Assert.False(ctx.Variables.ContainsKey("VerifiedDeviceType"));
        Assert.Null(ctx.IssuedSerialNumber);
        var identity = Assert.Single(All(vm.RootGraph).OfType<CompareVariablesNodeViewModel>());
        Assert.Equal("DeviceIdentity", identity.ResultVariableName);
        ctx.SetVariable("Dut.cpu_id", "cpu-1");
        ctx.SetVariable("SerialNumberCpuId", "cpu-1");
        Assert.Equal(StepResult.True, await identity.CreateStep(NullLogger.Instance).ExecuteAsync(ctx, default));
        Assert.True(ctx.GetVariable<bool>("DeviceIdentity.Passed"));
        ctx.SetVariable("Dut.cpu_id", "cpu-2");
        Assert.Equal(StepResult.False, await identity.CreateStep(NullLogger.Instance).ExecuteAsync(ctx, default));
        Assert.True(ctx.Variables.ContainsKey("DeviceIdentity.Passed"));
        Assert.False(ctx.GetVariable<bool>("DeviceIdentity.Passed"));
    }

    [Theory, MemberData(nameof(Models))]
    public void FailedRunWithAssignedNumberDoesNotNeedAServiceSerial(string model, int code, int boardType, int copper, int optical)
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        GraphSerializer.Deserialize(File.ReadAllText(FileFor(model)), vm);
        var product = EquipmentCatalog.Load(Path.GetDirectoryName(FileFor(model))!).Resolve(vm.RootGraph.DeviceModelId, null)!;
        var ctx = new TestContext(new RegisterState()) { ProductionModel = product };
        Assert.Equal(code == 32, FailureReportPolicy.ShouldSend(ExecutionStatus.Failed, ctx, false, false, false));
        Assert.False(FailureReportPolicy.ShouldSend(ExecutionStatus.Failed, ctx, false, true, false));
        Assert.Equal(code == 32, FailureReportPolicy.ShouldSend(ExecutionStatus.Failed, ctx, false, true, true));
        ctx.SetVariable("SerialNumber.Assigned", product.SerialSeries!.FirstSerialNumber);
        Assert.True(FailureReportPolicy.ShouldSend(ExecutionStatus.Failed, ctx, false, false, false));
        Assert.False(FailureReportPolicy.ShouldSend(ExecutionStatus.Failed, ctx, true, false, false));
        Assert.False(FailureReportPolicy.ShouldSend(ExecutionStatus.Completed, ctx, false, false, false));
        Assert.False(FailureReportPolicy.ShouldSend(ExecutionStatus.Cancelled, ctx, false, false, false));
        var board = new TestContext(new RegisterState())
        {
            ProductionModel = EquipmentCatalog.Load(Path.GetDirectoryName(FileFor(model))!).Resolve(null, "RTL-POE")
        };
        Assert.True(FailureReportPolicy.ShouldSend(ExecutionStatus.Failed, board, false, false, false));
        Assert.False(FailureReportPolicy.ShouldSend(ExecutionStatus.Failed, board, false, true, true));
    }

    [Theory, MemberData(nameof(Models))]
    public async Task OptionalLabelUsesFullSerialCatalogCodeAndActualDeviceMac(string model, int code, int boardType, int copper, int optical)
    {
        var settings = AppSettings.Instance;
        var oldPrinter = settings.DefaultPrinterName;
        try
        {
            settings.DefaultPrinterName = "QUALIFICATION-PRINTER";
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            GraphSerializer.Deserialize(File.ReadAllText(FileFor(model)), vm);
            var product = EquipmentCatalog.Load(Path.GetDirectoryName(FileFor(model))!).Resolve(vm.RootGraph.DeviceModelId, null)!;
            var ctx = new TestContext(new RegisterState()) { ProductionModel = product, LabelVariant = 0 };
            var serial = code * 100000 + 579;
            ctx.SetVariable("SerialNumber", serial);
            ctx.SetVariable("SerialNumber.Assigned", serial);
            ctx.SetVariable("Dut.default_mac", "C0:11:A6:00:00:01");
            ctx.SetVariable("CalculatedMac", "C0:11:A6:FF:00:01");
            var node = All(vm.RootGraph).OfType<PrintLabelNodeViewModel>().Single();
            var printer = new LabelTransport();
            Assert.Equal(StepResult.False, await node.CreateStep(NullLogger.Instance, printer).ExecuteAsync(ctx, default));
            Assert.Equal(0, printer.Calls); // A newly assigned serial cannot be printed with another MAC.
            ctx.SetVariable("Dut.default_mac", $"C0:11:A6:{code:X2}:02:43");
            Assert.Equal(StepResult.True, await node.CreateStep(NullLogger.Instance, printer).ExecuteAsync(ctx, default));
            Assert.Equal("QUALIFICATION-PRINTER", printer.Queue);
            Assert.Equal(1, printer.Calls);
            var command = Encoding.Latin1.GetString(printer.Bytes);
            Assert.Contains(serial.ToString(), command);
            var barcodeField = Regex.Match(command, @"\^BC[^\^]*\^FD(?<data>[^\^]*)\^FS").Groups["data"].Value;
            Assert.NotEmpty(barcodeField);
            Assert.NotNull(Code128Encoding.EncodeZpl(barcodeField, false, out var barcode));
            Assert.Equal($"0{code:D3}00579", barcode);
            Assert.Contains($"C0:11:A6:{code:X2}:02:43", command);
            Assert.DoesNotContain("C0:11:A6:FF:00:01", command); // The source is the actual device MAC.
            var evidence = Environment.GetEnvironmentVariable("TFORTIS_QUALIFICATION_EVIDENCE");
            if (!string.IsNullOrWhiteSpace(evidence))
            {
                var folder = Path.Combine(evidence, model);
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, "optional-label.zpl"), printer.Bytes);
            }
        }
        finally { settings.DefaultPrinterName = oldPrinter; }
    }

    [Theory, MemberData(nameof(Models))]
    public async Task CpuReadDoesNotRunSelftestButAnInitFailureDoesFail(string model, int code, int boardType, int copper, int optical)
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        GraphSerializer.Deserialize(File.ReadAllText(FileFor(model)), vm);
        var nodes = All(vm.RootGraph).OfType<SelfTestCheckNodeViewModel>().ToArray();
        foreach (var init in new[] { 0, 1 })
        {
            var page = new PageTransport($"<selftest><cpu_id>QUALIFICATION-CPU</cpu_id><dev_type>{boardType}</dev_type><init_ok>{init}</init_ok><default_mac>C0:11:A6:00:00:01</default_mac></selftest>");
            var ctx = new TestContext(new RegisterState());
            foreach (var read in nodes.Where(n => n.IsReadingField))
            {
                Assert.Equal(StepResult.True, await Offline(read, page).ExecuteAsync(ctx, default));
                Assert.Empty(ctx.ReportEntries);
                Assert.Null(ctx.IssuedSerialNumber);
            }
            Assert.Equal("QUALIFICATION-CPU", ctx.GetVariable<string>("Dut.cpu_id"));
            Assert.Equal(boardType.ToString(), ctx.GetVariable<string>("Dut.dev_type"));
            var check = nodes.Single(n => !n.IsReadingField);
            Assert.Equal(init == 1 ? StepResult.True : StepResult.False, await Offline(check, page).ExecuteAsync(ctx, default));
            Assert.Equal(init != 1, ctx.HasCriticalError);
            Assert.Equal(3, page.Calls);
        }
    }

    static SelfTestCheckStep Offline(SelfTestCheckNodeViewModel node, IHttpRequestService transport) => new(
        transport, NullLogger.Instance, node.Url, 1000, node.OutputPrefix, node.ValidationRules, node.FailOnError,
        useBrowser: false, pollIntervalMs: 100, enforceMinimumDeviceReadyTimeout: false, readField: node.ReadField);

    [Theory, MemberData(nameof(Models))]
    public async Task SerialBoundariesForeignSeriesAndMissingCpuAreHandled(string model, int code, int boardType, int copper, int optical)
    {
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        GraphSerializer.Deserialize(File.ReadAllText(FileFor(model)), vm);
        var product = EquipmentCatalog.Load(Path.GetDirectoryName(FileFor(model))!).Resolve(vm.RootGraph.DeviceModelId, null)!;
        var serialNode = Assert.Single(All(vm.RootGraph).OfType<GetSerialNumberFromServerNodeViewModel>());
        var series = product.SerialSeries!;
        foreach (var value in new[] { series.FirstSerialNumber, series.LastSerialNumber, series.FirstSerialNumber - 1, series.LastSerialNumber + 1,
                     code == 32 ? 3300579 : 3200579 })
        {
            var ctx = new TestContext(new RegisterState()) { ProductionModel = product };
            ctx.SetVariable("Dut.cpu_id", "QUALIFICATION-CPU");
            var transport = new SerialTransport(value);
            using var compiler = new GraphCompiler(bus, transport, NullLogger.Instance);
            var valid = value >= series.FirstSerialNumber && value <= series.LastSerialNumber;
            Assert.Equal(valid ? StepResult.True : StepResult.False,
                await compiler.Compile(Containing(vm.RootGraph, serialNode), serialNode, singleNode: true).StartNode.Step!.ExecuteAsync(ctx, default));
            Assert.Equal(1, transport.Calls);
            Assert.Contains("devType=" + Uri.EscapeDataString(model) + "&cpuId=QUALIFICATION-CPU", transport.Url);
            Assert.Equal(valid ? value : (int?)null, ctx.IssuedSerialNumber);
        }
        var missing = new TestContext(new RegisterState()) { ProductionModel = product };
        var unused = new SerialTransport(series.FirstSerialNumber);
        using var missingCompiler = new GraphCompiler(bus, unused, NullLogger.Instance);
        Assert.Equal(StepResult.False, await missingCompiler.Compile(Containing(vm.RootGraph, serialNode), serialNode, singleNode: true).StartNode.Step!.ExecuteAsync(missing, default));
        Assert.Equal(0, unused.Calls);
    }

    [Theory, MemberData(nameof(ReportCases))]
    public async Task RealCompiledReportStagesPreserveIdentityAndDeliveryStatus(
        string model, int code, int boardType, int copper, int optical, string scenario)
    {
        var settings = AppSettings.Instance;
        var oldStand = settings.StandId;
        var oldServer = settings.ServerBaseUrl;
        var folder = Directory.CreateTempSubdirectory("tfortis-qualification-");
        try
        {
            settings.StandId = scenario == "stand-missing" ? "" : "QUALIFICATION-SIMULATION";
            settings.ServerBaseUrl = "https://qualification.invalid";
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            GraphSerializer.Deserialize(File.ReadAllText(FileFor(model)), vm);
            var product = EquipmentCatalog.Load(Path.GetDirectoryName(FileFor(model))!).Resolve(vm.RootGraph.DeviceModelId, null)!;
            var serial = code * 100000 + 579;
            var transport = new SerialTransport(serial);
            using var handler = new ReportTransport(scenario == "server-failed" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
            using var compiler = new GraphCompiler(bus, transport, NullLogger.Instance)
            {
                ProfileReportTemplate = vm.RootGraph.ReportTemplate,
                ReportClientFactory = () => new HttpClient(handler, disposeHandler: false)
            };
            var ctx = new TestContext(new RegisterState()) { ProductionModel = product, DeviceModel = model };
            var all = All(vm.RootGraph).ToArray();
            foreach (var send in all.OfType<SendTestReportNodeViewModel>()) send.LocalReportsDirectory = folder.FullName;
            var failed = scenario is "device-failed" or "early-failed";
            if (scenario != "early-failed")
            {
                ctx.SetVariable("Dut.cpu_id", "QUALIFICATION-CPU");
                var get = all.OfType<GetSerialNumberFromServerNodeViewModel>().Single();
                Assert.Equal(StepResult.True, await compiler.Compile(Containing(vm.RootGraph, get), get, singleNode: true).StartNode.Step!.ExecuteAsync(ctx, default));
                ctx.SetVariable("Dut.dev_type", boardType);
                ctx.SetVariable("Dut.default_mac", "C0:11:A6:00:00:01");
                var identity = all.OfType<CompareVariablesNodeViewModel>().Single();
                Assert.Equal(StepResult.True, await compiler.Compile(Containing(vm.RootGraph, identity), identity, singleNode: true).StartNode.Step!.ExecuteAsync(ctx, default));
                ctx.SetVariable("Secret.Debug", "MUST-NOT-LEAK");
                ctx.SetVariable("DataTest.Debug", "INTERNAL-DETAILS");
                var dataPairs = copper / 2 + 1;
                for (var i = 0; i < dataPairs; i++) ctx.SetVariable($"DataTest.Port{i}.Passed", !(failed && i == 0));
                ctx.AddReportEntry("самотестирование", true, "1");
                ctx.AddReportEntry("Версия прошивки", true, "qualification-fixture");
                ctx.AddReportEntry("Версия бутлоадера", true, "fixture");
                ctx.AddReportEntry("MAC адрес", true, "C0:11:A6:00:00:01");
                ctx.SetVariable("Dut.ups_det", 1);
                ctx.AddReportEntry("напряжение АКБ", true, "24.2");
                var mac = all.OfType<BuildMacFromSerialNodeViewModel>().Single();
                Assert.Equal(StepResult.True, await compiler.Compile(Containing(vm.RootGraph, mac), mac, singleNode: true).StartNode.Step!.ExecuteAsync(ctx, default));
                Assert.Equal($"C0:11:A6:{code:X2}:02:43", ctx.GetVariable<string>("CalculatedMac"));
            }
            if (failed)
            {
                ctx.IsCleanup = true;
                ctx.HasCriticalError = true;
                var reason = scenario == "early-failed" ? "Контролируемый отказ: устройство не отдало CPU ID" :
                    "Контролируемый отказ: пара 192.168.0.2 ↔ 192.168.0.3";
                // TestViewModel retains the primary failure before running cleanup.
                ctx.SetVariable("Execution.Error", reason);
                ctx.SetVariable("Execution.SendFailureReport", FailureReportPolicy.ShouldSend(ExecutionStatus.Failed, ctx, false, false, false));
                ctx.AddReportEntry("Проверка устройства", false, reason);
            }
            var stage = vm.RootGraph.Nodes.OfType<SubtestNodeViewModel>().Single(s => failed ? s.Name == "Отчёт при ошибке" : s.Name.StartsWith("09."));
            var execution = await new TestExecutor().ExecuteAsync(compiler.Compile(stage.BodyGraph).StartNode, ctx, default);
            if (scenario == "stand-missing")
            {
                Assert.Equal(ExecutionStatus.Failed, execution);
                Assert.Equal(0, handler.Calls);
                Assert.Empty(folder.GetFiles());
                Assert.Contains("Stand ID", ctx.GetVariable<string>("BuildReport.Error"));
                WriteEvidence(model, scenario, "", new { model, status = "Failed to build: missing Stand ID", requests = handler.Calls });
                return;
            }
            Assert.Equal(ExecutionStatus.Completed, execution);
            var canSend = scenario != "early-failed" || code == 32;
            Assert.Equal(canSend ? 1 : 0, handler.Calls);
            if (!canSend)
            {
                Assert.Empty(folder.GetFiles());
                Assert.Null(ctx.GetVariable<string>("TestReportText"));
                WriteEvidence(model, scenario, "", new { model, requests = handler.Calls,
                    status = "No report before serial assignment; no configured failure serial",
                    error = ctx.GetVariable<string>("BuildReport.Error") });
                return;
            }
            var expectedSerial = scenario == "early-failed" ? 3200001 : serial;
            var report = ctx.GetVariable<string>("TestReportText")!;
            Assert.StartsWith($"test_result=true={(failed ? 0 : 1)}\r\n", report);
            Assert.Single(report.Split("\r\n").Where(l => l.StartsWith("serial_num=")));
            Assert.Contains($"serial_num=true={expectedSerial}\r\n", report);
            Assert.Contains("Тип проверки=true=diagnostic\r\n", report);
            if (scenario != "early-failed") Assert.Contains("CPU ID соответствует выданному номеру=true=1\r\n", report);
            Assert.DoesNotContain("MUST-NOT-LEAK", report);
            Assert.DoesNotContain("INTERNAL-DETAILS", report);
            Assert.Equal("https://qualification.invalid/api/Api.svc/result.json", handler.Url);
            Assert.Equal("POST", handler.Method);
            Assert.StartsWith("multipart/form-data", handler.ContentType);
            Assert.Contains("name=\"updatefile\"; filename=\"result.json\"", handler.Body);
            Assert.Contains(report, handler.Body);
            Assert.Equal(report, File.ReadAllText(Assert.Single(folder.GetFiles()).FullName));
            Assert.Equal(!failed, ctx.GetVariable<bool>("BuildReport.DevicePassed"));
            Assert.Equal(scenario == "server-failed" ? "Failed" : "Sent", ctx.GetVariable<string>("ReportDelivery.Status"));
            if (failed) Assert.Contains("Причина остановки=false=", report);
            if (scenario == "device-failed") Assert.Contains("Причина остановки=false=Контролируемый отказ: пара 192.168.0.2 ↔ 192.168.0.3", report);
            if (scenario != "server-failed")
            {
                // Cleanup must not upload the already accepted report a second time.
                ctx.IsCleanup = true;
                ctx.SetVariable("Execution.SendFailureReport", true);
                var failure = vm.RootGraph.Nodes.OfType<SubtestNodeViewModel>().Single(s => s.Name == "Отчёт при ошибке");
                await new TestExecutor().ExecuteAsync(compiler.Compile(failure.BodyGraph).StartNode, ctx, default);
                Assert.Equal(1, handler.Calls);
            }
            WriteEvidence(model, scenario, report, new { handler.Url, handler.Method, handler.ContentType,
                expectedSerial, serialRequest = transport.Url, delivery = ctx.GetVariable<string>("ReportDelivery.Status"),
                multipartBody = handler.Body,
                hardware = "not exercised; device measurements and DataTest are synthetic fixtures" });
            if (scenario == "passed")
            {
                ctx.SetVariable("SerialNumber", serial + 1);
                var build = stage.BodyGraph.Nodes.OfType<BuildTestReportNodeViewModel>().Single();
                Assert.Equal(StepResult.False, await compiler.Compile(stage.BodyGraph, build, singleNode: true).StartNode.Step!.ExecuteAsync(ctx, default));
                ctx.SetVariable("SerialNumber", serial);
                ctx.SetVariable("TestReportText", report.Replace($"serial_num=true={serial}\r\n", $"serial_num=true={serial + 1}\r\n"));
                var send = stage.BodyGraph.Nodes.OfType<SendTestReportNodeViewModel>().Single();
                await compiler.Compile(stage.BodyGraph, send, singleNode: true).StartNode.Step!.ExecuteAsync(ctx, default);
                Assert.False(ctx.GetVariable<bool>("SendReport.Success"));
                Assert.Equal(1, handler.Calls); // A different serial in the payload never reaches HTTP.
            }
        }
        finally
        {
            settings.StandId = oldStand;
            settings.ServerBaseUrl = oldServer;
            folder.Delete(true);
        }
    }

    static void WriteEvidence(string model, string scenario, string report, object metadata)
    {
        var root = Environment.GetEnvironmentVariable("TFORTIS_QUALIFICATION_EVIDENCE");
        if (string.IsNullOrWhiteSpace(root)) return;
        var folder = Path.Combine(root, model);
        Directory.CreateDirectory(folder);
        if (report.Length > 0) File.WriteAllText(Path.Combine(folder, scenario + ".txt"), report);
        File.WriteAllText(Path.Combine(folder, scenario + ".json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
    }

    static IEnumerable<string> ConnectionSignatures(GraphWorkspaceViewModel graph, string prefix = "root")
    {
        foreach (var c in graph.Connections)
            yield return $"{prefix}:{graph.Nodes.IndexOf(c.Source.Parent!)}:{c.Source.Title}>{graph.Nodes.IndexOf(c.Target.Parent!)}:{c.Target.Title}";
        foreach (var composite in graph.Nodes.OfType<ICompositeNodeViewModel>())
            foreach (var signature in ConnectionSignatures(composite.BodyGraph, prefix + "/" + graph.Nodes.IndexOf((NodeViewModel)composite)))
                yield return signature;
    }

    sealed class SerialTransport(int serial) : IHttpRequestService
    {
        public int Calls { get; private set; }
        public string Url { get; private set; } = "";
        public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls++;
            Url = url;
            return Task.FromResult(HttpRequestResult.Success(200, serial.ToString(), TimeSpan.Zero));
        }
    }

    sealed class PageTransport(string page) : IHttpRequestService
    {
        public int Calls { get; private set; }
        public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(HttpRequestResult.Success(200, page, TimeSpan.Zero));
        }
    }

    sealed class ReportTransport(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string Url { get; private set; } = "";
        public string Method { get; private set; } = "";
        public string ContentType { get; private set; } = "";
        public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Url = request.RequestUri!.AbsoluteUri;
            Method = request.Method.Method;
            ContentType = request.Content!.Headers.ContentType!.ToString();
            Body = await request.Content.ReadAsStringAsync(token);
            return new HttpResponseMessage(status) { Content = new StringContent(status == HttpStatusCode.OK ? "Ok" : "Unavailable") };
        }
    }

    sealed class LabelTransport : IRawLabelPrinter
    {
        public string Queue { get; private set; } = "";
        public byte[] Bytes { get; private set; } = [];
        public int Calls { get; private set; }
        public RawLabelPrintResult Print(string printerName, byte[] bytes, CancellationToken token)
        {
            Queue = printerName;
            Bytes = bytes;
            Calls++;
            return RawLabelPrintResult.Ok();
        }
    }
}
