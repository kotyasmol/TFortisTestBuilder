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
        var batterySubtest = viewModel.RootGraph.Nodes
            .OfType<SubtestNodeViewModel>()
            .Single(node => node.Name == "проверка акб (упс)");
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
        Assert.Equal(new[] { "0", "1", "1", "1", "0" }, waits.Select(wait => wait.ExpectedValue).ToArray());
        foreach (var wait in waits)
        {
            Assert.Equal("Dut.akb_det", wait.VariableName);
            Assert.Equal(VariableComparisonType.Number, wait.ComparisonType);
            Assert.Equal("SelftestSnapshot", wait.PollAction);
            Assert.Contains("/cgi-bin/luci/admin/statistics/deviceinfo", wait.Endpoint);
            Assert.Equal(HttpResponseValueType.String, wait.ResponseType);
            Assert.Equal(30000, wait.RequestTimeoutMs);
            Assert.Equal(5000, wait.IntervalMs);
            Assert.Equal(160000, wait.TimeoutMs);
            Assert.True(wait.FailOnTimeout);
        }

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
        Assert.Equal(9, executionNodes.Length);
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
