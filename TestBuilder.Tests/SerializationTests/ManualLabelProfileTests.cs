using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Graph;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

public class ManualLabelProfileTests
{
    [Fact]
    public void ManualLabelProfile_LoadsAsStandaloneFourCopyGraph()
    {
        var profilePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "profiles",
            "manual_label_printing.json"));
        var json = File.ReadAllText(profilePath);

        using var modbus = new ModbusService();
        var viewModel = new TestViewModel(modbus, new SlaveManager(modbus));

        var profileName = GraphSerializer.Deserialize(json, viewModel);

        Assert.Equal("Ручная печать 4 этикеток PSW+UPS-Box 8x2Pro", profileName);
        Assert.Equal(3, viewModel.RootGraph.Nodes.Count);
        Assert.Equal(2, viewModel.RootGraph.Connections.Count);
        Assert.Single(viewModel.RootGraph.Nodes.OfType<StartNodeViewModel>());
        Assert.Single(viewModel.RootGraph.Nodes.OfType<EndNodeViewModel>());
        var printNode = Assert.Single(viewModel.RootGraph.Nodes.OfType<PrintLabelNodeViewModel>());
        Assert.Equal("TSC TE310", printNode.PrinterName);
        Assert.Equal("SerialNumber", printNode.SerialVariableName);
        Assert.True(printNode.UseManualSerialNumber);
        Assert.Equal(string.Empty, printNode.ManualSerialNumber);
        Assert.Equal(4, printNode.Copies);
        Assert.True(printNode.UseQtProZplFormat);
        Assert.True(printNode.FailOnPrinterError);
        Assert.False(GraphConnectionRequirements.RequiresStandConnection(viewModel.RootGraph));

        printNode.ManualSerialNumber = "3200999";
        var savedJson = GraphSerializer.Serialize(viewModel, profileName);

        using var reloadedModbus = new ModbusService();
        var reloadedViewModel = new TestViewModel(reloadedModbus, new SlaveManager(reloadedModbus));
        GraphSerializer.Deserialize(savedJson, reloadedViewModel);
        var reloadedPrintNode = Assert.Single(
            reloadedViewModel.RootGraph.Nodes.OfType<PrintLabelNodeViewModel>());
        Assert.True(reloadedPrintNode.UseManualSerialNumber);
        Assert.Equal("3200999", reloadedPrintNode.ManualSerialNumber);
        Assert.True(reloadedPrintNode.UseQtProZplFormat);
    }

    [Fact]
    public void ModbusGraph_RequiresStandConnection()
    {
        using var modbus = new ModbusService();
        var viewModel = new TestViewModel(modbus, new SlaveManager(modbus));
        viewModel.RootGraph.Nodes.Add(new ModbusWriteNodeViewModel());

        Assert.True(GraphConnectionRequirements.RequiresStandConnection(viewModel.RootGraph));
    }
}
