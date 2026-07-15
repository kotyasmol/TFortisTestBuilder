using TestBuilder.Domain.Modbus;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

public class ProfileCommandTests
{
    [Fact]
    public void NewProfileCommand_ClearsRootGraphAndResetsProfileState()
    {
        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));

        vm.RootGraph.Nodes.Add(new DelayNodeViewModel());
        vm.RootGraph.Title = "Loaded profile";

        vm.NewProfileCommand.Execute(null);

        Assert.Empty(vm.RootGraph.Nodes);
        Assert.Empty(vm.RootGraph.Connections);
        Assert.Null(vm.SelectedProfile);
        Assert.Equal("Новый профиль", vm.CurrentProfileName);
        Assert.Equal("Полный тест", vm.RootGraph.Title);
        Assert.Equal("Полный тест", vm.CurrentGraphPath);
    }
}
