using TestBuilder.Services;

namespace TestBuilder.Tests.Services;

public class DataTestNetworkConfiguratorTests
{
    private static readonly BenchAdapter[] Adapters = DataTestNetworkConfigurator.BenchIps
        .Select((ip, index) => new BenchAdapter($"adapter-{index}", $"Ethernet {index}",
            $"0000000000{index:00}", "Down", index == 0 ? ip : string.Empty))
        .ToArray();

    private static AdapterAssignment[] ValidAssignments() => DataTestNetworkConfigurator.BenchIps
        .Select((ip, index) => new AdapterAssignment(ip, Adapters[index].Id)).ToArray();

    [Fact]
    public void ValidAssignments_AcceptDownAdapters()
    {
        Assert.Null(DataTestNetworkConfigurator.ValidateAssignments(ValidAssignments(), Adapters));
    }

    [Fact]
    public void DuplicateAdapter_IsRejectedBeforeChangingAnyAddress()
    {
        var assignments = ValidAssignments();
        assignments[1] = assignments[1] with { AdapterId = assignments[0].AdapterId };

        Assert.Contains("нескольких", DataTestNetworkConfigurator.ValidateAssignments(assignments, Adapters));
    }

    [Fact]
    public void AddressOnUnselectedAdapter_IsRejected()
    {
        var extra = new BenchAdapter("management", "Management", "001122334455", "Up", "192.168.0.5");

        Assert.Contains("192.168.0.5", DataTestNetworkConfigurator.ValidateAssignments(
            ValidAssignments(), [.. Adapters, extra]));
    }
}
