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
        var dataTestSubtest = viewModel.RootGraph.Nodes
            .OfType<SubtestNodeViewModel>()
            .Single(node => node.Name == "10. DataTest портов 0..9");
        var networkNode = dataTestSubtest.BodyGraph.Nodes
            .OfType<ConfigureNetworkAdaptersNodeViewModel>()
            .Single();
        Assert.Equal(10, networkNode.AdaptersText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.DoesNotContain("Name=", networkNode.AdaptersText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("74563CA71264", networkNode.AdaptersText, StringComparison.OrdinalIgnoreCase);
    }
}
