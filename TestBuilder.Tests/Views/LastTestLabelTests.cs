using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class LastTestLabelTests
{
    [Fact]
    public async Task ProductList_UsesAllProfileModels_Deduplicates_AndPrintsSelectedName()
    {
        var profiles = new List<GraphProfile> {
            new("one", "First", "New product A"), new("two", "Other configuration", "New product A"),
            new("three", "Third", "New product B"), new("four", "No model") };
        var printer = new Printer();
        using var vm = new LabelPrintingViewModel(NullLogger.Instance, () => true, new Http(), printer,
            () => "New product A", () => profiles) { SerialNumber = "3200428", MacAddress = "02:00:00:00:00:01" };
        Assert.Equal(new[] { "New product A", "New product B" }, vm.ProductNames);
        vm.SelectedProductName = "New product B";
        await vm.PrintCommand.ExecuteAsync(null);
        Assert.Contains("^FDNew product B^FS", Assert.Single(printer.Jobs));
        Assert.DoesNotContain("Other configuration", printer.Jobs[0]);
        profiles.Add(new("five", "New", "Added product"));
        vm.RefreshProductNames();
        Assert.Contains("Added product", vm.ProductNames);
        Assert.Equal("New product B", vm.SelectedProductName);
        profiles.Clear(); vm.RefreshProductNames();
        Assert.False(vm.HasProducts);
        await vm.PrintCommand.ExecuteAsync(null);
        Assert.Single(printer.Jobs);
        Assert.Contains("Выберите изделие", vm.Status);
    }

    [Fact]
    public async Task FailedPrinter_CapturesExactData_Persists_AndRepeatsAfterOtherSelections()
    {
        var template = LabelTemplateCatalog.Defaults()[0];
        var node = new PrintLabelNodeViewModel { LabelTemplate = template, SerialVariableName = "custom.serial",
            SerialShortVariableName = "custom.short", MacVariableName = "custom.mac", Copies = 4 };
        var context = new TestContext(new RegisterState()) { DeviceModel = "Product from run", ProfileName = "Production" };
        context.SetVariable("custom.serial", 3200428); context.SetVariable("custom.short", 428);
        context.SetVariable("custom.mac", "02:00:00:00:00:01");
        var failedPrinter = new Printer { Fail = true };
        Assert.Equal(StepResult.False, await node.CreateStep(NullLogger.Instance, failedPrinter).ExecuteAsync(context, CancellationToken.None));
        var label = Assert.IsType<PreparedLabel>(context.LabelPrinting.Last);
        Assert.Equal("3200428", label.Serial); Assert.Equal("Product from run", label.Template.Name);
        var snapshot = new LastTestLabelSnapshot(DateTimeOffset.Now, "Production", "Product from run", ExecutionStatus.Failed,
            context.GetVariable<string>("PrintLabel.Error")!, label);
        var path = Path.Combine(Path.GetTempPath(), "last-test-label-" + Guid.NewGuid() + ".json");
        try
        {
            new LastTestLabelStore(path).Save(snapshot);
            var restored = new LastTestLabelStore(path).Load()!;
            template.Command = "changed";
            context.SetVariable("custom.serial", 9999999);
            var allowed = true;
            var printer = new Printer { Fail = true };
            using var vm = new LabelPrintingViewModel(NullLogger.Instance, () => allowed, new Http(), printer,
                () => "Different model") { SerialNumber = "600004", MacAddress = "FF:FF:FF:FF:FF:FF", Copies = 1, PrinterName = "Other printer" };
            vm.SetLastTest(restored);
            Assert.True(vm.CanPrintLastTest); Assert.Contains("3200428", vm.LastTestDescription);
            allowed = false; vm.RefreshAvailability();
            await vm.PrintLastTestCommand.ExecuteAsync(null); Assert.Empty(printer.Jobs);
            allowed = true; vm.RefreshAvailability();
            await vm.PrintLastTestCommand.ExecuteAsync(null);
            Assert.True(vm.CanPrintLastTest); Assert.False(vm.CanRepeat);
            printer.Fail = false;
            await vm.PrintLastTestCommand.ExecuteAsync(null);
            Assert.Equal(2, printer.Jobs.Count);
            Assert.Equal(context.GetVariable<string>("PrintLabel.SingleCommand"), printer.Jobs.Last());
            Assert.Equal("Other printer", printer.Name);
            Assert.Contains("Тест завершён с ошибкой", vm.LastTestResult);
            Assert.DoesNotContain("Different model", printer.Jobs.Last());
            vm.SetLastTest(restored with { Label = null, ProfileName = "New run" });
            Assert.False(vm.CanPrintLastTest); Assert.Contains("не были подготовлены", vm.LastTestMessage);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ParallelCapture_IsSharedWithinRun_AndCleanupDoesNotReplaceIt()
    {
        var root = new TestContext(new RegisterState());
        var branch = root.CreateParallelBranch(CancellationToken.None);
        var node = new PrintLabelNodeViewModel { UseManualSerialNumber = true, ManualSerialNumber = "00123456", PrinterName = "" };
        Assert.Equal(StepResult.False, await node.CreateStep(NullLogger.Instance, new Printer()).ExecuteAsync(branch, CancellationToken.None));
        Assert.Equal("00123456", root.LabelPrinting.Last!.Serial);
        branch.IsCleanup = true; node.ManualSerialNumber = "99999999";
        await node.CreateStep(NullLogger.Instance, new Printer()).ExecuteAsync(branch, CancellationToken.None);
        Assert.Equal("00123456", root.LabelPrinting.Last!.Serial);
        Assert.Null(new TestContext(new RegisterState()).LabelPrinting.Last);
        var copy = root.LabelPrinting.Last!; copy.Template.Name = "mutated";
        Assert.NotEqual("mutated", root.LabelPrinting.Last!.Template.Name);
    }

    [Fact]
    public async Task FullRun_PublishesFailedPrint_PartialRunDoesNot_AndNextRunClearsStaleLabel()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(async () =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            vm.RootGraph.Nodes.Clear(); vm.RootGraph.Connections.Clear();
            vm.RootGraph.DeviceModel = "Run model";
            var start = new StartNodeViewModel();
            var print = new PrintLabelNodeViewModel { UseManualSerialNumber = true, ManualSerialNumber = "12345678", PrinterName = "" };
            vm.RootGraph.Nodes.Add(start); vm.RootGraph.Nodes.Add(print);
            vm.RootGraph.Connections.Add(new ConnectionViewModel(start.Output[0], print.Input[0]));
            var events = new List<LastTestLabelSnapshot>();
            vm.LastTestCompleted += events.Add;
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.Equal("12345678", Assert.Single(events).Label!.Serial);
            Assert.Equal(ExecutionStatus.Failed, events[0].Result);
            vm.ConfigureNodeRun(print, true);
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.Single(events);
            print.ManualSerialNumber = "";
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.Equal(2, events.Count); Assert.Null(events.Last().Label);
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ProductPickerAndLastTest_RenderAndRespectAvailability(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var profiles = new[] { new GraphProfile("one", "Profile", "Product A"), new GraphProfile("two", "Profile B", "Product B") };
            var allowed = true;
            using var vm = new LabelPrintingViewModel(NullLogger.Instance, () => allowed, new Http(), new Printer(), profiles: () => profiles);
            vm.SetLastTest(new(DateTimeOffset.Now, "Production", "Product A", ExecutionStatus.Failed, "Printer offline",
                new PreparedLabel(LabelTemplateCatalog.Defaults()[0], "3200428", "02:00:00:00:00:01", "TSC TE310", 4)));
            var view = new LabelPrintingView { DataContext = vm };
            var window = new Window { Width = 1200, Height = 1000, Content = view };
            try
            {
                window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var picker = view.FindControl<ComboBox>("LabelProductPicker")!;
                picker.SelectedItem = "Product B"; Dispatcher.UIThread.RunJobs();
                Assert.Equal("Product B", vm.SelectedProductName);
                var button = view.FindControl<Button>("PrintLastTestButton")!;
                Assert.True(button.IsEffectivelyEnabled);
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
                window.CaptureRenderedFrame()!.Save(Path.Combine(Path.GetTempPath(),
                    "tfortis-last-test-label-" + (dark ? "dark" : "light") + ".png"));
                allowed = false; vm.RefreshAvailability(); Dispatcher.UIThread.RunJobs();
                Assert.False(button.IsEffectivelyEnabled); Assert.False(picker.IsEffectivelyEnabled);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    private sealed class Printer : IRawLabelPrinter
    {
        public bool Fail; public string Name = "";
        public List<string> Jobs { get; } = new();
        public RawLabelPrintResult Print(string name, byte[] bytes, CancellationToken token)
        { Name = name; Jobs.Add(Encoding.UTF8.GetString(bytes)); return Fail ? new(false, 5, "Printer offline") : RawLabelPrintResult.Ok(); }
    }
    private sealed class Http : IHttpRequestService
    {
        public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout, CancellationToken token) =>
            throw new InvalidOperationException("No server request is needed to reprint a test label.");
    }
}
