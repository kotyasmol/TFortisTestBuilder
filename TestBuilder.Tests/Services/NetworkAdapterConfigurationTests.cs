using TestBuilder.Services;

namespace TestBuilder.Tests.Services;

public class NetworkAdapterConfigurationTests
{
    private static BenchAdapter Card(string id = "a", string ip = "192.168.0.2") => new(id, "Ethernet \"2\"; text", "001122334455", "Up", ip, 1000);
    private static NetworkAdapterIpv4Configuration Static(string ip = "192.168.0.20") => new("static", ip, "255.255.255.0", "", "keep", "");

    [Theory]
    [InlineData("192.168.0.20 & echo x", "255.255.255.0", "")]
    [InlineData("192.168.0.0", "255.255.255.0", "")]
    [InlineData("192.168.0.255", "255.255.255.0", "")]
    [InlineData("192.168.0.20", "255.0.255.0", "")]
    [InlineData("192.168.0.20", "255.255.255.0", "192.168.1.1")]
    [InlineData("192.168.0.20", "255.255.255.0", "192.168.0.20")]
    public async Task InvalidInputNeverRunsAnOsCommand(string address, string mask, string gateway)
    {
        var commands = 0;
        var op = new NetworkAdapterOperations(() => new[] { Card() }, (_, _) => { commands++; return Task.FromResult<string?>(null); }, () => null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => op.ApplyIpv4("a", new("static", address, mask, gateway, "keep", "")));
        Assert.Equal(0, commands);
    }

    [Fact]
    public async Task FullIpv4AndDnsAreAppliedOnlyToTheRequestedStableId()
    {
        var calls = new List<string[]>(); var card = Card();
        var op = new NetworkAdapterOperations(() => new[] { card, Card("b", "192.168.0.3") }, (args, _) =>
        { calls.Add(args.ToArray()); return Task.FromResult<string?>(null); }, () => null);
        await op.ApplyIpv4("a", new("static", "192.168.0.20", "255.255.255.0", "192.168.0.1", "static", "1.1.1.1, 8.8.8.8"));
        Assert.Equal(3, calls.Count);
        Assert.All(calls, call => Assert.Contains("name=" + card.Name, call));
        Assert.Contains("gateway=192.168.0.1", calls[0]); Assert.Contains("mask=255.255.255.0", calls[0]);
        Assert.Contains("address=1.1.1.1", calls[1]); Assert.Contains("address=8.8.8.8", calls[2]); Assert.Contains("index=2", calls[2]);
        Assert.DoesNotContain(calls.SelectMany(c => c), argument => argument == "text"); // The interface name is one argument.
        calls.Clear(); await op.ApplyIpv4("a", new("dhcp", "bad ignored draft", "", "", "dhcp", ""));
        Assert.Equal(2, calls.Count); Assert.All(calls, c => Assert.Contains("source=dhcp", c));
        calls.Clear(); await op.ApplyIpv4("a", new("keep", "", "", "", "keep", "")); Assert.Empty(calls);
    }

    [Fact]
    public async Task DuplicateIpMissingCardAndDeniedAccessAreRejectedWithoutCommands()
    {
        var commands = 0;
        Task<string?> Execute(IEnumerable<string> _, CancellationToken __) { commands++; return Task.FromResult<string?>(null); }
        var cards = new[] { Card(), Card("b", "192.168.0.20") };
        var op = new NetworkAdapterOperations(() => cards, Execute, () => null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => op.ApplyIpv4("a", Static()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => op.ApplyIpv4("removed", Static("192.168.0.25")));
        op = new NetworkAdapterOperations(() => cards, Execute, () => "Нет прав администратора");
        await Assert.ThrowsAsync<InvalidOperationException>(() => op.ApplyIpv4("a", Static("192.168.0.25")));
        Assert.Equal(0, commands);
    }

    [Fact]
    public async Task DnsFailureReportsThatTheAddressMayAlreadyHaveChanged()
    {
        var calls = 0;
        var op = new NetworkAdapterOperations(() => new[] { Card() }, (_, _) => Task.FromResult(++calls == 1 ? null : "DNS error"), () => null);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => op.ApplyIpv4("a", Static() with { DnsMode = "static", Dns = "1.1.1.1" }));
        Assert.Contains("Часть настроек уже применена", error.Message); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task InvalidDnsAndRenameAreNotSentToWindows()
    {
        var commands = 0;
        var op = new NetworkAdapterOperations(() => new[] { Card() }, (_, _) => { commands++; return Task.FromResult<string?>(null); }, () => null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => op.ApplyIpv4("a", Static() with { DnsMode = "static", Dns = "1.1.1.1, wrong" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => op.Rename("a", "bad\nname")); Assert.Equal(0, commands);
    }

    [Fact]
    public void ActualInventoryReturnsUniqueExistingEthernetOrWifiInterfaces()
    {
        var cards = DataTestNetworkConfigurator.GetComputerAdapters();
        Assert.Equal(cards.Count, cards.Select(c => c.Id).Distinct().Count());
        Assert.All(cards, c => Assert.False(string.IsNullOrWhiteSpace(c.Id)));
        Assert.All(DataTestNetworkConfigurator.GetAdapters(), c => Assert.True(c.CanUseForDataTest));
    }

    [Fact]
    public async Task RemovedCardAfterAddressChangeStopsDnsAndReportsPartialChange()
    {
        var cards = new[] { Card() }; var calls = 0;
        var op = new NetworkAdapterOperations(() => cards, (_, _) =>
        { calls++; cards = Array.Empty<BenchAdapter>(); return Task.FromResult<string?>(null); }, () => null);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => op.ApplyIpv4("a", Static() with { DnsMode = "dhcp" }));
        Assert.Contains("больше не найдена", failure.Message);
        Assert.Contains("Часть настроек уже применена", failure.Message); Assert.Equal(1, calls);
    }
}
