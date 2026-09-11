using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Graph;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

public class FullProfileSerializationTests
{
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
        Assert.Equal("SerialNumber", buildReport.SerialVariableName);
        Assert.Equal("TestReportText", buildReport.ReportVariableName);
        Assert.Equal("production", buildReport.TestType);
        Assert.Equal("TestReportText", sendReport.ReportVariableName);
        Assert.Equal("https://iccid.fort-telecom.ru", sendReport.ServerBaseUrl);
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
        Assert.Equal("SerialNumber", failureBuildReport.SerialVariableName);
        Assert.Equal("TestReportText", failureBuildReport.ReportVariableName);
        Assert.Equal("TestReportText", failureSendReport.ReportVariableName);
        Assert.Equal("https://iccid.fort-telecom.ru", failureSendReport.ServerBaseUrl);
        var serialNode = serialSubtest.BodyGraph.Nodes
            .OfType<GetSerialNumberFromServerNodeViewModel>()
            .Single();
        Assert.Equal("https://iccid.fort-telecom.ru", serialNode.ServerBaseUrl);
        Assert.True(serialNode.UseFixedSerialNumber);
        Assert.Equal(3200428, serialNode.FixedSerialNumber);
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
        Assert.False(printNode.UseManualSerialNumber);
        Assert.Equal(string.Empty, printNode.ManualSerialNumber);
        Assert.Equal(4, printNode.Copies);
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
        Assert.Equal(180000, startupSelftest.TimeoutMs);
        Assert.Equal(5000, startupSelftest.PollIntervalMs);

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
