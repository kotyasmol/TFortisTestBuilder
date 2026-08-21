using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

public class FullProfileSerializationTests
{
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
        Assert.Contains(
            startupSubtest.BodyGraph.Connections,
            connection => connection.Source.Parent is DelayNodeViewModel &&
                          connection.Target.Parent is ClearArpCacheNodeViewModel);

        var dataTestNode = dataTestSubtest.BodyGraph.Nodes
            .OfType<RunDataTestNodeViewModel>()
            .Single();
        Assert.Equal(5000, dataTestNode.InterPairDelayMs);
        var portLines = dataTestNode.PortsText.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(
            new[]
            {
                "port0-1,192.168.0.2,192.168.0.3,1000",
                "port2-3,192.168.0.4,192.168.0.5,1000",
                "port4-5,192.168.0.6,192.168.0.7,1000",
                "port6-7,192.168.0.8,192.168.0.9,1000",
                "port8-9,192.168.0.10,192.168.0.11,1000"
            },
            portLines);
        Assert.Contains(
            dataTestSubtest.BodyGraph.Connections,
            connection => connection.Source.Parent is StartNodeViewModel &&
                          ReferenceEquals(connection.Target.Parent, dataTestNode));
    }
}
