using System.Text.Json;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
namespace TestBuilder.Tests.SerializationTests;
public class CompactReportSerializationTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData(", \"includeAllVariables\": false", false)]
    [InlineData(", \"includeAllVariables\": true", true)]
    public void DefaultIsCompactAndExplicitDiagnosticOptInSurvivesRoundTrip(string option, bool expected)
    {
        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        var json = "{\"name\":\"report\",\"nodes\":[{\"id\":\"0\",\"type\":\"Build Test Report\"" + option + "}],\"connections\":[]}";
        GraphSerializer.Deserialize(json, vm);
        Assert.Equal(expected, vm.RootGraph.Nodes.OfType<BuildTestReportNodeViewModel>().Single().IncludeAllVariables);
        GraphSerializer.Deserialize(GraphSerializer.Serialize(vm, "report"), vm);
        Assert.Equal(expected, vm.RootGraph.Nodes.OfType<BuildTestReportNodeViewModel>().Single().IncludeAllVariables);
    }
    [Fact]
    public void CurrentProductionProfileCompilesAndKeepsBothReportsCompact()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "profiles", "PSW_2G6F_plus_full_algorithm.json"));
        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        GraphSerializer.Deserialize(File.ReadAllText(path), vm);
        Assert.NotNull(new GraphCompiler(modbus, NullLogger.Instance).Compile(vm.RootGraph));
        var json = GraphSerializer.Serialize(vm, "profile");
        using var doc = JsonDocument.Parse(json);
        var reports = Nodes(doc.RootElement).Where(n => n.GetProperty("type").GetString() == "Build Test Report").ToArray();
        Assert.Equal(2, reports.Length);
        Assert.All(reports, n => Assert.False(n.GetProperty("includeAllVariables").GetBoolean()));
    }
    private static IEnumerable<JsonElement> Nodes(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("type", out _)) yield return node;
            foreach (var p in node.EnumerateObject())
                foreach (var nested in Nodes(p.Value)) yield return nested;
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var item in node.EnumerateArray())
                foreach (var nested in Nodes(item)) yield return nested;
    }
}
