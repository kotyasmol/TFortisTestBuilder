using System.Linq;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

public class SubtestSerializationTests
{
    [Fact]
    public void SerializeAndDeserialize_PreservesSubtestBodyGraph()
    {
        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        vm.RootGraph.Clear();

        var subtest = new SubtestNodeViewModel
        {
            Name = "Selftest",
            Description = "Проверка внутренних ошибок устройства",
            IsEnabled = true,
            StopOnError = false,
            RunOnFailure = true
        };

        vm.RootGraph.Nodes.Add(new StartNodeViewModel());
        vm.RootGraph.Nodes.Add(subtest);
        vm.RootGraph.Nodes.Add(new EndNodeViewModel());

        var json = GraphSerializer.Serialize(vm, "Profile");

        Assert.Contains("\"type\": \"Subtest\"", json);
        Assert.Contains("\"bodyGraph\"", json);
        Assert.Contains("\"name\": \"Selftest\"", json);
        Assert.Contains("\"runOnFailure\": true", json);

        using var loadedModbus = new ModbusService();
        var loadedVm = new TestViewModel(loadedModbus, new SlaveManager(loadedModbus));

        GraphSerializer.Deserialize(json, loadedVm);

        var loadedSubtest = loadedVm.RootGraph.Nodes.OfType<SubtestNodeViewModel>().Single();

        Assert.Equal("Selftest", loadedSubtest.Name);
        Assert.Equal("Проверка внутренних ошибок устройства", loadedSubtest.Description);
        Assert.False(loadedSubtest.StopOnError);
        Assert.True(loadedSubtest.RunOnFailure);
        Assert.Contains(loadedSubtest.BodyGraph.Nodes, n => n is StartNodeViewModel);
        Assert.Contains(loadedSubtest.BodyGraph.Nodes, n => n is EndNodeViewModel);
    }

    [Fact]
    public void SerializeAndDeserialize_PreservesLiveReadForModbusCheck()
    {
        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        vm.RootGraph.Clear();

        var check = new CheckRegisterRangeNodeViewModel
        {
            SlaveId = 1,
            Address = 100,
            Min = 10,
            Max = 20,
            UseCurrentSlaveId = true,
            LiveRead = true
        };

        vm.RootGraph.Nodes.Add(new StartNodeViewModel());
        vm.RootGraph.Nodes.Add(check);
        vm.RootGraph.Nodes.Add(new EndNodeViewModel());

        var json = GraphSerializer.Serialize(vm, "Profile");

        Assert.Contains("\"type\": \"Check Register Range\"", json);
        Assert.Contains("\"liveRead\": true", json);

        using var loadedModbus = new ModbusService();
        var loadedVm = new TestViewModel(loadedModbus, new SlaveManager(loadedModbus));

        GraphSerializer.Deserialize(json, loadedVm);

        var loadedCheck = loadedVm.RootGraph.Nodes.OfType<CheckRegisterRangeNodeViewModel>().Single();

        Assert.Equal(1, loadedCheck.SlaveId);
        Assert.Equal(100, loadedCheck.Address);
        Assert.Equal(10, loadedCheck.Min);
        Assert.Equal(20, loadedCheck.Max);
        Assert.True(loadedCheck.UseCurrentSlaveId);
        Assert.True(loadedCheck.LiveRead);
    }
}
