using System.Text.Json;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Graph;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

public class FullProfileSerializationTests
{
    [Theory]
    [InlineData("PSW_2G6F_plus_full_algorithm.json", "/test.shtml")]
    [InlineData("PSW_2G6F_plus_diagnostic_draft.json", "/test.shtml")]
    [InlineData("PSW_UPS_Box_8x2Pro_full_algorithm_polling.json", "/cgi-bin/luci/admin/statistics/deviceinfo")]
    public void SelftestUrlsFollowSwitchFamily(string profileFile, string expectedPath)
    {
        using var profile = ReadProfile(profileFile);
        var selftestNodes = AllNodes(profile.RootElement)
            .Where(node => node.GetProperty("type").GetString() == "Selftest Check")
            .ToArray();

        Assert.NotEmpty(selftestNodes);
        Assert.All(selftestNodes, node => Assert.Contains(expectedPath,
            node.GetProperty("url").GetString(), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("PSW_2G6F_plus_full_algorithm.json", "Set PSW MAC (UDP)", "Set Pro MAC")]
    [InlineData("PSW_UPS_Box_8x2Pro_full_algorithm_polling.json", "Set Pro MAC", "Set PSW MAC (UDP)")]
    public void MacWriterFollowsSwitchFamily(string profileFile, string expectedType, string forbiddenType)
    {
        using var profile = ReadProfile(profileFile);
        var types = AllNodes(profile.RootElement)
            .Select(node => node.GetProperty("type").GetString()).ToArray();

        Assert.Contains(expectedType, types);
        Assert.DoesNotContain(forbiddenType, types);
    }

    private static JsonDocument ReadProfile(string file) => JsonDocument.Parse(File.ReadAllText(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "profiles", file))));

    private static IEnumerable<JsonElement> AllNodes(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("type", out _)) yield return element;
            foreach (var property in element.EnumerateObject())
                foreach (var node in AllNodes(property.Value)) yield return node;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                foreach (var node in AllNodes(item)) yield return node;
        }
    }

    [Fact]
    public void Psw2G6FDiagnosticDraft_LoadsAndKeepsRequiredFailureGate()
    {
        var profilePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "profiles",
            "PSW_2G6F_plus_diagnostic_draft.json"));
        using var modbus = new ModbusService();
        var viewModel = new TestViewModel(modbus, new SlaveManager(modbus));

        var name = GraphSerializer.Deserialize(File.ReadAllText(profilePath), viewModel);

        Assert.Contains("диагностический черновик", name);
        Assert.Equal(17, viewModel.RootGraph.Nodes.Count);
        Assert.Equal(15, viewModel.RootGraph.Connections.Count);
        var firstSelftest = Assert.Single(viewModel.RootGraph.Nodes.OfType<SelfTestCheckNodeViewModel>());
        Assert.Equal("http://192.168.0.1/test.shtml", firstSelftest.Url);
        Assert.Equal("init_ok=1..1\ndev_type=6..6", firstSelftest.ValidationRules);
        Assert.Equal(300000, firstSelftest.TimeoutMs);
        Assert.Equal(20000, viewModel.RootGraph.Nodes.OfType<DelayNodeViewModel>().Single().Milliseconds);
        Assert.Equal(new[] { 5, 5, 3, 5 }, viewModel.RootGraph.Nodes
            .OfType<ForEachSlaveNodeViewModel>()
            .Select(node => node.BodyGraph.Connections.Count));
        Assert.True(GraphConnectionRequirements.RequiresStandConnection(viewModel.RootGraph));
        var sensors = viewModel.RootGraph.Nodes.OfType<SubtestNodeViewModel>().Single(n => !n.RunOnFailure);
        Assert.Single(sensors.BodyGraph.Nodes.OfType<WaitVariableUntilNodeViewModel>());
        Assert.DoesNotContain(sensors.BodyGraph.Nodes.OfType<ModbusWriteNodeViewModel>(), n => n.Address == 1507);
        var dataTest = viewModel.RootGraph.Nodes.OfType<RunDataTestNodeViewModel>().Single();
        Assert.Equal(100, dataTest.TargetBandwidthMbps);
        Assert.Equal(3, dataTest.PortsText.Split('\n').Length);
        Assert.DoesNotContain(viewModel.RootGraph.Nodes, node => node is SetProMacNodeViewModel or PrintLabelNodeViewModel);
        var cleanup = viewModel.RootGraph.Nodes.OfType<SubtestNodeViewModel>().Single(n => n.RunOnFailure);
        Assert.True(cleanup.RunOnFailure);
        Assert.Equal(8, cleanup.BodyGraph.Connections.Count);
        Assert.Equal(9, cleanup.BodyGraph.Nodes.OfType<ForEachSlaveNodeViewModel>()
            .Single().BodyGraph.Connections.Count);
        var gate = viewModel.RootGraph.Nodes.OfType<CheckVariableEqualityNodeViewModel>()
            .Single(node => node.VariableName == "Migration.ProductionReady");
        Assert.Equal("1", gate.ExpectedValue);
        Assert.NotNull(new GraphCompiler(modbus, NullLogger.Instance).Compile(viewModel.RootGraph));
        Assert.DoesNotContain("Check IO-2 Sensors and Relay", GraphSerializer.Serialize(viewModel, name));
    }

    [Fact]
    public void LegacyGigabitDataTestProfile_IsMigratedTo100Mbit()
    {
        const string json = """
            {
              "name": "legacy-data-test",
              "nodes": [
                {
                  "id": "0",
                  "type": "Run Data Test",
                  "x": 0,
                  "y": 0,
                  "targetBandwidthMbps": 1000,
                  "portsText": "port0-1,192.168.0.2,192.168.0.3,1000"
                }
              ],
              "connections": []
            }
            """;

        using var modbus = new ModbusService();
        var viewModel = new TestViewModel(modbus, new SlaveManager(modbus));

        GraphSerializer.Deserialize(json, viewModel);

        var dataTestNode = viewModel.RootGraph.Nodes.OfType<RunDataTestNodeViewModel>().Single();
        Assert.Equal(100, dataTestNode.TargetBandwidthMbps);
        Assert.Equal("port0-1,192.168.0.2,192.168.0.3,100", dataTestNode.PortsText);
        Assert.Equal(2.0, dataTestNode.AllowedTxDeficitPercent);
        Assert.True(dataTestNode.Bidirectional);
    }

    [Fact]
    public void FullPollingProfile_LoadsThroughApplicationSerializer()
    {
        var profilePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "profiles",
            "PSW_UPS_Box_8x2Pro_full_algorithm_polling.json"));
        var json = File.ReadAllText(profilePath);

        using var modbus = new ModbusService();
        var viewModel = new TestViewModel(modbus, new SlaveManager(modbus));

        var profileName = GraphSerializer.Deserialize(json, viewModel);

        Assert.Equal("PSW_UPS_Box_8x2Pro_full_algorithm_polling", profileName);
        Assert.True(GraphConnectionRequirements.RequiresStandConnection(viewModel.RootGraph));
        var startupSubtest = viewModel.RootGraph.Nodes
            .OfType<SubtestNodeViewModel>()
            .Single(node => node.Name == "06. Ожидание загрузки DUT и selftest");
        var dataTestSubtest = viewModel.RootGraph.Nodes
            .OfType<SubtestNodeViewModel>()
            .Single(node => node.Name == "10. DataTest портов 0..9");
        var inOutSubtest = viewModel.RootGraph.Nodes
            .OfType<SubtestNodeViewModel>()
            .Single(node => node.Name == "09a. Проверка Sensor1, Sensor2 и реле");
        var batterySubtest = viewModel.RootGraph.Nodes
            .OfType<SubtestNodeViewModel>()
            .Single(node => node.Name == "проверка акб (упс)");
        var reportSubtest = viewModel.RootGraph.Nodes
            .OfType<SubtestNodeViewModel>()
            .Single(node => node.Name == "17. Финальная статистика и отчет");
        var emergencyShutdownSubtest = viewModel.RootGraph.Nodes
            .OfType<SubtestNodeViewModel>()
            .Single(node => node.Name == "Аварийное выключение стенда");
        var failureReportSubtest = viewModel.RootGraph.Nodes
            .OfType<SubtestNodeViewModel>()
            .Single(node => node.Name == "Отчёт при ошибке");
        var serialSubtest = viewModel.RootGraph.Nodes
            .OfType<SubtestNodeViewModel>()
            .Single(node => node.Name == "15. Получение серийника и запись MAC");
        var printSubtest = viewModel.RootGraph.Nodes
            .OfType<SubtestNodeViewModel>()
            .Single(node => node.Name == "16. Печать этикеток");
        Assert.Contains(viewModel.AvailableNodes, node => node is ReadHttpVariableNodeViewModel);
        Assert.DoesNotContain(viewModel.AvailableNodes, node => node.Title == "Check IO-2 Sensors and Relay");
        Assert.DoesNotContain(viewModel.AvailableNodes, node => node is GetUpsStatusNodeViewModel);
        Assert.DoesNotContain(viewModel.AvailableNodes, node => node is GetUpsVoltageNodeViewModel);
        Assert.DoesNotContain(viewModel.AvailableNodes, node => node is GetIrpStatusNodeViewModel);
        Assert.False(reportSubtest.RunOnFailure);
        Assert.True(emergencyShutdownSubtest.RunOnFailure);
        Assert.True(failureReportSubtest.RunOnFailure);
        Assert.True(
            viewModel.RootGraph.Nodes.IndexOf(emergencyShutdownSubtest) <
            viewModel.RootGraph.Nodes.IndexOf(failureReportSubtest));
        var buildReport = reportSubtest.BodyGraph.Nodes.OfType<BuildTestReportNodeViewModel>().Single();
        var sendReport = reportSubtest.BodyGraph.Nodes.OfType<SendTestReportNodeViewModel>().Single();
        var reportConfirmation = reportSubtest.BodyGraph.Nodes
            .OfType<OperatorActionNodeViewModel>()
            .Single();
        Assert.Equal("SerialNumber", buildReport.SerialVariableName);
        Assert.Equal("TestReportText", buildReport.ReportVariableName);
        Assert.Equal("production", buildReport.TestType);
        Assert.Equal("TestReportText", sendReport.ReportVariableName);
        Assert.Equal("https://iccid.fort-telecom.ru", sendReport.ServerBaseUrl);
        Assert.Contains("{SerialNumber}", reportConfirmation.Message);
        Assert.Contains("{Dut.default_mac}", reportConfirmation.Message);
        Assert.Contains(
            reportSubtest.BodyGraph.Connections,
            connection => ReferenceEquals(connection.Source.Parent, buildReport) &&
                          ReferenceEquals(connection.Target.Parent, reportConfirmation));
        Assert.Contains(
            reportSubtest.BodyGraph.Connections,
            connection => ReferenceEquals(connection.Source.Parent, reportConfirmation) &&
                          connection.Source.Title == "Продолжить" &&
                          ReferenceEquals(connection.Target.Parent, sendReport));
        Assert.Contains(
            reportSubtest.BodyGraph.Connections,
            connection => connection.Source.Parent is SelfTestCheckNodeViewModel &&
                          connection.Source.Title == "False" &&
                          ReferenceEquals(connection.Target.Parent, buildReport));

        Assert.Empty(failureReportSubtest.BodyGraph.Nodes.OfType<SelfTestCheckNodeViewModel>());
        var failureBuildReport = failureReportSubtest.BodyGraph.Nodes
            .OfType<BuildTestReportNodeViewModel>()
            .Single();
        var failureSendReport = failureReportSubtest.BodyGraph.Nodes
            .OfType<SendTestReportNodeViewModel>()
            .Single();
        var failureReportConfirmation = failureReportSubtest.BodyGraph.Nodes
            .OfType<OperatorActionNodeViewModel>()
            .Single();
        var failureSerialGuard = failureReportSubtest.BodyGraph.Nodes
            .OfType<CheckVariableRangeNodeViewModel>()
            .Single();
        Assert.Equal("SerialNumber", failureBuildReport.SerialVariableName);
        Assert.Equal("TestReportText", failureBuildReport.ReportVariableName);
        Assert.Equal("TestReportText", failureSendReport.ReportVariableName);
        Assert.Equal("https://iccid.fort-telecom.ru", failureSendReport.ServerBaseUrl);
        Assert.Equal("SerialNumber", failureSerialGuard.VariableName);
        Assert.Equal(3200000, failureSerialGuard.Min);
        Assert.Equal(3299999, failureSerialGuard.Max);
        Assert.Contains("{SerialNumber}", failureReportConfirmation.Message);
        Assert.Contains("{Dut.default_mac}", failureReportConfirmation.Message);
        Assert.Contains(
            failureReportSubtest.BodyGraph.Connections,
            connection => ReferenceEquals(connection.Source.Parent, failureSerialGuard) &&
                          connection.Source.Title == "True" &&
                          ReferenceEquals(connection.Target.Parent, failureBuildReport));
        Assert.Contains(
            failureReportSubtest.BodyGraph.Connections,
            connection => ReferenceEquals(connection.Source.Parent, failureReportConfirmation) &&
                          connection.Source.Title == "Продолжить" &&
                          ReferenceEquals(connection.Target.Parent, failureSendReport));
        var serialNode = serialSubtest.BodyGraph.Nodes
            .OfType<GetSerialNumberFromServerNodeViewModel>()
            .Single();
        Assert.Equal("https://iccid.fort-telecom.ru", serialNode.ServerBaseUrl);
        Assert.False(serialNode.UseFixedSerialNumber);
        Assert.DoesNotContain("\"fixedSerialNumber\"", json);
        var setMacNode = serialSubtest.BodyGraph.Nodes
            .OfType<SetProMacNodeViewModel>()
            .Single();
        Assert.Equal("set_mac_pro.bat", setMacNode.BatchPath);
        Assert.Equal("PSW+UPS-Box 8x2Pro", setMacNode.BoardVersion);
        Assert.Equal("Dut.NewMac", setMacNode.MacVariableName);
        Assert.Equal(60000, setMacNode.TimeoutMs);
        var printNode = printSubtest.BodyGraph.Nodes
            .OfType<PrintLabelNodeViewModel>()
            .Single();
        Assert.Equal("TSC TE310", printNode.PrinterName);
        Assert.Equal("SerialNumber", printNode.SerialVariableName);
        Assert.Equal("SerialShort", printNode.SerialShortVariableName);
        Assert.Equal("Dut.default_mac", printNode.MacVariableName);
        Assert.False(printNode.UseManualSerialNumber);
        Assert.Equal(string.Empty, printNode.ManualSerialNumber);
        Assert.Equal(4, printNode.Copies);
        Assert.True(printNode.UseQtProZplFormat);
        Assert.True(printNode.FailOnPrinterError);
        Assert.Contains(
            startupSubtest.BodyGraph.Connections,
            connection => connection.Source.Parent is DelayNodeViewModel &&
                          connection.Target.Parent is ClearArpCacheNodeViewModel);
        var startupDelay = startupSubtest.BodyGraph.Nodes
            .OfType<DelayNodeViewModel>()
            .Single(node => node.Milliseconds == 5000);
        var startupSelftest = startupSubtest.BodyGraph.Nodes
            .OfType<SelfTestCheckNodeViewModel>()
            .Single();
        Assert.Equal(5000, startupDelay.Milliseconds);
        Assert.Equal(300000, startupSelftest.TimeoutMs);
        Assert.Equal(5000, startupSelftest.PollIntervalMs);

        Assert.All(inOutSubtest.BodyGraph.Nodes.OfType<ModbusWriteNodeViewModel>(), n => Assert.Equal(21, n.SlaveId));
        Assert.Equal(2, inOutSubtest.BodyGraph.Nodes.OfType<WaitVariableUntilNodeViewModel>().Count());
        var relayWait = Assert.Single(inOutSubtest.BodyGraph.Nodes.OfType<WaitUntilNodeViewModel>());
        Assert.Equal(21, relayWait.SlaveId);
        Assert.Equal(1507, relayWait.Address);
        Assert.Equal(1, relayWait.ExpectedValue);
        Assert.True(relayWait.LiveRead);
        Assert.Equal(3, inOutSubtest.BodyGraph.Nodes.OfType<ReadHttpVariableNodeViewModel>().Count());
        Assert.Contains(
            viewModel.RootGraph.Connections,
            connection => ReferenceEquals(connection.Source.Parent, inOutSubtest) &&
                          ReferenceEquals(connection.Target.Parent, dataTestSubtest));

        var dataTestNode = dataTestSubtest.BodyGraph.Nodes
            .OfType<RunDataTestNodeViewModel>()
            .Single();
        Assert.Equal(5000, dataTestNode.InterPairDelayMs);
        Assert.Equal(100, dataTestNode.TargetBandwidthMbps);
        Assert.Equal(2.0, dataTestNode.AllowedTxDeficitPercent);
        Assert.True(dataTestNode.Bidirectional);
        var portLines = dataTestNode.PortsText.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(
            new[]
            {
                "port0-1,192.168.0.2,192.168.0.3,100",
                "port2-3,192.168.0.4,192.168.0.5,100",
                "port4-5,192.168.0.6,192.168.0.7,100",
                "port6-7,192.168.0.8,192.168.0.9,100",
                "port8-9,192.168.0.10,192.168.0.11,100"
            },
            portLines);
        Assert.Contains(
            dataTestSubtest.BodyGraph.Connections,
            connection => connection.Source.Parent is StartNodeViewModel &&
                          ReferenceEquals(connection.Target.Parent, dataTestNode));

        var allSubtestNames = viewModel.RootGraph.Nodes
            .OfType<SubtestNodeViewModel>()
            .Select(node => node.Name)
            .ToArray();
        Assert.DoesNotContain("12. UPS: переход на АКБ", allSubtestNames);
        Assert.DoesNotContain("13. UPS: подтверждение питания от АКБ", allSubtestNames);
        Assert.DoesNotContain("14. UPS: возврат на AC1", allSubtestNames);

        var waits = batterySubtest.BodyGraph.Nodes
            .OfType<WaitVariableUntilNodeViewModel>()
            .ToArray();
        Assert.Equal(3, waits.Length);
        foreach (var wait in waits)
        {
            Assert.Equal("20..27", wait.ExpectedValue);
            Assert.Equal("Dut.akb_voltage", wait.VariableName);
            Assert.Equal(VariableComparisonType.Number, wait.ComparisonType);
            Assert.Equal("SelftestSnapshot", wait.PollAction);
            Assert.Contains("/cgi-bin/luci/admin/statistics/deviceinfo", wait.Endpoint);
            Assert.Equal(HttpResponseValueType.String, wait.ResponseType);
            Assert.Equal(30000, wait.RequestTimeoutMs);
            Assert.Equal(5000, wait.IntervalMs);
            Assert.Equal(160000, wait.TimeoutMs);
            Assert.True(wait.FailOnTimeout);
        }
        Assert.DoesNotContain(
            batterySubtest.BodyGraph.Nodes.OfType<WaitVariableUntilNodeViewModel>(),
            wait => wait.VariableName == "Dut.akb_det");

        var writes = batterySubtest.BodyGraph.Nodes
            .OfType<ModbusWriteNodeViewModel>()
            .ToArray();
        Assert.Equal(
            new[]
            {
                (SlaveId: (byte)17, Address: (ushort)1706, Value: (ushort)1),
                (SlaveId: (byte)23, Address: (ushort)1200, Value: (ushort)0),
                (SlaveId: (byte)23, Address: (ushort)1200, Value: (ushort)1),
                (SlaveId: (byte)17, Address: (ushort)1706, Value: (ushort)0)
            },
            writes.Select(write => (write.SlaveId, write.Address, write.Value)).ToArray());
        Assert.All(writes, write => Assert.True(write.VerifyWrite));
        Assert.Empty(batterySubtest.BodyGraph.Nodes.OfType<DelayNodeViewModel>());

        var executionNodes = batterySubtest.BodyGraph.Nodes
            .Where(node => node is not StartNodeViewModel && node is not EndNodeViewModel)
            .ToArray();
        Assert.Equal(7, executionNodes.Length);
        for (var index = 0; index < executionNodes.Length - 1; index++)
        {
            var source = executionNodes[index];
            var target = executionNodes[index + 1];
            Assert.Contains(
                batterySubtest.BodyGraph.Connections,
                connection => ReferenceEquals(connection.Source.Parent, source) &&
                              ReferenceEquals(connection.Target.Parent, target));
        }

        Assert.Contains(
            viewModel.RootGraph.Connections,
            connection => ReferenceEquals(connection.Source.Parent, dataTestSubtest) &&
                          ReferenceEquals(connection.Target.Parent, batterySubtest));
        Assert.Contains(
            viewModel.RootGraph.Connections,
            connection => ReferenceEquals(connection.Source.Parent, batterySubtest) &&
                          connection.Target.Parent is SubtestNodeViewModel target &&
                          target.Name == "15. Получение серийника и запись MAC");
    }
}
