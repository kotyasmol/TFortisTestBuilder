using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class ReportTemplateTests
{
    [Fact]
    public async Task ProjectsSelectedMeasurementsInTemplateOrderAndKeepsWireHeaders()
    {
        var context = Context();
        context.SetVariable("SelfTest.OutputPrefix", "DutFinal");
        context.SetVariable("SelfTest.Parsed", true);
        context.SetVariable("SelfTest.ReportFields", new Dictionary<string, string>
        {
            ["init_ok"] = "1", ["link_1"] = "0", ["firmvare_vers"] = "1021"
        });
        context.SetVariable("Heater1.Current", 552);
        context.SetVariable("PoE.B.Millivolts", 55163);
        context.SetVariable("PoE.B.Passed", true);
        context.SetVariable("DataTest.Port0.Passed", true);
        context.SetVariable("ArpClear.BatStdOut", "verbose diagnostics");
        context.AddReportEntry("напряжение АКБ", true, "15.075");

        var template = new ReportTemplate
        {
            Fields =
            [
                Entry("самотестирование", ReportValueFormat.BooleanText),
                Variable("Heater1.Current", "Ток первого нагревательного элемента"),
                Variable("PoE.B.Millivolts", "Тест PoE B на линии 0", ReportValueFormat.PoeMillivolts, "PoE.B.Passed"),
                Variable("DataTest.Port0.Passed", "Передача данных порт 0", ReportValueFormat.BooleanNumber),
                new ReportField { Source = ReportSourceKind.TestType, Enabled = false },
                Entry("Версия прошивки") with { Enabled = false },
                Entry("link_0"),
                Entry("напряжение АКБ", ReportValueFormat.FixedSix)
            ]
        };

        Assert.Equal(
            "test_result=true=1\r\nstand_id=true=APK03-07\r\nserial_num=true=3200123\r\nsession=true=test-session\r\n" +
            "самотестирование=true=true\r\nТок первого нагревательного элемента=true=552\r\n" +
            "Тест PoE B на линии 0: пройдено, 55.163V=true=55163.000000\r\nПередача данных порт 0=true=1\r\n" +
            "Тип проверки=true=production\r\nlink_0=true=0\r\nнапряжение АКБ=true=15.075000\r\n",
            await Build(context, template));
    }

    [Fact]
    public async Task SelectedVariablesUseTheSameWireValuesAsAutomaticVariables()
    {
        var automatic = await Build(Variables(), null);
        var selected = await Build(Variables(), new ReportTemplate
        {
            Fields =
            [
                Variable("Measured.Scalar", "Measured.Scalar"),
                Variable("Measured.Vector", "Measured.Vector"),
                Variable("Measured.Snapshot", "Measured.Snapshot"),
                Variable("Measured.False", "Measured.False"),
                Variable("Measured.Disabled", "Measured.Disabled") with { Enabled = false },
                Variable("Measured.Missing", "Measured.Missing")
            ]
        });

        foreach (var name in new[] { "Measured.Scalar", "Measured.Vector", "Measured.Snapshot", "Measured.False" })
        {
            var line = Assert.Single(automatic.Split("\r\n").Where(line => line.StartsWith(name + "=", StringComparison.Ordinal)));
            Assert.Contains(line, selected.Split("\r\n"));
        }

        Assert.DoesNotContain("Measured.Disabled", selected);
        Assert.DoesNotContain("Measured.Missing", selected);
        Assert.DoesNotContain("Measured.Unlisted", selected);
        Assert.StartsWith("test_result=true=1\r\n", selected);

        static TestContext Variables()
        {
            var context = Context();
            context.SetVariable("Measured.Scalar", 12.5m);
            context.SetVariable("Measured.Vector", new[] { 1, 2 });
            context.SetVariable("Measured.Snapshot", new Dictionary<string, string> { ["link_1"] = "1" });
            context.SetVariable("Measured.False", false);
            context.SetVariable("Measured.Disabled", "disabled");
            context.SetVariable("Measured.Unlisted", "unlisted");
            return context;
        }
    }

    [Fact]
    public async Task CanKeepAutomaticResultsWhenSelectingVariablesAndHideOrReplaceSpecificEntries()
    {
        var context = Context();
        context.AddReportEntry("Первая проверка", true, "1");
        context.AddReportEntry("Скрытая проверка", false, "Не пройдена");
        context.AddReportEntry("Исходное название", true, "2");
        context.AddReportEntry("Другая проверка", true, "3");
        context.AddReportEntry("Напряжение= канал", true, "старое");
        context.SetVariable("Measured.Voltage", 54);
        context.SetVariable("Debug", "не включено");
        var template = new ReportTemplate
        {
            IncludeAutomaticEntries = true,
            Fields =
            [
                Entry("Скрытая проверка") with { Enabled = false },
                Entry("Исходное название") with { Label = "Новое название" },
                Variable("Measured.Voltage", "Напряжение: канал")
            ]
        };

        Assert.Equal(
            "test_result=true=0\r\nstand_id=true=APK03-07\r\nserial_num=true=3200123\r\nsession=true=test-session\r\n" +
            "Тип проверки=true=production\r\nПервая проверка=true=1\r\nДругая проверка=true=3\r\n" +
            "Новое название=true=2\r\nНапряжение: канал=true=54\r\n" +
            "Причина остановки=false=Скрытая проверка: Не пройдена\r\n",
            await Build(context, template));
    }

    [Theory]
    [InlineData("test_result")]
    [InlineData("session")]
    [InlineData("BuildReport.Success")]
    [InlineData("SendReport.RawResponse")]
    [InlineData("ReportDelivery.Status")]
    public void ReportServiceVariablesCannotBeSelected(string key)
    {
        Assert.False(ReportTemplate.CanSelectVariable(key));
        var template = new ReportTemplate { Fields = [Variable(key, "Переменная")] };
        Assert.Contains("зарезервирован", template.Validate());
    }

    [Fact]
    public async Task HiddenFailedChecksCannotChangeVerdictOrHideEarlyStopReason()
    {
        var context = Context();
        context.AddReportEntry("Контроль тока", false, "Ток вне допуска");
        context.CriticalFailure = new ExecutionFailure("Контроль тока", "Ток вне допуска");
        context.SetVariable("Heater1.Passed", false);
        context.SetVariable("Report", "stale previous payload");
        context.SetVariable("BuildReport.VariableName", "AnotherReport");
        context.SetVariable("AnotherReport", "another generated payload");

        var template = new ReportTemplate
        {
            Fields =
            [
                Entry("Контроль тока") with { Enabled = false },
                Variable("Heater2.Passed", "Нагреватель 2", ReportValueFormat.BooleanNumber),
                Variable("Report", "Предыдущая копия"),
                Variable("AnotherReport", "Копия другого отчёта")
            ]
        };

        var report = await Build(context, template);
        Assert.StartsWith("test_result=true=0\r\n", report);
        Assert.Contains("Причина остановки=false=Шаг «Контроль тока»: Ток вне допуска\r\n", report);
        Assert.DoesNotContain("Контроль тока=false=", report);
        Assert.DoesNotContain("Нагреватель 2", report);
        Assert.DoesNotContain("stale previous payload", report);
        Assert.DoesNotContain("another generated payload", report);
        Assert.False(context.GetVariable<bool>("BuildReport.DevicePassed"));
    }

    [Fact]
    public async Task EmergencyReportUsesOriginalRunErrorAfterCleanupUpdatesFailure()
    {
        var context = Context();
        context.IsCleanup = true;
        context.HasCriticalError = true;
        context.SetVariable("Execution.Error", "Шаг «DataTest»: потеря связи");
        context.CriticalFailure = new ExecutionFailure("Выключение", "Повтор отключения");

        var report = await Build(context, new ReportTemplate());

        Assert.Contains("Причина остановки=false=Шаг «DataTest»: потеря связи\r\n", report);
        Assert.DoesNotContain("Повтор отключения", report);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CombinedResultRequiresAllPassesButCanReportAKnownFailure(bool firstPassed)
    {
        var context = Context();
        context.SetVariable("Heater1.Passed", firstPassed);
        var template = new ReportTemplate
        {
            Fields =
            [
                new ReportField
                {
                    Source = ReportSourceKind.AllPassed, Key = "Heater1.Passed;Heater2.Passed",
                    Label = "Ток нагревателей", Format = ReportValueFormat.BooleanNumber
                }
            ]
        };

        var report = await Build(context, template);
        if (firstPassed)
            Assert.DoesNotContain("Ток нагревателей", report);
        else
            Assert.Contains("Ток нагревателей=false=0\r\n", report);
    }

    [Fact]
    public async Task InvalidTemplateCannotEmitADuplicateReservedHeaderOrReuseOldPayload()
    {
        var context = Context();
        context.SetVariable("Report", "old payload");
        var template = new ReportTemplate
        {
            Fields = [Variable("Device.Serial", " serial_num ")]
        };

        var result = await Step(template).ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.False(context.Variables.ContainsKey("Report"));
        Assert.Contains("зарезервирована", context.GetVariable<string>("BuildReport.Error"));
    }

    private static ReportField Entry(string key, ReportValueFormat format = ReportValueFormat.Text) =>
        new() { Source = ReportSourceKind.Entry, Key = key, Format = format };

    private static ReportField Variable(string key, string label,
        ReportValueFormat format = ReportValueFormat.Text, string statusKey = "") =>
        new() { Source = ReportSourceKind.Variable, Key = key, Label = label, Format = format, StatusKey = statusKey };

    private static TestContext Context()
    {
        var context = new TestContext(new RegisterState());
        context.SetVariable("SerialNumber", 3200123);
        return context;
    }

    private static BuildTestReportStep Step(ReportTemplate? template) => new(NullLogger.Instance,
        "Report", "APK03-07", "SerialNumber", "test-session", "production", true, template);

    private static async Task<string> Build(TestContext context, ReportTemplate? template)
    {
        Assert.Equal(StepResult.True, await Step(template).ExecuteAsync(context, CancellationToken.None));
        return context.GetVariable<string>("Report")!;
    }
}
