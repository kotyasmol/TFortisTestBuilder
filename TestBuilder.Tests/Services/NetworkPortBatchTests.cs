using System.Text.Json;
using TestBuilder.Tests.Support;
using TestBuilder.Services;

namespace TestBuilder.Tests.Services;

public class NetworkPortBatchTests
{
    internal static BenchNetworkPlan Plan(int ports = 24, int mbps = 1000) =>
        new NetworkPortBatch(ports, 1, "Порт", "Медь", mbps, "10.42.0.2", "172.20.0.2", true)
            .AppendTo(new BenchNetworkPlan { Mask = "255.255.0.0", Addresses = new() });

    [Theory]
    [InlineData(12)] [InlineData(24)] [InlineData(48)] [InlineData(300)]
    public void AnyStandSizeGetsCompletePairsUniqueAddressesAndAnIndependentDraft(int ports)
    {
        var source = new BenchNetworkPlan { Mask = "255.255.0.0", Addresses = new() };
        var result = new NetworkPortBatch(ports, 1, "LAN", "Медь", 1000, "10.42.0.2", "172.20.0.2", true).AppendTo(source);
        result.Validate(); Assert.Empty(source.Addresses);
        Assert.Equal(ports, result.Addresses.Count);
        Assert.Equal(ports / 2, result.BuildDataTestPairs().Split(Environment.NewLine).Length);
        Assert.Equal(ports * 2, result.Addresses.SelectMany(a => new[] { a.Ip, a.TemporaryIp }).Distinct().Count());
        Assert.Equal($"LAN {ports}", result.Addresses[^1].Name);
        Assert.All(result.Addresses, a => { Assert.Equal(1000, a.TestMbps); Assert.True(a.IncludeInDataTest); });
        var next = result.CreateNextAddress(); result.Addresses.Add(next); result.Validate(); // No hidden 247-card limit.
        Assert.Equal(ports + 1, result.Addresses.Count);
    }

    [Theory]
    [InlineData("odd")]
    [InlineData("subnet")]
    [InlineData("duplicate")]
    [InlineData("name")]
    [InlineData("too-many")]
    public void InvalidBatchNeverPartlyChangesTheOriginalPlan(string problem)
    {
        var source = NetworkTestPlans.TenPorts(); source.Addresses[0].Name = "Порт 11";
        var before = JsonSerializer.Serialize(source);
        var batch = new NetworkPortBatch(12, 12, "Порт", "Медь", 100, "192.168.0.12", "192.0.2.12", true);
        batch = problem switch
        {
            "odd" => batch with { Count = 13 },
            "subnet" => batch with { FirstIp = "192.168.0.250" },
            "duplicate" => batch with { FirstTemporaryIp = "192.168.0.12" },
            "name" => batch with { FirstNumber = 11 },
            _ => batch with { Count = NetworkPortBatch.MaximumBatchSize + 1 }
        };
        Assert.Throws<InvalidOperationException>(() => batch.AppendTo(source));
        Assert.Equal(before, JsonSerializer.Serialize(source));
    }

    [Fact]
    public void OddPortCanBeAddedExplicitlyWithoutPretendingItWasPaired()
    {
        var result = new NetworkPortBatch(1, 25, "SFP", "SFP", 1000, "10.42.0.26", "172.20.0.26", false).AppendTo(Plan());
        Assert.Equal(25, result.Addresses.Count);
        Assert.False(result.Addresses[^1].IncludeInDataTest); Assert.Equal("", result.Addresses[^1].Pair);
        Assert.Equal(12, result.BuildDataTestPairs().Split(Environment.NewLine).Length);
    }

    [Fact]
    public void BundledLargerPlansAreExternalAndHaveTheExpectedNumberOfPairs()
    {
        var presets = NetworkPresetCatalog.LoadBundled();
        foreach (var ports in new[] { 12, 24, 48 })
        {
            var preset = Assert.Single(presets.Where(p => p.Id == $"ports-{ports}-1000"));
            preset.Validate(); Assert.Equal(ports, preset.Plan.Addresses.Count);
            Assert.Equal(ports / 2, preset.Plan.BuildDataTestPairs().Split(Environment.NewLine).Length);
        }
    }

    [Theory]
    [InlineData(24)] [InlineData(48)]
    public async Task WindowsAddressConfigurationVisitsEveryCardAndConfirmsItBeforeSaving(int ports)
    {
        var plan = Plan(ports);
        var changes = plan.Addresses.Select((a, i) => new DataTestNetworkConfigurator.AdapterAddressChange(
            new BenchAdapter($"card-{i}", $"Card {i}", "", "Down", ""), a.Ip, a.TemporaryIp, false,
            Array.Empty<DataTestNetworkConfigurator.AdapterAddress>())).ToArray();
        var commands = 0; var confirmed = 0; var saved = 0;
        var error = await DataTestNetworkConfigurator.ApplyAddressChangesAsync(changes, plan.Mask,
            plan.Addresses.Select(a => a.Ip).ToArray(), () => { Assert.Equal(ports, confirmed); saved++; },
            new Progress<string>(_ => { }),
            (_, _) => { commands++; return Task.FromResult<string?>(null); },
            (_, address, _, _) => { Assert.NotNull(address); confirmed++; return Task.FromResult(true); }, default);
        Assert.Null(error); Assert.Equal(ports, commands); Assert.Equal(ports, confirmed); Assert.Equal(1, saved);
    }
}
