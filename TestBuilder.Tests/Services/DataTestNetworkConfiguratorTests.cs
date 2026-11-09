using TestBuilder.Services;
using static TestBuilder.Services.DataTestNetworkConfigurator;

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedSwap_RestoresBothAddressesIncludingTheirMasks(bool failSavingBindings)
    {
        var a = new BenchAdapter("a", "a", "", "Up", "192.168.0.2");
        var b = new BenchAdapter("b", "b", "", "Up", "192.168.0.3");
        var originalA = new AdapterAddress(a.Ipv4, "255.255.255.0");
        var originalB = new AdapterAddress(b.Ipv4, "255.255.0.0");
        var changes = new[] { new AdapterAddressChange(a, b.Ipv4, "192.0.2.3", false, [originalA]),
            new AdapterAddressChange(b, a.Ipv4, "192.0.2.2", false, [originalB]) };
        var addresses = new Dictionary<string, AdapterAddress> { ["a"] = originalA, ["b"] = originalB };
        var saveCalled = false;
        var failureInjected = false;

        var error = await ApplyAddressChangesAsync(changes, "255.255.255.0", [a.Ipv4, b.Ipv4], () =>
            {
                saveCalled = true;
                throw new IOException("settings write failed");
            }, new Progress<string>(), RunAsync,
            (id, address, dhcp, _) => Task.FromResult(!dhcp && addresses[id] == address), CancellationToken.None);

        Assert.Equal(failSavingBindings, saveCalled);
        Assert.Contains("Прежние настройки IPv4 восстановлены", error);
        Assert.Equal(originalA, addresses["a"]);
        Assert.Equal(originalB, addresses["b"]);

        Task<string?> RunAsync(string[] arguments, CancellationToken _)
        {
            Assert.Contains("store=persistent", arguments);
            Assert.Equal("set", arguments[2]);
            var id = arguments.Single(a => a.StartsWith("name="))[5..];
            var address = arguments.Single(a => a.StartsWith("address="))[8..];
            var mask = arguments.Single(a => a.StartsWith("mask="))[5..];
            if (!failSavingBindings && !failureInjected && id == "b" && address == originalA.Ip)
            {
                failureInjected = true;
                return Task.FromResult<string?>("address command failed");
            }
            if (addresses.Any(a => a.Key != id && a.Value.Ip == address))
                return Task.FromResult<string?>("duplicate address");
            addresses[id] = new(address, mask);
            return Task.FromResult<string?>(null);
        }
    }
}
