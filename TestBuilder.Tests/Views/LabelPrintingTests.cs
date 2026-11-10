using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using System.Text;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class LabelPrintingTests
{
    [Theory]
    [InlineData(0, "3200428", "003200428")]
    [InlineData(0, "612447", "000612447")]
    [InlineData(1, "12345678", "12345678")]
    public async Task PrintsSelectedTemplate_RepeatKeepsSnapshot_AndFailureDoesNotReplaceIt(int kind, string serial, string barcode)
    {
        var printer = new Printer();
        using var vm = new LabelPrintingViewModel(NullLogger.Instance, () => true, new Http(), printer)
        { SelectedType = kind, SerialNumber = serial, MacAddress = "C0:11:A6:20:01:AC", Copies = 2, PrinterName = "Label printer" };
        await vm.PrintCommand.ExecuteAsync(null);
        Assert.True(vm.CanRepeat);
        Assert.Contains(barcode, printer.Jobs.Single());
        var original = printer.Jobs.Single();
        vm.SerialNumber = "bad"; vm.MacAddress = "bad";
        await vm.PrintCommand.ExecuteAsync(null);
        Assert.Single(printer.Jobs);
        await vm.RepeatLastCommand.ExecuteAsync(null);
        Assert.Equal(original, printer.Jobs.Last());
        vm.Copies = 0;
        await vm.RepeatLastCommand.ExecuteAsync(null);
        Assert.Equal(2, printer.Jobs.Count);
    }

    [Fact]
    public async Task AmbiguousShortSerialWithoutProfileDoesNotPrint()
    {
        var printer = new Printer();
        using var vm = new LabelPrintingViewModel(NullLogger.Instance, () => true, new Http(), printer)
            { SerialNumber = "42", MacAddress = "C0:11:A6:20:01:AC" };
        await vm.PrintCommand.ExecuteAsync(null);
        Assert.Empty(printer.Jobs);
        Assert.Contains("полный серийный", vm.Status);
    }

    [Fact]
    public async Task FindsAndPrintsCatalogModelWithoutWorkstationTemplate()
    {
        var printer = new Printer();
        var http = new Http
        {
            Response = """[{"device":{"serial":3300579,"mac":"C0:11:A6:21:02:43"}}]"""
        };
        using var vm = new LabelPrintingViewModel(NullLogger.Instance, () => true, http, printer,
            profiles: () => Array.Empty<GraphProfile>())
        { SerialNumber = "3300579", PrinterName = "Label printer" };

        await vm.FindBySerialCommand.ExecuteAsync(null);
        Assert.Equal("PSW+Box 8x2Pro", vm.SelectedProductName);
        Assert.Equal("C0:11:A6:21:02:43", vm.MacAddress);
        await vm.PrintCommand.ExecuteAsync(null);
        var job = Assert.Single(printer.Jobs);
        Assert.Contains("^FDSN: 3300579^FS", job);
        Assert.Contains("^FD>:003300579^FS", job);
    }

    [Fact]
    public async Task FormatsUseProfileNameAndModelParameters_RepeatKeepsOriginalProfile()
    {
        var device = "PSW-2G6F+";
        var printer = new Printer();
        using var vm = new LabelPrintingViewModel(NullLogger.Instance, () => true, new Http(), printer, () => device)
            { SerialNumber = "12447", MacAddress = "C0:11:A6:06:01:AC", PrinterName = "Label printer" };
        Assert.Equal(2, vm.LabelOptions.Count);
        Assert.Contains(vm.Preview!.Elements, e => e.Text == device);
        await vm.PrintCommand.ExecuteAsync(null);
        Assert.Contains("000612447", Assert.Single(printer.Jobs));
        Assert.Contains("^FDPSW-2G6F+", printer.Jobs[0]);
        device = "PSW+UPS-Box 8x2Pro";
        vm.RefreshTemplates();
        await vm.RepeatLastCommand.ExecuteAsync(null);
        Assert.Equal(printer.Jobs[0], printer.Jobs[1]);
        vm.SerialNumber = "428";
        await vm.PrintCommand.ExecuteAsync(null);
        Assert.Contains("003200428", printer.Jobs[2]);
        Assert.Contains("^FDPSW+UPS-Box 8x2Pro", printer.Jobs[2]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LookupUsesOneIdentifier_AndSelectsFoundProductEvenWithoutItsProfile(bool byMac)
    {
        var http = new Http(); var printer = new Printer();
        using var vm = new LabelPrintingViewModel(NullLogger.Instance, () => true, http, printer,
            profiles: () => new[] { new GraphProfile("existing", "Existing", "PSW-2G6F+") }) { PrinterName = "Label printer" };
        if (byMac)
        {
            vm.MacAddress = "c011.a620.01ac";
            await vm.FindByMacCommand.ExecuteAsync(null);
            Assert.Contains("getdevicehistorylist?findParam=mac&findVal=C0%3A11%3AA6%3A20%3A01%3AAC", http.Url);
        }
        else
        {
            vm.SerialNumber = "3200428";
            await vm.FindBySerialCommand.ExecuteAsync(null);
            Assert.Contains("getdevicehistorylist?findParam=serial&findVal=3200428", http.Url);
        }
        Assert.Equal("3200428", vm.SerialNumber);
        Assert.Equal("C0:11:A6:20:01:AC", vm.MacAddress);
        Assert.Equal("PSW+UPS-Box 8x2Pro", vm.SelectedProductName);
        Assert.Empty(printer.Jobs);
        await vm.PrintCommand.ExecuteAsync(null);
        Assert.Contains("^FDPSW+UPS-Box 8x2Pro", Assert.Single(printer.Jobs));
    }

    [Fact]
    public async Task ShortSerialUsesSelectedProductRange()
    {
        var http = new Http();
        using var vm = new LabelPrintingViewModel(NullLogger.Instance, () => true, http, new Printer(), profiles: () => new[] {
            new GraphProfile("one", "One", "PSW+UPS-Box 8x2Pro"), new GraphProfile("two", "Two", "PSW-2G6F+")
        }) { SerialNumber = "00428" };

        await vm.FindBySerialCommand.ExecuteAsync(null);
        Assert.Empty(http.Url);
        Assert.Contains("выберите изделие", vm.Status);

        vm.SelectedProductName = "PSW+UPS-Box 8x2Pro";
        await vm.FindBySerialCommand.ExecuteAsync(null);
        Assert.Contains("findParam=serial&findVal=3200428", http.Url);
        Assert.Equal("3200428", vm.SerialNumber);
        Assert.Equal("C0:11:A6:20:01:AC", vm.MacAddress);
    }

    [Theory]
    [InlineData("https://server.example/", "/api/api.svc/getdevicehistorylist")]
    [InlineData("https://server.example/site/api", "/site/api/api.svc/getdevicehistorylist")]
    [InlineData("https://server.example/api/Api.svc/", "/api/Api.svc/getdevicehistorylist")]
    [InlineData("https://server.example/api/api.svc/getdevicehistorylist?findParam=mac", "/api/api.svc/getdevicehistorylist")]
    public async Task LookupAcceptsServerAndEndpointUrls(string server, string expectedPath)
    {
        var http = new Http();
        await new LabelLookupService(http).FindAsync(server, "3200428", false, CancellationToken.None);
        Assert.Equal("https://server.example" + expectedPath + "?findParam=serial&findVal=3200428", http.Url);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task TestPrintUsesStandardTemplateWithZeros_AndKeepsProductRepeat(int kind)
    {
        var previousDefault = AppSettings.Instance.DefaultPrinterName;
        var http = new Http(); var printer = new Printer();
        try
        {
            using var vm = new LabelPrintingViewModel(NullLogger.Instance, () => true, http, printer, profiles: () => Array.Empty<GraphProfile>())
                { SelectedType = kind, Copies = 4 };
            AppSettings.Instance.DefaultPrinterName = "Workstation printer";

            await vm.PrintTestLabelCommand.ExecuteAsync(null);
            var template = LabelTemplateCatalog.Defaults()[0];
            template.BarcodePrefix = "0000";
            var expected = kind == 0 ? template.Render("00000000", 0, "00:00:00:00:00:00")
                : PrintLabelStep.BuildEpl("00000000");
            Assert.Equal(expected, Assert.Single(printer.Jobs));
            Assert.Equal("Workstation printer", Assert.Single(printer.Names));
            Assert.Empty(http.Url);
            Assert.False(vm.CanRepeat);
            Assert.Empty(vm.SerialNumber);
            Assert.Empty(vm.MacAddress);

            vm.SelectedType = 1; vm.PrinterName = "Explicit printer"; vm.SerialNumber = "12345678";
            await vm.PrintCommand.ExecuteAsync(null);
            var product = printer.Jobs.Last();
            await vm.PrintTestLabelCommand.ExecuteAsync(null);
            Assert.Equal("Explicit printer", printer.Names.Last());
            await vm.RepeatLastCommand.ExecuteAsync(null);
            Assert.Equal(product, printer.Jobs.Last());
        }
        finally { AppSettings.Instance.DefaultPrinterName = previousDefault; }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("NoAuth")]
    [InlineData("[{\"device\":{\"serial\":3200429,\"mac\":\"C0:11:A6:20:01:AC\"}}]")]
    [InlineData("[{\"device\":{\"serial\":3200428}}]")]
    public void LookupRejectsMissingOrMismatchedData(string body)
        => Assert.ThrowsAny<Exception>(() => LabelLookupService.Parse(body, "3200428", false));

    [Fact]
    public async Task ViewShowsEditableFieldsAndBlocksDuringProductionRun()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var allowed = true;
            using var vm = new LabelPrintingViewModel(NullLogger.Instance, () => allowed, new Http(), new Printer());
            var view = new LabelPrintingView { DataContext = vm };
            var window = new Window { Width = 1100, Height = 800, Content = view };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var input = view.FindControl<TextBox>("LabelSerialInput")!;
                input.Text = "3200428";
                Assert.Equal("3200428", vm.SerialNumber);
                allowed = false; vm.RefreshAvailability(); Dispatcher.UIThread.RunJobs();
                Assert.False(input.IsEffectivelyEnabled);
                Assert.False(vm.PrintCommand.CanExecute(null));
                Assert.False(view.FindControl<Button>("PrintTestLabelButton")!.IsEffectivelyEnabled);
                allowed = true; vm.RefreshAvailability(); Dispatcher.UIThread.RunJobs();
                window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                window.CaptureRenderedFrame()!.Save(Path.Combine(Path.GetTempPath(), "tfortis-label-printing.png"));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CustomTemplateLookupPrintAndRepeatDoNotDependOnBuiltInModels()
    {
        var original = TestBuilder.Services.AppSettings.Instance.LabelTemplates;
        try
        {
            var template = new TestBuilder.Services.LabelTemplateDefinition {
                Name = "Custom", UseShortSerial = true, RequiresMac = false, MatchServerSerial = true,
                SerialOffset = 8000000, ShortSerialMax = 9999, ShortSerialDigits = 4, BarcodePrefix = "88",
                Command = "^XA^FD{{Name}} {{Serial}} {{Barcode}}^FS^XZ" };
            TestBuilder.Services.AppSettings.Instance.LabelTemplates = new() { template };
            var data = LabelLookupService.Parse("[{\"device\":{\"serial\":8000042}}]", "8000042", false);
            Assert.Equal(0, data.Kind);
            var printer = new Printer();
            using var vm = new LabelPrintingViewModel(NullLogger.Instance, () => true, new Http(), printer)
                { SerialNumber = "42", Copies = 1, PrinterName = "Label printer" };
            await vm.PrintCommand.ExecuteAsync(null);
            Assert.Equal("^XA^FDCustom 8000042 880042^FS^XZ", Assert.Single(printer.Jobs));
            template.Command = "CHANGED";
            await vm.RepeatLastCommand.ExecuteAsync(null);
            Assert.Equal(printer.Jobs[0], printer.Jobs[1]);
        }
        finally { TestBuilder.Services.AppSettings.Instance.LabelTemplates = original; }
    }

    [Fact]
    public async Task ConfigurationEditorsRender()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var modbus = new ModbusService();
            using var profile = new TestViewModel(modbus, new SlaveManager(modbus));
            foreach (var window in new Window[] { new DiscoverySettingsWindow(), new NetworkPlanWindow(), new LabelTemplatesWindow(),
                new CatalogSerialsWindow(profile, new EquipmentModel("sample", "PSW+Box 8x2Pro")) })
            {
                try {
                    window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                    window.CaptureRenderedFrame()!.Save(Path.Combine(Path.GetTempPath(), $"tfortis-{window.GetType().Name}.png"));
                    Assert.True(window.Bounds.Width > 0);
                } finally { window.Close(); }
            }
        }, CancellationToken.None);
    }

    private sealed class Http : IHttpRequestService
    {
        public string Url = "";
        public string Response = "[{\"device\":{\"serial\":3200428,\"mac\":\"C0:11:A6:20:01:AC\"}}]";
        public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout, CancellationToken token)
        {
            Url = url;
            return Task.FromResult(HttpRequestResult.Success(200, Response, TimeSpan.Zero));
        }
    }
    private sealed class Printer : IRawLabelPrinter
    {
        public List<string> Jobs { get; } = new();
        public List<string> Names { get; } = new();
        public RawLabelPrintResult Print(string printerName, byte[] bytes, CancellationToken token)
        { Names.Add(printerName); Jobs.Add(Encoding.UTF8.GetString(bytes)); return RawLabelPrintResult.Ok(); }
    }
}
