using System.Text.Json;
using TestBuilder.Serialization;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.Tests.Support;
using TestBuilder.Tests.StepTests;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

[Collection(AppSettingsTestCollection.Name)]
public class ProfilePurposeTests
{
    public static IEnumerable<object[]> ProductionFiles() => Directory.EnumerateFiles(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../profiles")), "*.json").Select(p => new object[] { p });

    [Theory, MemberData(nameof(ProductionFiles))]
    public void EveryProductionBaselineCompilesWithoutHardwareAccess(string path)
    {
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        GraphSerializer.Deserialize(File.ReadAllText(path), vm);
        Assert.False(vm.IsDiagnosticProfile);
        using var compiler = new GraphCompiler(bus, NullLogger.Instance);
        Assert.NotNull(compiler.Compile(vm.RootGraph));
    }
    [Theory]
    [InlineData(null, "diagnostic", "diagnostic")]
    [InlineData(null, "production", "production")]
    [InlineData("production", "diagnostic", "production")]
    [InlineData("diagnostic", "production", "diagnostic")]
    public void ExplicitPurposeAndLegacyReportSemanticsSurviveSave(string? purpose, string reportType, string expected)
    {
        var dto = new GraphDto { ProfilePurpose = purpose, Name = "Произвольное имя", Nodes = new()
        {
            new NodeDto { Id = "1", Type = "Build Test Report", TestType = reportType }
        }};
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        GraphSerializer.Deserialize(JsonSerializer.Serialize(dto), vm);
        Assert.Equal(expected, vm.RootGraph.ProfilePurpose);
        using var saved = JsonDocument.Parse(GraphSerializer.Serialize(vm, "Другое имя"));
        Assert.Equal(expected, saved.RootElement.GetProperty("profilePurpose").GetString());
    }

    [Fact]
    public void UnknownPurposeDoesNotSilentlyBecomeProduction()
    {
        Assert.Throws<InvalidOperationException>(() => ProfilePurpose.Resolve(new GraphDto { ProfilePurpose = "future-unknown" }));
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        var original = new StartNodeViewModel(); vm.RootGraph.Nodes.Add(original);
        var before = GraphSerializer.Serialize(vm, "Original");
        var incoming = JsonSerializer.Serialize(new GraphDto { ProfilePurpose = "future-unknown", Nodes = new()
        {
            new NodeDto { Id = "0", Type = "End" }
        }});
        Assert.Throws<InvalidOperationException>(() => GraphSerializer.Deserialize(incoming, vm));
        Assert.Same(original, Assert.Single(vm.RootGraph.Nodes));
        Assert.Equal(before, GraphSerializer.Serialize(vm, "Original"));
    }
}
