using System.Text.Json;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Steps;
using TestBuilder.Serialization;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

public class RemovedNodeSerializationTests
{
    public static IEnumerable<object[]> RemovedTypes()
    {
        string[] types =
        [
            "Check Register Equality", "Проверка равенства",
            "Poll Register", "Опрос регистра",
            "Get UPS Status", "GET_UPS_STATUS", "Получить UPS статус",
            "Get UPS Voltage", "GET_UPS_VOLTAGE", "Получить UPS напряжение",
            "Get IRP Status", "GET_IRP_STATUS", "Получить IRP статус"
        ];
        foreach (var type in types)
            foreach (var legacyField in new[] { false, true })
                yield return new object[] { type, legacyField };
    }

    [Theory]
    [MemberData(nameof(RemovedTypes))]
    public void RemovedTypesAndAliasesAreRejectedWithoutReplacingOpenGraph(string type, bool legacyField)
    {
        var node = new NodeDto { Id = "removed" };
        if (legacyField) node.NodeType = type;
        else node.Type = type;

        AssertRejectedPreservingGraph(new GraphDto { Nodes = [node] }, type);
    }

    [Theory]
    [InlineData("body")]
    [InlineData("bodyGraph")]
    public void RemovedNodesAreRejectedInsideDisabledCleanupAndNestedLoop(string bodyField)
    {
        var body = new GraphDto { Nodes = [new NodeDto { Id = "old", Type = "Poll Register" }] };
        var loop = new NodeDto { Id = "loop", Type = "For Slaves", Body = body };
        var subtest = new NodeDto { Id = "cleanup", Type = "Subtest", IsEnabled = false, RunOnFailure = true };
        var nested = new GraphDto { Nodes = [loop] };
        if (bodyField == "body") subtest.Body = nested;
        else subtest.BodyGraph = nested;

        AssertRejectedPreservingGraph(new GraphDto { Nodes = [subtest] }, "Poll Register");
    }

    [Theory]
    [InlineData("GetUpsStatus", "Wait Variable Until")]
    [InlineData("GetUpsVoltage", "WAIT_VARIABLE_UNTIL")]
    [InlineData("GetIrpStatus", "Ожидание переменной")]
    [InlineData(" getirpstatus ", "Wait Variable Until")]
    public void RemovedPollActionsAreRejectedWithHttpGetMigrationHint(string pollAction, string type)
    {
        var body = new GraphDto { Nodes = [new NodeDto { Id = "wait", Type = type, PollAction = pollAction }] };
        var profile = new GraphDto { Nodes = [new NodeDto { Id = "subtest", Type = "Subtest", BodyGraph = body }] };

        AssertRejectedPreservingGraph(profile, "HttpGet");
    }

    [Fact]
    public void WaitWithoutPollSettingsUsesUniversalHttpDefaults()
    {
        using var modbus = new ModbusService();
        using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        GraphSerializer.Deserialize("""{"nodes":[{"id":"wait","type":"Wait Variable Until"}]}""", vm);

        var wait = Assert.IsType<WaitVariableUntilNodeViewModel>(Assert.Single(vm.RootGraph.Nodes));
        Assert.Equal("HttpGet", wait.PollAction);
        Assert.Equal("/api/getUpsStatus", wait.Endpoint);
        Assert.Equal(HttpResponseValueType.Integer, wait.ResponseType);
        Assert.Equal(new[] { "HttpReachable", "SelftestSnapshot", "HttpGet", "None" }, wait.PollActions);
    }

    [Theory]
    [InlineData("PSW_2G6F_plus_full_algorithm.json")]
    [InlineData("PSW_UPS_Box_8x2Pro_full_algorithm_polling.json")]
    [InlineData("PSW_UPS_Box_8x2Pro_parallel_start.json")]
    [InlineData("manual_label_printing.json")]
    [InlineData("station_dashboard_demo.json")]
    public void CurrentProfilesLoadAndCompile(string file)
    {
        using var modbus = new ModbusService();
        using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "profiles", file));
        var json = File.ReadAllText(path);
        GraphSerializer.Deserialize(json, vm);
        using var compiler = new GraphCompiler(modbus, NullLogger.Instance);

        Assert.NotNull(compiler.Compile(vm.RootGraph).StartNode);
        var remainingTypes = vm.AvailableNodes.Select(node => node.Title).ToArray();
        foreach (var data in RemovedTypes())
            Assert.DoesNotContain((string)data[0], remainingTypes);
    }

    private static void AssertRejectedPreservingGraph(GraphDto profile, string expectedMessage)
    {
        using var modbus = new ModbusService();
        using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        var subtest = new SubtestNodeViewModel { Name = "Открытый подтест" };
        vm.RootGraph.Nodes.Add(subtest);
        vm.RootGraph.DeviceModel = "Текущая модель";
        vm.RootGraph.ConfigurationName = "Текущая конфигурация";
        var before = GraphSerializer.Serialize(vm, "Открытый профиль");

        var error = Assert.Throws<InvalidOperationException>(() =>
            GraphSerializer.Deserialize(JsonSerializer.Serialize(profile), vm));

        Assert.Contains(expectedMessage, error.Message);
        Assert.Contains("Обновите", error.Message);
        Assert.Same(subtest, Assert.Single(vm.RootGraph.Nodes));
        Assert.Equal(before, GraphSerializer.Serialize(vm, "Открытый профиль"));
    }
}
