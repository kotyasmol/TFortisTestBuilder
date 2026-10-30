using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class LabelPreviewTests
{
    [Theory]
    [InlineData("00000000", "1C", 158)]
    [InlineData("3200428", "1B", 224)]
    [InlineData("00428", "1B", 180)]
    public void SerialLabel_CentersExactCode128AndText(string serial, string kind, int expectedWidth)
    {
        var template = LabelTemplateCatalog.Defaults().Single(t => t.Id == "SerialOnlyEpl");
        var command = LabelPrintContent.Render(template, serial, 0, "");
        Assert.Contains("\r\nZB\r\n", command);
        Assert.Contains($",0,{kind},2,1,47,N,\"{serial}\"", command);
        var preview = LabelPreview.Parse(template, command);
        var bars = Assert.Single(preview.Elements.Where(e => e.Bars.Length > 0));
        var text = Assert.Single(preview.Elements.Where(e => e.Text == serial));
        Assert.Equal(expectedWidth, bars.Width);
        Assert.Equal(template.WidthDots / 2.0, bars.X + bars.Width / 2);
        Assert.Equal(template.WidthDots / 2.0, text.X + text.Width / 2);
        Assert.Equal(36, text.Height);
        Assert.Empty(preview.Message);
    }

    [Fact]
    public void CustomEplFontAndModuleWidth_KeepBothFieldsCentered()
    {
        var template = LabelTemplateCatalog.Defaults().Single(t => t.Id == "SerialOnlyEpl");
        template.WidthDots = 600;
        template.PrinterDpi = 203;
        template.Command = template.Command.Replace(",2,1,47", ",4,1,47").Replace(",0,3,1,1,N", ",0,2,2,1,N");
        var preview = LabelPreview.FromInput(template, "12345678", "");
        Assert.All(preview.Elements, e => Assert.Equal(300, e.X + e.Width / 2));
        Assert.Equal(316, preview.Elements.Single(e => e.Bars.Length > 0).Width);
        Assert.Equal(16, preview.Elements.Single(e => e.Text.Length > 0).Height);
    }

    [Fact]
    public void ProLabel_AlignsSerialWithMac_AndKeepsCalibratedBarcodePosition()
    {
        var template = LabelTemplateCatalog.Defaults().Single(t => t.Id == "PswUpsBox8x2Pro");
        var preview = LabelPreview.FromInput(template, "3200428", "C0:11:A6:20:01:AC");
        var sn = preview.Elements.Single(e => e.Text.StartsWith("SN:"));
        Assert.Equal(510, sn.X);
        Assert.Equal(510, preview.Elements.Single(e => e.Text.StartsWith("MAC:")).X);
        var bars = preview.Elements.Single(e => e.Bars.Length > 0);
        Assert.Equal(246, bars.Width);
        Assert.Equal(510, bars.X);
        Assert.Equal(633, bars.X + bars.Width / 2);
        Assert.Empty(preview.Message);
    }

    [Fact]
    public void BuiltInUpgrade_PreservesCustomCommandsAndMetadata_AndIsIdempotent()
    {
        using var stream = typeof(LabelTemplateCatalog).Assembly.GetManifestResourceStream("TestBuilder.label-template-upgrades.json")!;
        var previous = System.Text.Json.JsonSerializer.Deserialize<List<LabelTemplateDefinition>>(stream)!;
        foreach (var old in previous)
        {
            old.Name = "Название пользователя";
            old.WidthDots = 400;
            old.PrinterDpi = 203;
            var copy = old.CopyUpgraded();
            Assert.Equal(LabelTemplateCatalog.Defaults().Single(t => t.Id == old.Id).Command, copy.Command);
            Assert.NotEqual(old.Command, copy.Command);
            Assert.Equal(old.Name, copy.Name);
            Assert.Equal(old.WidthDots, copy.WidthDots);
            Assert.Equal(old.PrinterDpi, copy.PrinterDpi);
            Assert.Equal(copy.Command, copy.CopyUpgraded().Command);
            old.Command += "\n";
            Assert.Equal(old.Command, old.CopyUpgraded().Command);
        }
    }

    [Theory]
    [InlineData(0, "3200428", "SN: 00428")]
    [InlineData(1, "612447", "SN: 12447")]
    [InlineData(2, "12345678", "12345678")]
    public void BuiltInTemplates_UseDataAndRealCommandLayout(int index, string serial, string expected)
    {
        var template = LabelTemplateCatalog.Defaults()[index];
        var preview = LabelPreview.FromInput(template, serial, "c011.a620.01ac");
        Assert.True(preview.HasContent); Assert.False(preview.IsSample); Assert.Empty(preview.Message);
        Assert.Contains(preview.Elements, e => e.Text == expected);
        Assert.Single(preview.Elements.Where(e => e.Bars.Length > 0));
        if (template.RequiresMac)
        {
            Assert.Contains(preview.Elements, e => e.Text == "MAC: C0:11:A6:20:01:AC");
            Assert.Equal(preview.Elements, LabelPreview.FromInput(template, expected[4..], "c011.a620.01ac").Elements);
        }
    }

    [Fact]
    public void CustomTemplates_FollowCoordinates_AndUnsupportedCommandsAreExplicit()
    {
        var template = new LabelTemplateDefinition { Name = "Своя", WidthDots = 500,
            Command = "^XA^PW500^LL200^FO40,30^A0N,20,12^FD{{Serial}}^FS^FO20,70^BY2^BCN,40,N,N,N^FD{{Barcode}}^FS^XZ" };
        var preview = LabelPreview.FromInput(template, "1234", "");
        Assert.Empty(preview.Message);
        Assert.Equal(500, preview.Width); Assert.Equal(200, preview.Height);
        var text = Assert.Single(preview.Elements.Where(e => e.Text == "1234"));
        Assert.Equal(40, text.X); Assert.Equal(30, text.Y);
        template.Command = template.Command.Replace("40,30", "80,45");
        Assert.Equal(80, LabelPreview.FromInput(template, "1234", "").Elements[0].X);
        template.Command = "^XA^FO20,10^BQN,2,5^FDLA,{{Serial}}^FS^XZ";
        preview = LabelPreview.FromInput(template, "1234", "");
        Assert.False(preview.HasContent); Assert.Contains("^BQ", preview.Message);
        template.Command = "^XA^PW30^LL20^FO15,10^A0N,20,20^FDLong text^FS^XZ";
        Assert.Contains("выходит за размер", LabelPreview.FromInput(template, "1234", "").Message);
        template.Command = "^XA^FO20,10^A0R,30,20^FD{{Serial}}^FS^XZ";
        Assert.False(LabelPreview.FromInput(template, "1234", "").HasContent);
    }

    [Fact]
    public void SamplesAndInvalidInput_AreNotPresentedAsActualPrintData()
    {
        var template = LabelTemplateCatalog.Defaults()[0];
        var sample = LabelPreview.FromInput(template, "", "");
        Assert.True(sample.IsSample); Assert.Contains("на примере", sample.Message);
        foreach (var (serial, mac) in new[] { ("bad", ""), ("9999999", ""), ("42", "invalid") })
        {
            var preview = LabelPreview.FromInput(template, serial, mac);
            Assert.False(preview.HasContent); Assert.True(preview.HasMessage);
        }
        template.Command = "^XA^FO999999999999,1^FDHello^FS^XZ";
        Assert.False(LabelPreview.FromInput(template, "42", "").HasContent);
        template.Command = new string('x', 100001);
        Assert.False(LabelPreview.FromInput(template, "42", "").HasContent);
    }

    [Fact]
    public void Code128_ContainsStartDataChecksumAndStop_ForBAndC()
    {
        Assert.Equal("211214" + "111323" + "131123" + "411131" + "2331112", Code128Encoding.Encode("AB", false));
        Assert.Equal("211232" + "112232" + "131123" + "121241" + "2331112", Code128Encoding.Encode("1234", true));
        Assert.Null(Code128Encoding.Encode("123", true));
        Assert.Null(Code128Encoding.Encode("текст", false));
    }

    [Fact]
    public void CatalogRefresh_PreservesSelectedTemplate_AndUsesChangedCommand()
    {
        var original = AppSettings.Instance.LabelTemplates;
        try
        {
            AppSettings.Instance.LabelTemplates = LabelTemplateCatalog.Defaults();
            using var vm = new LabelPrintingViewModel(NullLogger.Instance, () => true, new Http(), new Printer()) { SelectedType = 1, SerialNumber = "1234" };
            var selectedId = vm.LabelOptions[1].Id;
            AppSettings.Instance.LabelTemplates.RemoveAt(0);
            AppSettings.Instance.LabelTemplates[1].Command = "^XA^FO30,40^A0N,20,12^FDNew {{Serial}}^FS^XZ";
            AppSettings.Instance.LabelTemplates[1].Language = "ZPL";
            vm.RefreshTemplates();
            Assert.Equal(1, vm.SelectedType); Assert.Equal(selectedId, vm.LabelOptions[1].Id);
            Assert.Contains(vm.Preview!.Elements, e => e.Text == "New 1234");
            Assert.Contains(vm.LabelOptions[1].Preview.Elements, e => e.Text == "New 12345678");
        }
        finally { AppSettings.Instance.LabelTemplates = original; }
    }

    [Fact]
    public async Task TemplateEditor_PreviewsDraftChangesWithoutSavingCatalog()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var originalCommand = AppSettings.Instance.LabelTemplates[0].Command;
            var window = new LabelTemplatesWindow();
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var command = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.AcceptsReturn);
                command.Text = "^XA^FO20,20^A0N,25,20^FDDraft {{ShortSerial}}^FS^XZ";
                await Task.Delay(250); Dispatcher.UIThread.RunJobs();
                var preview = window.GetVisualDescendants().OfType<LabelPreviewControl>().Single().Preview!;
                Assert.Contains(preview.Elements, e => e.Text == "Draft 00042");
                Assert.Equal(originalCommand, AppSettings.Instance.LabelTemplates[0].Command);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task VisualSelection_InputLookupAndRepeat_KeepPreviewAndPrintedDataInSync(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var printer = new Printer(); var http = new Http();
            using var vm = new LabelPrintingViewModel(NullLogger.Instance, () => true, http, printer);
            var view = new LabelPrintingView { DataContext = vm };
            var window = new Window { Width = 1100, Height = 1000, Content = view };
            void Layout() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); }
            try
            {
                window.Show(); Layout();
                var picker = view.FindControl<ListBox>("LabelTemplatePicker")!;
                var third = picker.GetVisualDescendants().OfType<ListBoxItem>().Single(i => ReferenceEquals(i.DataContext, vm.LabelOptions[1]));
                var point = third.TranslatePoint(new Point(100, 50), window)!.Value;
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout();
                Assert.Equal(1, vm.SelectedType);
                view.FindControl<TextBox>("LabelSerialInput")!.Text = "12345678"; Layout();
                Assert.Contains(vm.Preview!.Elements, e => e.Text == "12345678");
                Assert.Empty(printer.Commands); Assert.Equal(0, http.Requests);
                vm.SerialNumber = "3200428";
                await vm.FindBySerialCommand.ExecuteAsync(null); Layout();
                Assert.Equal(0, vm.SelectedType); Assert.False(vm.Preview!.IsSample);
                Assert.Contains(vm.Preview.Elements, e => e.Text == "MAC: C0:11:A6:20:01:AC");
                Assert.Same(vm.Preview, view.FindControl<LabelPreviewControl>("CurrentLabelPreview")!.Preview);
                await vm.PrintCommand.ExecuteAsync(null);
                var printed = Assert.Single(printer.Commands);
                Assert.Equal(LabelPreview.Parse(AppSettings.Instance.LabelTemplates[0], printed).Elements, vm.LastPreview!.Elements);
                var last = vm.LastPreview;
                vm.SerialNumber = "invalid"; vm.SelectedType = 1;
                Assert.False(vm.Preview!.HasContent); Assert.Same(last, vm.LastPreview);
                await vm.RepeatLastCommand.ExecuteAsync(null);
                Assert.Equal(printed, printer.Commands.Last());
                Assert.Equal(last.Elements, vm.LastPreview!.Elements);
                vm.SelectedType = 0; vm.SerialNumber = "3200428"; Layout();
                window.CaptureRenderedFrame()!.Save($"/private/tmp/tfortis-label-preview-{dark}.png");
                window.Width = 800; Layout();
                Assert.True(picker.GetVisualDescendants().OfType<ListBoxItem>().All(i => i.Bounds.Width < picker.Bounds.Width));
                window.CaptureRenderedFrame()!.Save($"/private/tmp/tfortis-label-preview-narrow-{dark}.png");
            }
            finally { window.Close(); Application.Current!.RequestedThemeVariant = ThemeVariant.Default; }
            return true;
        }, CancellationToken.None);
    }

    private sealed class Printer : IRawLabelPrinter
    {
        public List<string> Commands { get; } = new();
        public RawLabelPrintResult Print(string name, byte[] bytes, CancellationToken token)
        { Commands.Add(System.Text.Encoding.ASCII.GetString(bytes)); return RawLabelPrintResult.Ok(); }
    }
    private sealed class Http : IHttpRequestService
    {
        public int Requests;
        public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout, CancellationToken token)
        {
            Requests++;
            return Task.FromResult(HttpRequestResult.Success(200,
                "[{\"device\":{\"serial\":3200428,\"mac\":\"C0:11:A6:20:01:AC\"}}]", TimeSpan.Zero));
        }
    }
}
