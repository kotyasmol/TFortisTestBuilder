using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
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
        var startupSubtest = viewModel.RootGraph.Nodes
            .OfType<SubtestNodeViewModel>()
            .Single(node => node.Name == "06. Ожидание загрузки DUT и selftest");
        var dataTestSubtest = viewModel.RootGraph.Nodes
            .OfType<SubtestNodeViewModel>()
            .Single(node => node.Name == "10. DataTest портов 0..9");
        var upsPreparationSubtest = viewModel.RootGraph.Nodes
            .OfType<SubtestNodeViewModel>()
            .Single(node => node.Name == "11. UPS: подготовка и детекция");
        var upsBatteryTransitionSubtest = viewModel.RootGraph.Nodes
            .OfType<SubtestNodeViewModel>()
            .Single(node => node.Name == "12. UPS: переход на АКБ");
        var upsAcTransitionSubtest = viewModel.RootGraph.Nodes
            .OfType<SubtestNodeViewModel>()
            .Single(node => node.Name == "14. UPS: возврат на AC1");
        Assert.Contains(viewModel.AvailableNodes, node => node is ReadHttpVariableNodeViewModel);
        Assert.DoesNotContain(viewModel.AvailableNodes, node => node is GetUpsStatusNodeViewModel);
        Assert.DoesNotContain(viewModel.AvailableNodes, node => node is GetUpsVoltageNodeViewModel);
        Assert.DoesNotContain(viewModel.AvailableNodes, node => node is GetIrpStatusNodeViewModel);
        Assert.Contains(
            startupSubtest.BodyGraph.Connections,
            connection => connection.Source.Parent is DelayNodeViewModel &&
                          connection.Target.Parent is ClearArpCacheNodeViewModel);

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

        Assert.DoesNotContain(upsPreparationSubtest.BodyGraph.Nodes, node => node is GetIrpStatusNodeViewModel);
        var selftestNode = upsPreparationSubtest.BodyGraph.Nodes.OfType<SelfTestCheckNodeViewModel>().Single();
        var irpCheckNode = upsPreparationSubtest.BodyGraph.Nodes
            .OfType<CheckVariableEqualityNodeViewModel>()
            .Single(node => node.VariableName == "Dut.ups_det");
        Assert.Contains(
            upsPreparationSubtest.BodyGraph.Connections,
            connection => ReferenceEquals(connection.Source.Parent, selftestNode) &&
                          ReferenceEquals(connection.Target.Parent, irpCheckNode));
        var httpReads = upsPreparationSubtest.BodyGraph.Nodes
            .OfType<ReadHttpVariableNodeViewModel>()
            .OrderBy(node => node.Endpoint)
            .ToArray();
        Assert.Equal(2, httpReads.Length);
        Assert.Contains(
            httpReads,
            node => node.Endpoint == "/api/getUpsStatus" &&
                    node.ResponseType == HttpResponseValueType.Integer &&
                    node.OutputVariableName == "Dut.ups_rez" &&
                    node.FailOnError);
        Assert.Contains(
            httpReads,
            node => node.Endpoint == "/api/getUpsVoltage" &&
                    node.ResponseType == HttpResponseValueType.Number &&
                    node.OutputVariableName == "Dut.akb_voltage" &&
                    node.FailOnError);

        var batteryWait = upsBatteryTransitionSubtest.BodyGraph.Nodes.OfType<WaitVariableUntilNodeViewModel>().Single();
        var acWait = upsAcTransitionSubtest.BodyGraph.Nodes.OfType<WaitVariableUntilNodeViewModel>().Single();
        Assert.Equal("1", batteryWait.ExpectedValue);
        Assert.Equal("0", acWait.ExpectedValue);

        foreach (var wait in new[] { batteryWait, acWait })
        {
            Assert.Equal("HttpGet", wait.PollAction);
            Assert.Equal("/api/getUpsStatus", wait.Endpoint);
            Assert.Equal(HttpResponseValueType.Integer, wait.ResponseType);
            Assert.True(wait.FailOnTimeout);
        }
    }
}
