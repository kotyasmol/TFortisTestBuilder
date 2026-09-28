using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class CompactReportTests
{
    [Fact]
    public async Task FreshSnapshotDoesNotReuseMissingFieldsFromPreviousRead()
    {
        var context = Context();
        context.SetVariable("SelfTest.OutputPrefix", "Dut");
        context.SetVariable("SelfTest.Parsed", true);
        context.SetVariable("Dut.sfp_7_pres", "1");
        context.SetVariable("SelfTest.ReportFields", new Dictionary<string, string> { ["init_ok"] = "1" });
        Assert.DoesNotContain("SFP7", await Build(context));
    }

    [Theory]
    [InlineData(54000, true)]
    [InlineData(47000, false)]
    public async Task PoeReportKeepsFinalMeasurementAndFailureAfterRetries(int voltage, bool passed)
    {
        var context = Context();
        context.CurrentSlaveId = 3;
        context.RegisterState.Update(3, 1403, (ushort)voltage);
        var step = new CheckRegisterRangeStep(1, 1403, 53000, 56000, NullLogger.Instance,
            useCurrentSlaveId: true, readAttempts: 3);
        Assert.Equal(passed ? StepResult.True : StepResult.False, await step.ExecuteAsync(context, CancellationToken.None));
        var entry = Assert.Single(context.ReportEntries);
        Assert.Equal("напряжение PoE B, устройство 3", entry.Name);
        Assert.Equal(passed, entry.IsSuccess);
        Assert.Contains(passed ? "54.000 В" : "47.000 В", entry.Value);
        Assert.StartsWith(passed ? "test_result=true=1" : "test_result=true=0", await Build(context));
    }

    [Fact]
    public async Task CompactReportKeepsMeasurementsAndLatestSnapshotWithoutDiagnosticsOrAliases()
    {
        var context = Context();
        context.SetVariable("ArpClear.Success", false);
        context.SetVariable("SelfTest.Ok", false);
        context.SetVariable("PrintLabel.Zpl", "raw commands");
        context.SetVariable("Dut.default_mac", "OLD");
        context.SetVariable("SelfTest.OutputPrefix", "DutFinal");
        context.SetVariable("SelfTest.Parsed", true);
        context.SetVariable("DutFinal.default_mac", "C0:11:A6:06:30:9F");
        context.SetVariable("DutFinal.firmvare_vers", "20d");
        context.SetVariable("DutFinal.init_ok", "1");
        context.SetVariable("DutFinal.link_1", "1");
        context.SetVariable("DutFinal.link[0]", "1");
        context.SetVariable("DutFinal.poe_a_1_state", "1");
        context.SetVariable("DutFinal.poe_a_st[0]", "1");
        context.SetVariable("DutFinal.poe_a_1_v", "54.5");
        context.SetVariable("DutFinal.poe_a_v[0]", "54.5");
        context.SetVariable("DutFinal.sfp_7_pres", "1");
        context.SetVariable("DutFinal.sfp_pres[7]", "1");
        context.AddReportEntry("Передача данных sfp", true, "TX 990; RX 990; потери 0%");
        var report = await Build(context);
        Assert.StartsWith("test_result=true=1\r\n", report);
        Assert.Contains("Версия прошивки=true=20d\r\n", report);
        Assert.Contains("MAC адрес=true=C0:11:A6:06:30:9F\r\n", report);
        Assert.Contains("Передача данных sfp=true=", report);
        Assert.Single(report.Split('\n').Where(line => line.StartsWith("link_0=")));
        Assert.Single(report.Split('\n').Where(line => line.StartsWith("poe_a_v_0=")));
        Assert.Single(report.Split('\n').Where(line => line.StartsWith("присутствие SFP7=")));
        Assert.DoesNotContain("ArpClear", report);
        Assert.DoesNotContain("SelfTest.Ok", report);
        Assert.DoesNotContain("raw commands", report);
        Assert.DoesNotContain("OLD", report);
        Assert.DoesNotContain("[0]", report);
    }

    [Fact]
    public async Task RealFailureMakesWholeReportFailEvenWhenGraphContinues()
    {
        var context = Context();
        context.AddReportEntry("Печать этикеток", false, "offline");
        Assert.False(context.HasCriticalError);
        var report = await Build(context);
        Assert.StartsWith("test_result=true=0\r\n", report);
        Assert.Contains("Печать этикеток=false=offline", report);
    }

    [Fact]
    public async Task FailedFinalReadNeverFallsBackToOlderGoodSnapshot()
    {
        var context = Context();
        context.SetVariable("Dut.default_mac", "OLD");
        context.SetVariable("SelfTest.OutputPrefix", "DutFinal");
        context.SetVariable("SelfTest.Parsed", false);
        var report = await Build(context);
        Assert.StartsWith("test_result=true=0", report);
        Assert.Contains("Загрузка тестовой страницы=false=", report);
        Assert.DoesNotContain("OLD", report);
    }

    [Fact]
    public async Task ValidationFailuresRemainFailuresWithNonCriticalSelftest()
    {
        var context = Context();
        context.SetVariable("SelfTest.OutputPrefix", "DutFinal");
        context.SetVariable("SelfTest.Parsed", true);
        context.SetVariable("SelfTest.CheckedRuleCount", 4);
        context.SetVariable("SelfTest.FailedRuleCount", 1);
        context.SetVariable("SelfTest.ValidationSummary", "dev_type mismatch");
        var report = await Build(context);
        Assert.StartsWith("test_result=true=0", report);
        Assert.Contains("Проверки тестовой страницы=false=dev_type mismatch", report);
    }

    private static TestContext Context()
    {
        var context = new TestContext(new RegisterState());
        context.SetVariable("SerialNumber", 612447);
        return context;
    }
    private static async Task<string> Build(TestContext context)
    {
        await new BuildTestReportStep(NullLogger.Instance, "Report", "APK03-07", "SerialNumber", "", "production", false)
            .ExecuteAsync(context, CancellationToken.None);
        return context.GetVariable<string>("Report")!;
    }
}
