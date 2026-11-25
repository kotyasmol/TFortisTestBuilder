using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class LabelDataFormattingTests : IDisposable
{
    private readonly FreshUserJourneyTests.CleanWorkstation _workstation = new();
    public void Dispose() => _workstation.Dispose();
    private const string Mac = "C0:11:A6:20:01:AC";
    private static LabelTemplateDefinition Legacy()
    {
        using var stream = typeof(LabelTemplateCatalog).Assembly.GetManifestResourceStream("TestBuilder.label-template-upgrades.json")!;
        return JsonSerializer.Deserialize<List<LabelTemplateDefinition>>(stream)!.Last(t => t.Id == "PswUpsBox8x2Pro" && t.SerialTextFormat == null);
    }
    private static PrintLabelStep Step(LabelTemplateDefinition template, Printer printer, bool last = false) =>
        new(NullLogger.Instance, "Test", "SerialNumber", "SerialShort", "Mac", true, "3200428", Mac, 1, true,
            true, printer, 1000, template: template, useLastTestLabel: last);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EveryCatalogSeriesUsesItsOwnTypeAndShortSerialInBothLayouts(bool visual)
    {
        var stock = LabelTemplateCatalog.Defaults()[0];
        if (visual) stock.Layout = LabelLayoutDefinition.FromTemplate(stock);
        var models = EquipmentCatalog.LoadBundled().Models.Where(m => m.SerialSeries != null && m.LabelCode != null).ToArray();
        Assert.True(models.Length > 10);
        foreach (var model in models)
        {
            var template = LabelPrintContent.ForModel(stock, model, 1);
            var full = Math.Max(model.SerialSeries!.FirstSerialNumber, model.SerialSeries.SerialOffset + 428);
            Assert.True(LabelPrintContent.TryResolveSerial(template, full.ToString(), out var resolved, out var shortSn, out _));
            Assert.Equal(full, resolved);
            var expected = shortSn.ToString("D5");
            var preview = LabelPreview.Parse(template, LabelPrintContent.Render(template, full.ToString(), shortSn, Mac));
            Assert.Null(preview.BarcodeError);
            Assert.Contains(preview.Elements, e => e.Text == "SN: " + expected);
            Assert.Equal(model.LabelCode!.Value.ToString("D3") + expected,
                Assert.Single(preview.Elements.Where(e => e.Bars.Length > 0)).EncodedData);
        }
    }

    [Theory]
    [InlineData("{{ShortSerial:6}}", "{{DeviceCode:2}}{{ShortSerial:4}}", "000428", "320428")]
    [InlineData("{{Serial}}", "{{ServicePrefix}}{{DeviceCode:3}}{{ShortSerial:5}}", "3200428", "003200428")]
    [InlineData("SN-{{ShortSerial:5}}", "{{Serial}}", "SN-00428", "3200428")]
    [InlineData("{{SerialTail:5}}", "{{VariantCode}}-{{Mac}}", "00428", "2-C0:11:A6:20:01:AC")]
    public void TextAndBarcodeAreIndependentAndPreserveLeadingZeros(string textFormat, string barcodeFormat, string text, string data)
    {
        var t = LabelTemplateCatalog.Defaults()[0]; t.VariantCode = 2;
        t.SerialTextFormat = textFormat; t.BarcodeFormat = barcodeFormat;
        Assert.Equal(text, t.RenderSerialText("3200428", 428, Mac));
        Assert.Equal(data, t.RenderBarcode("3200428", 428, Mac));
        var model = EquipmentCatalog.LoadBundled().Models.Single(m => m.LabelCode == 32);
        var adapted = LabelPrintContent.ForModel(t, model, 2);
        Assert.Equal(textFormat, adapted.SerialTextFormat); Assert.Equal(barcodeFormat, adapted.BarcodeFormat);
        Assert.Equal(text, adapted.RenderSerialText("3200428", 428, Mac));
        Assert.Equal(data, adapted.RenderBarcode("3200428", 428, Mac));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RtlBoardsPrintOnlyTheCounterInTextAndTheirTypeInTheBarcode(bool visual)
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
        foreach (var model in EquipmentCatalog.LoadBundled().Models.Where(m => m.RtlReport != null && m.LabelCode.HasValue))
        {
            var source = LabelTemplateCatalog.Defaults()[2];
            if (visual) source.Layout = LabelLayoutDefinition.FromTemplate(source);
            var t = LabelPrintContent.ForModel(source, model, 0);
            var serial = model.LabelCode!.Value.ToString("D3") + "00428";
            var preview = LabelPreview.FromInput(t, serial, "");
            Assert.Null(preview.BarcodeError);
            Assert.Contains(preview.Elements, e => e.Text == "00428");
            Assert.Equal(serial, Assert.Single(preview.Elements.Where(e => e.Bars.Length > 0)).EncodedData);
            Assert.Null(source.SerialTextFormat); Assert.Null(source.BarcodeFormat);
            var printer = new Printer(); var context = new TestContext(new RegisterState()) { ProductionModel = model };
            context.SetVariable("SerialNumber.Assigned", int.Parse(serial));
            var step = new PrintLabelStep(NullLogger.Instance, "Test", "", "", "", true, serial, "", 1, false, true, printer, 1000, template: source);
            Assert.Equal(StepResult.True, await step.ExecuteAsync(context, default));
            Assert.Equal("00428", context.GetVariable<string>("PrintLabel.SerialText"));
            Assert.Equal(serial, context.LabelPrinting.Last!.Serial);
            using var vm = new LabelPrintingViewModel(NullLogger.Instance, () => true, new Http(), printer, () => model.Name)
                { SelectedType = 1, PrinterName = "Test", SerialNumber = serial };
            Assert.Contains(vm.Preview!.Elements, e => e.Text == "00428");
            await vm.PrintCommand.ExecuteAsync(null);
            Assert.Equal(2, printer.Jobs.Count);
            var printed = LabelPreview.Parse(t, printer.Jobs.Last());
            Assert.Contains(printed.Elements, e => e.Text == "00428");
            vm.SerialNumber = "99900428";
            Assert.False(vm.Preview!.HasContent);
            await vm.PrintCommand.ExecuteAsync(null);
            Assert.Equal(2, printer.Jobs.Count);
            context.SetVariable("SerialNumber.Assigned", 99900428);
            var wrong = new PrintLabelStep(NullLogger.Instance, "Test", "", "", "", true, "99900428", "", 1, false, true, printer, 1000, template: source);
            Assert.Equal(StepResult.False, await wrong.ExecuteAsync(context, default));
            Assert.Equal(2, printer.Jobs.Count);
            var explicitFull = source.Copy(); explicitFull.SerialTextFormat = "{{Serial}}";
            Assert.Equal("{{Serial}}", LabelPrintContent.ForModel(explicitFull, model, 0).SerialTextFormat);
            var savedFull = source.Copy(); savedFull.CommitDataFormats();
            var savedPreview = LabelPreview.FromInput(LabelPrintContent.ForModel(savedFull, model, 0), serial, "");
            Assert.Contains(savedPreview.Elements, e => e.Text == serial);
        }
    }

    [Theory]
    [InlineData("{{Barcode}}")]
    [InlineData("{{SerialText}}")]
    [InlineData("{{SerialTail}}")]
    [InlineData("{{ShortSerial:0}}")]
    [InlineData("{{ShortSerial:11}}")]
    [InlineData("{{Mac:5}}")]
    [InlineData("{{Missing}}")]
    [InlineData("{{Serial:5}")]
    public void InvalidOrRecursiveRulesAreRejected(string format)
    {
        var t = LabelTemplateCatalog.Defaults()[0]; t.BarcodeFormat = format;
        Assert.Throws<InvalidOperationException>(t.Validate);
    }

    [Theory]
    [InlineData("{{ShortSerial:2}}")]
    [InlineData("{{DeviceCode:1}}{{ShortSerial}}")]
    [InlineData("^XA")]
    [InlineData("{{Cabinet}}")]
    public async Task InvalidDataOrInsufficientPrecisionNeverReachThePrinter(string format)
    {
        var t = LabelTemplateCatalog.Defaults()[0]; t.BarcodeFormat = format; t.CabinetName = "Шкаф";
        var printer = new Printer(); var context = new TestContext(new RegisterState());
        Assert.Equal(StepResult.False, await Step(t, printer).ExecuteAsync(context, default));
        Assert.Empty(printer.Jobs); Assert.False(string.IsNullOrWhiteSpace(context.GetVariable<string>("PrintLabel.Error")));
    }

    [Fact]
    public void ABoardWithoutSeriesCanExplicitlyPrintTheLastDigitsWithoutInventingAShortNumber()
    {
        var t = LabelTemplateCatalog.Defaults()[2];
        t.SerialTextFormat = "{{SerialTail:5}}"; t.BarcodeFormat = "{{DeviceCode:3}}{{SerialTail:5}}"; t.DeviceCode = 312;
        t.Command = System.Text.RegularExpressions.Regex.Replace(t.Command, @"(?m)^[AB][^\r\n]+", m =>
            m.Value.Replace("{{Serial}}", m.Value[0] == 'A' ? "{{SerialText}}" : "{{Barcode}}"));
        var p = LabelPreview.FromInput(t, "31200428", "");
        Assert.Contains(p.Elements, e => e.Text == "00428"); Assert.Null(p.BarcodeError);
        Assert.Equal("31200428", Assert.Single(p.Elements.Where(e => e.Bars.Length > 0)).EncodedData);
        t.BarcodeFormat = "{{ShortSerial:5}}";
        Assert.Contains("включите разбор", LabelPreview.FromInput(t, "31200428", "").Message);
    }

    [Fact]
    public async Task OnlyLabelPresentationChangesWhileProductionIdentityStaysFull()
    {
        var printer = new Printer(); var context = new TestContext(new RegisterState());
        Assert.Equal(StepResult.True, await Step(LabelTemplateCatalog.Defaults()[0], printer).ExecuteAsync(context, default));
        Assert.Equal("3200428", context.GetVariable<string>("PrintLabel.Serial"));
        Assert.Equal(3200428, context.GetVariable<int>("PrintLabel.FullSerial"));
        Assert.Equal("00428", context.GetVariable<string>("PrintLabel.SerialText"));
        Assert.Equal("03200428", context.GetVariable<string>("PrintLabel.Barcode"));
        Assert.Equal("3200428", context.LabelPrinting.Last!.Serial);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LegacyStockCopiesUpgradeWithoutRewritingFilesOrVisualGeometry(bool visual)
    {
        var old = Legacy(); old.Id = "catalog:another-board";
        if (visual) { old.Layout = LabelLayoutDefinition.FromTemplate(old); old.Layout.Elements[0].X += 3; }
        var before = JsonSerializer.Serialize(old);
        var upgraded = old.CopyUpgraded();
        Assert.Equal(before, JsonSerializer.Serialize(old));
        Assert.Equal("{{ShortSerial}}", upgraded.SerialTextFormat);
        Assert.Equal("{{DeviceCode:3}}{{ShortSerial}}", upgraded.BarcodeFormat);
        Assert.Equal("00428", upgraded.RenderSerialText("3200428", 428, Mac));
        if (visual) Assert.Equal(old.Layout!.Elements.Select(e => (e.X, e.Y, e.Width, e.Height)), upgraded.Layout!.Elements.Select(e => (e.X, e.Y, e.Width, e.Height)));
        old.Layout = null; old.Command += " ";
        Assert.Equal(old.Command, old.CopyUpgraded().Command);
        Assert.Null(old.CopyUpgraded().BarcodeFormat);
    }

    [Fact]
    public async Task ReprintingAnOldSavedLabelKeepsItsOriginalTextAndBarcode()
    {
        var old = Legacy(); var expected = LabelPrintContent.Render(old, "3200428", 428, Mac);
        Assert.Contains("SN: 3200428", expected);
        var saved = new PreparedLabel(old, "3200428", Mac, "Test", 1);
        var context = new TestContext(new RegisterState()) { LastTestLabel = saved };
        var printer = new Printer();
        Assert.Equal(StepResult.True, await Step(old, printer, last: true).ExecuteAsync(context, default));
        Assert.Equal(expected, Assert.Single(printer.Jobs));
        using var vm = new LabelPrintingViewModel(NullLogger.Instance, () => true, new Http(), printer);
        vm.SetLastTest(new(DateTimeOffset.Now, "Old run", "Old product", ExecutionStatus.Completed, "", saved));
        Assert.Contains(vm.LastTestPreview!.Elements, e => e.Text == "SN: 3200428");
        await vm.PrintLastTestCommand.ExecuteAsync(null);
        Assert.Equal(expected, printer.Jobs.Last());
    }

    [Fact]
    public void CustomRulesTravelWithCatalogAndProfileAndRequireANewRuntime()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
        var t = LabelTemplateCatalog.Defaults()[0]; t.SerialTextFormat = "{{ShortSerial:6}}";
        t.BarcodeFormat = "{{ServicePrefix}}{{DeviceCode:2}}-{{ShortSerial:5}}"; t.ServicePrefix = "7";
        var file = LabelTemplateStore.LocalPath(clean.Profiles); LabelTemplateStore.Save(file, new[] { t });
        var loaded = Assert.Single(LabelTemplateStore.Load(clean.Profiles, LabelTemplateCatalog.Defaults()));
        Assert.Equal(JsonSerializer.Serialize(t), JsonSerializer.Serialize(loaded));
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        vm.Nodes.Clear(); vm.Nodes.Add(new PrintLabelNodeViewModel { LabelTemplate = loaded });
        var json = GraphSerializer.Serialize(vm, "Label rules");
        Assert.Contains("Print Label (data v1)", json);
        GraphSerializer.Deserialize(json, vm);
        Assert.Equal(JsonSerializer.Serialize(loaded), JsonSerializer.Serialize(Assert.Single(vm.Nodes.OfType<PrintLabelNodeViewModel>()).LabelTemplate));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EditingOnlyOneRuleKeepsTheOtherElementSourceIntact(bool visual)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var t = LabelTemplateCatalog.Defaults()[2];
            if (visual) t.Layout = LabelLayoutDefinition.FromTemplate(t);
            var data = new LabelDataSettingsView { DataContext = t };
            var window = new Window { Content = new ScrollViewer { Content = data }, Width = 700, Height = 850 };
            try
            {
                window.Show(); Layout(window);
                Assert.Null(t.SerialTextFormat); Assert.Null(t.BarcodeFormat);
                ControlNamed<ComboBox>(data, "SerialTextSource").SelectedIndex = 3; Layout(window);
                Assert.Null(t.BarcodeFormat);
                if (visual) Assert.Equal("{{Serial}}", t.Layout!.Elements.Single(e => e.Kind == "Barcode").Value);
                else Assert.Contains("N,\"{{Serial}}\"", t.Command.Split('\n').Single(line => line.StartsWith("B")));
                var first = LabelPreview.FromInput(t, "31200428", "");
                Assert.Contains(first.Elements, e => e.Text == "00428");
                Assert.Equal("31200428", Assert.Single(first.Elements.Where(e => e.Bars.Length > 0)).EncodedData);
                var textRule = t.SerialTextFormat;
                ControlNamed<ComboBox>(data, "BarcodeDataSource").SelectedIndex = 5; Layout(window);
                Assert.Equal(textRule, t.SerialTextFormat);
                var second = LabelPreview.FromInput(t, "31200428", "");
                Assert.Contains(second.Elements, e => e.Text == "00428");
                Assert.Equal("00428", Assert.Single(second.Elements.Where(e => e.Bars.Length > 0)).EncodedData);
            }
            finally { window.Close(); }
        }, default);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task DataControlsApplyIndependentRulesWithoutChangingTheSourceOrGlobalCatalog(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var original = LabelTemplateCatalog.Defaults()[0]; var before = JsonSerializer.Serialize(original);
            var owner = new Window { Width = 1100, Height = 850 }; owner.Show();
            var window = new LabelTemplatesWindow(original, profileOnly: true);
            try
            {
                var result = window.ShowDialog<LabelTemplateDefinition?>(owner); Layout(window);
                var data = window.GetVisualDescendants().OfType<LabelDataSettingsView>().Single();
                var draft = Assert.IsType<LabelTemplateDefinition>(data.DataContext);
                Assert.Equal(before, JsonSerializer.Serialize(draft));
                ControlNamed<ComboBox>(data, "SerialTextSource").SelectedIndex = 1;
                Assert.Equal("{{DeviceCode:3}}{{ShortSerial}}", draft.BarcodeFormat);
                ControlNamed<ComboBox>(data, "BarcodeDataSource").SelectedIndex = 1;
                ControlNamed<ComboBox>(data, "BarcodeSerialDigits").SelectedItem = 6;
                Assert.Equal("{{Serial}}", draft.SerialTextFormat);
                Assert.Equal("0032000428", draft.RenderBarcode("3200428", 428, Mac));
                ControlNamed<TextBox>(data, "BarcodeDataFormat").Text = "{{DeviceCode:2}}{{ShortSerial:4}}";
                Layout(window);
                Assert.Equal(4, ControlNamed<ComboBox>(data, "BarcodeSerialDigits").SelectedItem);
                ControlNamed<ComboBox>(data, "SerialTextSource").SelectedIndex = 3;
                ControlNamed<ComboBox>(data, "PrintedSerialDigits").SelectedItem = 5;
                Assert.Equal("00428", draft.RenderSerialText("3200428", 428, Mac));
                Assert.Equal("320428", draft.RenderBarcode("3200428", 428, Mac));
                Layout(window);
                var evidence = Path.Combine(Path.GetTempPath(), "tfortis-label-data-20261124"); Directory.CreateDirectory(evidence);
                window.CaptureRenderedFrame()!.Save(Path.Combine(evidence, $"label-format-{dark}.png"));
                window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Применить к ноде")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var applied = Assert.IsType<LabelTemplateDefinition>(await result);
                Assert.Equal(draft.BarcodeFormat, applied.BarcodeFormat);
                Assert.False(File.Exists(LabelTemplateStore.LocalPath(clean.Profiles)));
                Assert.Equal(before, JsonSerializer.Serialize(original));
            }
            finally { window.Close(); owner.Close(); Application.Current!.RequestedThemeVariant = ThemeVariant.Default; }
            return true;
        }, default);
    }
    private static T ControlNamed<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Layout(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); Dispatcher.UIThread.RunJobs(); }
    private sealed class Printer : IRawLabelPrinter
    {
        public List<string> Jobs { get; } = new();
        public RawLabelPrintResult Print(string name, byte[] bytes, CancellationToken token) { Jobs.Add(Encoding.UTF8.GetString(bytes)); return RawLabelPrintResult.Ok(); }
    }
    private sealed class Http : IHttpRequestService
    {
        public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout, CancellationToken token) => throw new InvalidOperationException("Reprinting must not request the server.");
    }
}
