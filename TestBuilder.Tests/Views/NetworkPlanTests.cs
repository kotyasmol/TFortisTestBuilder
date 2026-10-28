using System.Text.Json;
using TestBuilder.Services;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class NetworkPlanTests
{
    private static BenchNetworkPlan Plan() => new()
    {
        Addresses = new()
        {
            new() { Ip = "10.1.1.2", TemporaryIp = "192.0.2.2", Name = "Линия A", Role = "Медь", Pair = "Пара A", IncludeInDataTest = true },
            new() { Ip = "10.1.1.3", TemporaryIp = "192.0.2.3", Name = "Линия B", Role = "Медь", Pair = "Пара A", IncludeInDataTest = true },
            new() { Ip = "10.1.1.4", TemporaryIp = "192.0.2.4", Role = "SFP", TestMbps = 1000 }
        }
    };

    [Fact]
    public void MetadataRoundTripsAndOnlySelectedPairsAreExported()
    {
        var restored = JsonSerializer.Deserialize<BenchNetworkPlan>(JsonSerializer.Serialize(Plan()))!;
        Assert.Equal("Линия A", restored.Addresses[0].Name);
        Assert.Equal("Медь", restored.Addresses[0].Role);
        Assert.Equal("Пара A,10.1.1.2,10.1.1.3,100", restored.BuildDataTestPairs());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("speed")]
    [InlineData("delimiter")]
    [InlineData("null")]
    public void InvalidPairCannotReplaceNodeSettings(string problem)
    {
        var previous = AppSettings.Instance.NetworkPlan;
        try
        {
            var plan = Plan();
            if (problem == "missing") plan.Addresses[1].IncludeInDataTest = false;
            if (problem == "speed") plan.Addresses[1].TestMbps = 1000;
            if (problem == "delimiter") plan.Addresses[0].Pair = "bad,pair";
            if (problem == "null") plan.Addresses[0].Name = null!;
            AppSettings.Instance.NetworkPlan = plan;
            var node = new RunDataTestNodeViewModel { PortsText = "original", AllowGigabit = true };
            node.ImportNetworkPlanCommand.Execute(null);
            Assert.Equal("original", node.PortsText);
            Assert.True(node.AllowGigabit);
            Assert.NotEmpty(node.NetworkPlanStatus);
        }
        finally { AppSettings.Instance.NetworkPlan = previous; }
    }

    [Fact]
    public void LinkIdentificationUsesStableIdAndPreservesAssignments()
    {
        var cards = DataTestNetworkConfigurator.BenchIps.Select((ip, i) => new BenchAdapter($"card-{i}", $"Card {i}", $"MAC{i}", "Up", ip, 1000)).ToArray();
        var vm = new NetworkSetupViewModel(() => cards, () => true);
        vm.Rows[0].SelectedAdapter = cards[1];
        vm.RememberLinksCommand.Execute(null);
        cards = cards.Select(c => c.Id == "card-1" ? c with { Name = "Renamed", Status = "Down", SpeedMbps = 0 } : c).ToArray();
        vm.CompareLinksCommand.Execute(null);
        Assert.Contains("Renamed", vm.LinkChanges);
        Assert.Contains("MAC1", vm.LinkChanges);
        Assert.DoesNotContain("несколько", vm.LinkChanges);
        Assert.Equal("card-1", vm.Rows[0].SelectedAdapter!.Id);
        Assert.Equal("card-0", vm.Rows[1].SelectedAdapter!.Id);
    }
}
