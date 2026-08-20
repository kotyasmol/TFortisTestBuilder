using System.Linq;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

public class NodeColorSerializationTests
{
    [Fact]
    public void SerializeAndDeserialize_PreservesNodeColor()
    {
        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        vm.RootGraph.Clear();

        var delay = new DelayNodeViewModel { NodeColor = "purple" };
        vm.RootGraph.Nodes.Add(new StartNodeViewModel());
        vm.RootGraph.Nodes.Add(delay);
        vm.RootGraph.Nodes.Add(new EndNodeViewModel());

        var json = GraphSerializer.Serialize(vm, "Profile");

        Assert.Contains("\"color\": \"purple\"", json);

        using var loadedModbus = new ModbusService();
        var loadedVm = new TestViewModel(loadedModbus, new SlaveManager(loadedModbus));
        GraphSerializer.Deserialize(json, loadedVm);

        var loadedDelay = loadedVm.RootGraph.Nodes.OfType<DelayNodeViewModel>().Single();
        Assert.Equal("purple", loadedDelay.NodeColor);
    }
}
