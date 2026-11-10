using System.Text.Json.Nodes;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

public class ProReportProfileTests
{
    private const string ProProfile = "PSW+UPS-Box 8x2Pro · Параллельная проверка с SFP · 10.11.json";

    [Theory]
    [InlineData(false, "17. Отчёт о проверке")]
    [InlineData(true, "Отчёт при ошибке")]
    public async Task ReportTemplateCanBeOptedIntoWithoutChangingTheProductionGraph(bool failed, string branch)
    {
        using var modbus = new ModbusService();
        using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        var source = File.ReadAllText(TestProfilePaths.Find(ProProfile));
        GraphSerializer.Deserialize(source, vm);
        Assert.Equal(50, vm.RootGraph.ReportTemplate!.Fields.Count);
        vm.RootGraph.ReportTemplate = null;
        var original = JsonNode.Parse(GraphSerializer.Serialize(vm, "8x2Pro"))!.AsObject();

        vm.RootGraph.ReportTemplate = new ReportTemplate
        {
            Fields =
            [
                new ReportField
                {
                    Source = ReportSourceKind.Entry, Key = "напряжение АКБ",
                    Label = "Напряжение АКБ", Format = ReportValueFormat.FixedSix
                },
                new ReportField { Source = ReportSourceKind.TestType }
            ]
        };
        var saved = GraphSerializer.Serialize(vm, "8x2Pro");
        var configured = JsonNode.Parse(saved)!.AsObject();
        Assert.Equal(GraphSerializer.CatalogSerialsStartType, configured["nodes"]![0]!["type"]!.GetValue<string>());
        Assert.True(configured.Remove("reportTemplate"));
        configured["nodes"]![0]!["type"] = original["nodes"]![0]!["type"]!.GetValue<string>();
        Assert.True(JsonNode.DeepEquals(original, configured));

        GraphSerializer.Deserialize(saved, vm);
        using var compiler = new GraphCompiler(modbus, NullLogger.Instance)
        {
            ProfileReportTemplate = vm.RootGraph.ReportTemplate
        };
        Assert.NotNull(compiler.Compile(vm.RootGraph));

        var graph = vm.RootGraph.Nodes.OfType<SubtestNodeViewModel>().Single(node => node.Name == branch).BodyGraph;
        var build = graph.Nodes.OfType<BuildTestReportNodeViewModel>().Single();
        var step = Assert.IsType<TestBuilder.Domain.Steps.BuildTestReportStep>(
            compiler.Compile(graph, build, singleNode: true).StartNode.Step);
        var context = new TestContext(new RegisterState());
        context.SetVariable("SerialNumber", 3200001);
        context.SetVariable("DataTest.Debug", "internal diagnostics");
        if (failed)
            context.AddReportEntry("Питание DUT", false, "нет ответа");
        else
            context.AddReportEntry("напряжение АКБ", true, "24.2");

        Assert.Equal(StepResult.True, await step.ExecuteAsync(context, CancellationToken.None));
        var report = context.GetVariable<string>("TestReportText")!;
        Assert.DoesNotContain("DataTest.Debug", report);
        if (failed)
        {
            Assert.StartsWith("test_result=true=0\r\n", report);
            Assert.Contains("Причина остановки=false=", report);
            Assert.DoesNotContain("Напряжение АКБ", report);
        }
        else
        {
            Assert.StartsWith("test_result=true=1\r\n", report);
            Assert.Contains("Напряжение АКБ=true=24.200000\r\nТип проверки=true=production\r\n", report);
        }

        GraphSerializer.Deserialize(source, vm);
        Assert.Equal(50, vm.RootGraph.ReportTemplate!.Fields.Count);
    }
}
