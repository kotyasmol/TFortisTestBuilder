using System.Text.Json;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

public class DataTestModeTests
{
    [Theory]
    [InlineData("Программный генератор трафика", "SoftwarePcap")]
    [InlineData(" программный генератор трафика ", "SoftwarePcap")]
    [InlineData("SoftwarePcap", "SoftwarePcap")]
    [InlineData("pcap", "SoftwarePcap")]
    [InlineData("Software", "SoftwarePcap")]
    [InlineData("TYPE_SOFT_GEN", "SoftwarePcap")]
    [InlineData(null, "SoftwarePcap")]
    [InlineData(" ", "SoftwarePcap")]
    [InlineData("Unsupported", "Unsupported")]
    public async Task LegacyModesRoundTripCanonically_UnknownModesRemainInvalid(string? mode, string expected)
    {
        // An empty port list makes execution stop before opening pcap/NICs.
        var context = new TestContext(new RegisterState());
        var step = new RunDataTestStep(NullLogger.Instance, mode!, 10000, 1514,
            43962, 15000, 100, 5000, 500, 0, 1, 2, true,
            Array.Empty<DataTestPortConfig>(), "DataTest", true);
        Assert.Equal(StepResult.False, await step.ExecuteAsync(context, CancellationToken.None));
        Assert.Equal(expected, context.GetVariable<string>("DataTest.Mode"));

        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        var json = JsonSerializer.Serialize(new {
            name = "Legacy DataTest", nodes = new[] { new {
                id = "0", type = "Run Data Test", mode,
                portsText = "Медь,192.168.0.2,192.168.0.3,100",
                disabledPortsText = "SFP,192.168.0.8,192.168.0.9,1000"
            } }, connections = Array.Empty<object>()
        });
        GraphSerializer.Deserialize(json, vm);
        var restored = Assert.Single(vm.Nodes.OfType<RunDataTestNodeViewModel>());
        Assert.Equal(expected, restored.Mode);
        Assert.Equal("SFP,192.168.0.8,192.168.0.9,1000", restored.DisabledPortsText);
        // Also cover direct/programmatic node values entering the save boundary.
        restored.Mode = mode!;
        var saved = GraphSerializer.Serialize(vm, "Legacy DataTest");
        using var document = JsonDocument.Parse(saved);
        Assert.Equal(expected, document.RootElement.GetProperty("nodes")[0].GetProperty("mode").GetString());
        GraphSerializer.Deserialize(saved, vm);
        Assert.Equal(expected, Assert.Single(vm.Nodes.OfType<RunDataTestNodeViewModel>()).Mode);
    }
}
