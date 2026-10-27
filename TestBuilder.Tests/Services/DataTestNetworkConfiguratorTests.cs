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
    public void CustomPlanAcceptsDifferentSubnetMaskAndCardCount()
    {
        var plan = new BenchNetworkPlan { Mask = "255.255.0.0", Addresses = new() {
            new() { Ip = "10.42.1.2", TemporaryIp = "172.20.1.2" },
            new() { Ip = "10.42.2.2", TemporaryIp = "172.20.2.2" } } };
        var adapters = new[] { new BenchAdapter("a","A","","Down",""), new BenchAdapter("b","B","","Up","10.42.1.2") };
        Assert.Null(DataTestNetworkConfigurator.ValidateAssignments(
            new[] { new AdapterAssignment("10.42.1.2","b"), new AdapterAssignment("10.42.2.2","a") }, adapters, plan));
        plan.Addresses[1].TemporaryIp = plan.Addresses[0].Ip;
        Assert.Throws<InvalidOperationException>(plan.Validate);
    }

    [Fact]
    public void ChangingSubnetAllowsOnlyPreviouslyManagedAddressOnSameCard()
    {
        var plan = new BenchNetworkPlan { Addresses = new() { new() { Ip = "10.42.1.2", TemporaryIp = "172.20.1.2" } } };
        var adapters = new[] { new BenchAdapter("a","A","","Up","192.168.0.2") };
        var assignments = new[] { new AdapterAssignment("10.42.1.2", "a") };
        Assert.Null(DataTestNetworkConfigurator.ValidateAssignments(assignments, adapters, plan,
            new Dictionary<string,string> { ["192.168.0.2"] = "a" }));
        Assert.NotNull(DataTestNetworkConfigurator.ValidateAssignments(assignments, adapters, plan,
            new Dictionary<string,string> { ["192.168.0.2"] = "other" }));
    }

    [Theory]
    [InlineData("255.0.255.0", "10.1.1.2")]
    [InlineData("255.255.255.0", "10.1.1.0")]
    [InlineData("255.255.255.0", "10.1.1.255")]
    public void InvalidPlanIsRejected(string mask, string ip)
    {
        var plan = new BenchNetworkPlan { Mask = mask, Addresses = new() { new() { Ip = ip, TemporaryIp = "172.20.1.2" } } };
        Assert.Throws<InvalidOperationException>(plan.Validate);
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
