using System.Linq;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

public class GraphClipboardTests
{
    [Fact]
    public void CopyAndPaste_ForSlavesNode_ClonesBodyBoundaryNodes()
    {
        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        vm.RootGraph.Clear();

        var forSlaves = new ForEachSlaveNodeViewModel();
        vm.RootGraph.Nodes.Add(forSlaves);
        vm.RootGraph.SelectedNodes.Add(forSlaves);

        vm.CopyNodes();
        vm.PasteNodes();

        var pastedForSlaves = vm.RootGraph.Nodes.OfType<ForEachSlaveNodeViewModel>().Last();

        Assert.Equal(2, vm.RootGraph.Nodes.OfType<ForEachSlaveNodeViewModel>().Count());
        Assert.Contains(pastedForSlaves.BodyGraph.Nodes, n => n is BodyStartNodeViewModel);
        Assert.Contains(pastedForSlaves.BodyGraph.Nodes, n => n is BodyEndNodeViewModel);
    }
}
