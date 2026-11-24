using TestBuilder.Tests.Support;
using TestBuilder.Services;

namespace TestBuilder.Tests.Services;

public class NetworkPlanAddressTests
{
    [Fact]
    public void SuggestedAddressesFollowTheCustomMaskWithoutChangingExistingRows()
    {
        var plan = new BenchNetworkPlan { Mask = "255.255.0.0", Addresses = new()
        {
            new() { Ip = "10.42.1.255", TemporaryIp = "172.20.1.255" }
        } };
        var next = plan.CreateNextAddress();
        Assert.Equal("10.42.2.0", next.Ip); Assert.Equal("172.20.2.0", next.TemporaryIp);
        Assert.Single(plan.Addresses);
        plan.Addresses.Add(next); plan.Validate();
    }

    [Fact]
    public void SuggestionsWrapInsideTheSubnetAndSkipNetworkAndBroadcast()
    {
        var plan = new BenchNetworkPlan { Mask = "255.255.255.252", Addresses = new()
        {
            new() { Ip = "10.0.0.2", TemporaryIp = "172.20.0.2" }
        } };
        var next = plan.CreateNextAddress();
        Assert.Equal("10.0.0.1", next.Ip); Assert.Equal("172.20.0.1", next.TemporaryIp);
        plan.Addresses.Add(next); plan.Validate();
        var full = plan.CreateNextAddress();
        Assert.Equal("", full.Ip); Assert.Equal("", full.TemporaryIp);
    }

    [Fact]
    public void SuggestionsAvoidAddressesInBothColumns()
    {
        var plan = new BenchNetworkPlan { Addresses = new()
        {
            new() { Ip = "192.168.0.4", TemporaryIp = "192.168.0.3" },
            new() { Ip = "192.168.0.2", TemporaryIp = "192.0.2.2" }
        } };
        var next = plan.CreateNextAddress();
        Assert.Equal("192.168.0.5", next.Ip); Assert.Equal("192.0.2.3", next.TemporaryIp);
        plan.Addresses.Add(next); plan.Validate();
    }

    [Theory]
    [InlineData(false, "Адрес карты")]
    [InlineData(true, "Временный адрес")]
    public void MissingAddressNamesTheExactFieldAndRow(bool temporary, string field)
    {
        var plan = NetworkTestPlans.TenPorts();
        if (temporary) plan.Addresses[3].TemporaryIp = ""; else plan.Addresses[3].Ip = "";
        var error = Assert.Throws<InvalidOperationException>(plan.Validate);
        Assert.Contains("Строка 4", error.Message); Assert.Contains(field, error.Message); Assert.Contains("заполните", error.Message);
    }

    [Fact]
    public void DuplicateAddressExplainsBothLocations()
    {
        var plan = NetworkTestPlans.TenPorts();
        plan.Addresses.Add(new() { Ip = "192.168.0.12", TemporaryIp = "192.168.0.2" });
        var error = Assert.Throws<InvalidOperationException>(plan.Validate);
        Assert.Contains("Строка 11", error.Message); Assert.Contains("Временный адрес", error.Message);
        Assert.Contains("строке 1", error.Message); Assert.Contains("Адрес карты", error.Message);
    }

    [Theory]
    [InlineData("126.255.255.255", "1.0.0.0")]
    [InlineData("223.255.255.255", "128.0.0.1")]
    public void WideSubnetSuggestionsSkipReservedAddressRanges(string current, string expected)
    {
        var plan = new BenchNetworkPlan { Mask = "128.0.0.0", Addresses = new()
        {
            new() { Ip = current, TemporaryIp = "172.20.1.2" }
        } };
        var next = plan.CreateNextAddress();
        Assert.Equal(expected, next.Ip);
        plan.Addresses.Add(next); plan.Validate();
    }
}
