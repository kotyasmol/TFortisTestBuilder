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

    [Fact]
    public void Psw2G6FSuggestion_MapsActivePortsByNegotiatedSpeed_AndKeepsDownAdaptersAsSpare()
    {
        var speeds = new[] { 1000d, 1000, 100, 0, 0, 100, 100, 100, 100, 100 };
        var adapters = DataTestNetworkConfigurator.BenchIps.Select((ip, index) =>
            new BenchAdapter($"adapter-{index}", $"Ethernet {index}", $"MAC{index}",
                speeds[index] == 0 ? "Down" : "Up", ip, speeds[index])).ToArray();
        var internet = new BenchAdapter("internet", "INET", "INETMAC", "Up", "10.160.24.159", 1000);

        var (assignments, error) = DataTestNetworkConfigurator.SuggestPsw2G6FAssignments(
            [.. adapters, internet], []);

        Assert.Null(error);
        Assert.Equal(new[] { "adapter-2", "adapter-5", "adapter-6", "adapter-7",
            "adapter-8", "adapter-9", "adapter-0", "adapter-1", "adapter-3", "adapter-4" },
            assignments!.Select(assignment => assignment.AdapterId));
        Assert.Null(DataTestNetworkConfigurator.ValidateAssignments(assignments!, [.. adapters, internet]));
    }

    [Fact]
    public void Psw2G6FSuggestion_DoesNotGuessWhenLinkSpeedsDoNotMatch()
    {
        var adapters = DataTestNetworkConfigurator.BenchIps.Select((ip, index) =>
            new BenchAdapter($"adapter-{index}", $"Ethernet {index}", $"MAC{index}",
                "Up", ip, 100)).ToArray();

        var (assignments, error) = DataTestNetworkConfigurator.SuggestPsw2G6FAssignments(adapters, []);

        Assert.Null(assignments);
        Assert.Contains("6 поднятых", error);
    }

    [Fact]
    public void ServiceConnection_CannotBeSelectedForIpRewrite()
    {
        var internet = new BenchAdapter("internet", "INET", "INETMAC", "Up", "10.160.24.159", 1000);
        var assignments = ValidAssignments();
        assignments[0] = assignments[0] with { AdapterId = internet.Id };

        Assert.Contains("служебное", DataTestNetworkConfigurator.ValidateAssignments(
            assignments, [.. Adapters, internet]));
    }

    [Fact]
    public void TemporaryAddressOnItsAssignedAdapter_AllowsRetryAfterPartialFailure()
    {
        var adapters = Adapters.ToArray();
        adapters[0] = adapters[0] with { Ipv4 = "192.0.2.2" };

        Assert.Null(DataTestNetworkConfigurator.ValidateAssignments(ValidAssignments(), adapters));
    }

    [Fact]
    public void UnexpectedTemporaryAddressOnSelectedAdapter_IsRejected()
    {
        var adapters = Adapters.ToArray();
        adapters[0] = adapters[0] with { Ipv4 = "192.0.2.3" };

        Assert.Contains("вне сети стенда", DataTestNetworkConfigurator.ValidateAssignments(
            ValidAssignments(), adapters));
    }
}
