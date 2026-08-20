using System.Linq;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.StepTests;

public class ConfigureNetworkAdaptersStepTests
{
    [Fact]
    public void ParseConfigurations_ReadsNameAndMacSelectors()
    {
        var configurations = ConfigureNetworkAdaptersStep.ParseConfigurations("""
            Name=Ethernet 0;192.168.10.1;24
            MAC=00-11-22-33-44-55;192.168.10.2;24
            """);

        Assert.Collection(
            configurations,
            first => Assert.Equal(new NetworkAdapterConfiguration("Name=Ethernet 0", "192.168.10.1", 24), first),
            second => Assert.Equal(new NetworkAdapterConfiguration("MAC=00-11-22-33-44-55", "192.168.10.2", 24), second));
    }

    [Fact]
    public void ParseConfigurations_KeepsMalformedLinesForRuntimeValidation()
    {
        var configurations = ConfigureNetworkAdaptersStep.ParseConfigurations("Ethernet 0,192.168.10.1,24");

        var configuration = Assert.Single(configurations);
        Assert.Equal("Ethernet 0,192.168.10.1,24", configuration.Selector);
        Assert.Equal(string.Empty, configuration.Address);
        Assert.Equal(0, configuration.PrefixLength);
    }

    [Fact]
    public void SerializeAndDeserialize_PreservesAdapterConfiguration()
    {
        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        vm.RootGraph.Clear();
        vm.RootGraph.Nodes.Add(new StartNodeViewModel());
        vm.RootGraph.Nodes.Add(new ConfigureNetworkAdaptersNodeViewModel
        {
            AdaptersText = "MAC=001122334455;192.168.10.1;24",
            OutputVariableName = "StandNetwork",
            FailOnError = false
        });

        var json = GraphSerializer.Serialize(vm, "Profile");

        using var loadedModbus = new ModbusService();
        var loadedVm = new TestViewModel(loadedModbus, new SlaveManager(loadedModbus));
        GraphSerializer.Deserialize(json, loadedVm);

        var node = loadedVm.RootGraph.Nodes.OfType<ConfigureNetworkAdaptersNodeViewModel>().Single();
        Assert.Equal("MAC=001122334455;192.168.10.1;24", node.AdaptersText);
        Assert.Equal("StandNetwork", node.OutputVariableName);
        Assert.False(node.FailOnError);
    }
}
