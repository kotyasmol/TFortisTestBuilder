using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using System.Text;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Http;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class LabelPrintingTests
{
    [Theory]
    [InlineData(0, "3200428", "03200428")]
    [InlineData(1, "612447", "00612447")]
    [InlineData(2, "12345678", "12345678")]
    public async Task PrintsSelectedTemplate_RepeatKeepsSnapshot_AndFailureDoesNotReplaceIt(int kind, string serial, string barcode)
    {
        var printer = new Printer();
        using var vm = new LabelPrintingViewModel(NullLogger.Instance, () => true, new Http(), printer)
        { SelectedType = kind, SerialNumber = serial, MacAddress = "C0:11:A6:20:01:AC", Copies = 2 };
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
    public async Task LookupFillsFieldsWithoutPrinting_AndUsesReadOnlyEndpoint()
    {
        var http = new Http(); var printer = new Printer();
        using var vm = new LabelPrintingViewModel(NullLogger.Instance, () => true, http, printer) { SerialNumber = "3200428" };
        await vm.FindBySerialCommand.ExecuteAsync(null);
        Assert.Contains("getdevicehistorylist?findParam=serial&findVal=3200428", http.Url);
        Assert.Equal("C0:11:A6:20:01:AC", vm.MacAddress);
        Assert.Empty(printer.Jobs);
        await vm.FindByMacCommand.ExecuteAsync(null);
        Assert.Contains("findParam=mac&findVal=C0%3A11%3AA6%3A20%3A01%3AAC", http.Url);
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
                allowed = true; vm.RefreshAvailability(); Dispatcher.UIThread.RunJobs();
                window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                window.CaptureRenderedFrame()!.Save("/private/tmp/tfortis-label-printing.png");
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
                { SerialNumber = "42", Copies = 1 };
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
            foreach (var window in new Window[] { new DiscoverySettingsWindow(), new NetworkPlanWindow(), new LabelTemplatesWindow() })
            {
                try {
                    window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                    window.CaptureRenderedFrame()!.Save("/private/tmp/tfortis-" + window.GetType().Name + ".png");
                    Assert.True(window.Bounds.Width > 0);
                } finally { window.Close(); }
            }
        }, CancellationToken.None);
    }

    private sealed class Http : IHttpRequestService
    {
        public string Url = "";
        public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout, CancellationToken token)
        {
            Url = url;
            return Task.FromResult(HttpRequestResult.Success(200,
                "[{\"device\":{\"serial\":3200428,\"mac\":\"C0:11:A6:20:01:AC\"}}]", TimeSpan.Zero));
        }
    }
    private sealed class Printer : IRawLabelPrinter
    {
        public List<string> Jobs { get; } = new();
        public RawLabelPrintResult Print(string printerName, byte[] bytes, CancellationToken token)
        { Jobs.Add(Encoding.UTF8.GetString(bytes)); return RawLabelPrintResult.Ok(); }
    }
}
