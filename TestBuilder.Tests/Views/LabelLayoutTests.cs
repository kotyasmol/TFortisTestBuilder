using System.Text.Json;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;
using ZXing;
using ZXing.Common;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class LabelLayoutTests : IDisposable
{
    private readonly FreshUserJourneyTests.CleanWorkstation _workstation = new();
    public void Dispose() => _workstation.Dispose();
    private static LabelTemplateDefinition Template()
    {
        var template = LabelTemplateCatalog.Defaults()[0];
        template.Layout = LabelLayoutDefinition.FromTemplate(template); return template.Copy();
    }
    [Fact]
    public void EplAndUnsupportedLegacyCommandsAreHandledExplicitly()
    {
        var t = LabelTemplateCatalog.Defaults()[2]; t.Layout = LabelLayoutDefinition.FromTemplate(t);
        var preview = LabelPreview.Parse(t, LabelPrintContent.Render(t, "00001234", 0, ""));
        Assert.Null(preview.BarcodeError); Assert.Equal("00001234", Decode(preview.Elements.Single(e => e.Bars.Length > 0)));
        t = LabelTemplateCatalog.Defaults()[0]; t.Command = "^XA^FO10,10^BQN,2,5^FD{{Serial}}^FS^XZ";
        Assert.Throws<InvalidOperationException>(() => LabelLayoutDefinition.FromTemplate(t));
    }

    [Fact]
    public void CompleteCatalogRemembersDeletionAndRejectsAnOverlappingEdit()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var file = LabelTemplateStore.LocalPath(folder); var defaults = LabelTemplateCatalog.Defaults();
            var revision = LabelTemplateStore.Revision(file);
            LabelTemplateStore.Save(file, new[] { defaults[1] }, expectedRevision: revision);
            Assert.Equal(defaults[1].Id, Assert.Single(LabelTemplateStore.Load(folder, defaults)).Id);
            Assert.Throws<System.IO.InvalidDataException>(() => LabelTemplateStore.Save(file, defaults, expectedRevision: revision));
            Assert.Single(LabelTemplateStore.Read(file).Templates);
            var layout = Template(); layout.Layout = null;
            Assert.Throws<InvalidOperationException>(() => layout.Validate()); // Old runtimes cannot print the stale source command.
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public void GeneralLayoutIsIncludedInPortableExportAndCorruptCatalogDoesNotCrashThePage()
    {
        var settings = AppSettings.Instance; var oldFolder = settings.GraphsFolder;
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            settings.GraphsFolder = folder;
            var template = Template(); var file = LabelTemplateStore.LocalPath(folder);
            LabelTemplateStore.Save(file, new[] { template });
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
            vm.Nodes.Clear(); vm.Nodes.Add(new PrintLabelNodeViewModel { UseQtProZplFormat = true });
            var json = ProfilePortableExport.Create(GraphSerializer.Serialize(vm, "Общий макет"), new EquipmentCatalog());
            Assert.Contains("Print Label (layout v1)", json);
            GraphSerializer.Deserialize(json, vm);
            Assert.NotNull(Assert.Single(vm.Nodes.OfType<PrintLabelNodeViewModel>()).LabelTemplate!.Layout);
            File.WriteAllText(file, "broken");
            using var page = new LabelPrintingViewModel(NullLogger.Instance, () => true);
            Assert.Empty(page.LabelOptions); Assert.Null(page.Preview);
            Assert.Contains("Каталог этикеток не загружен", page.Status);
        }
        finally { settings.GraphsFolder = oldFolder; if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    private static string Decode(LabelPreviewElement bar)
    {
        var w = (int)bar.Width + (int)bar.QuietZone * 2; const int h = 70;
        var pixels = Enumerable.Repeat((byte)255, w * h).ToArray();
        var module = (int)bar.Width / bar.Bars.Sum(c => c - '0'); var x = (int)bar.QuietZone;
        for (var i = 0; i < bar.Bars.Length; i++)
        {
            var width = (bar.Bars[i] - '0') * module;
            if (i % 2 == 0) for (var y = 0; y < h; y++) Array.Fill(pixels, (byte)0, y * w + x, width);
            x += width;
        }
        var reader = new BarcodeReaderGeneric { Options = new DecodingOptions { PossibleFormats = new[] { BarcodeFormat.CODE_128 }, TryHarder = true } };
        var decoded = reader.Decode(pixels, w, h, RGBLuminanceSource.BitmapFormat.Gray8);
        return Assert.IsType<Result>(decoded).Text;
    }

    [Theory]
    [InlineData("003200428")]
    [InlineData("03200428")]
    [InlineData("000000000")]
    [InlineData("3300001")]
    [InlineData("12345678")]
    [InlineData("AB1234")]
    public void BarcodePixelsDecodeIndependentlyIncludingLeadingZeros(string payload)
    {
        var template = Template(); template.WidthDots = 800;
        var element = template.Layout!.Elements.Single(e => e.Kind == "Barcode"); element.Value = payload; element.X = 30;
        var command = LabelPrintContent.Render(template, "3200428", 428, "C0:11:A6:20:01:AC");
        var preview = LabelPreview.Parse(template, command); Assert.Null(preview.BarcodeError);
        var bar = Assert.Single(preview.Elements.Where(e => e.Bars.Length > 0));
        Assert.Equal(payload, bar.EncodedData); Assert.Equal(payload, Decode(bar));
    }

    [Fact]
    public void ModelDataRemainDynamicAndCabinetTextIsEncodedSafely()
    {
        var baseTemplate = Template();
        foreach (var model in EquipmentCatalog.LoadBundled().Models.Where(m => m.SerialSeries != null && m.LabelCode != null))
        {
            var t = LabelPrintContent.ForModel(baseTemplate, model, 0);
            var serial = Math.Max(model.SerialSeries!.FirstSerialNumber, model.SerialSeries.SerialOffset + 42);
            var number = serial - model.SerialSeries.SerialOffset;
            var command = LabelPrintContent.Render(t, serial.ToString(), number, "C0:11:A6:20:01:AC");
            var preview = LabelPreview.Parse(t, command);
            Assert.Contains(preview.Elements, e => e.Text == "SN: " + number.ToString("D5"));
            Assert.Equal(t.Barcode(number), Decode(Assert.Single(preview.Elements.Where(e => e.Bars.Length > 0))));
        }
        baseTemplate.CabinetName = "Шкаф ^~\"";
        var text = baseTemplate.Layout!.Elements.Single(e => e.Value == "{{Cabinet}}"); text.Width = 200; text.X = 0;
        // Model data are validated before rendering; static editor text is safely hex encoded.
        baseTemplate.CabinetName = "Шкаф"; text.Value = "Шкаф ^~\"";
        var rendered = LabelPrintContent.Render(baseTemplate, "3200428", 428, "C0:11:A6:20:01:AC");
        Assert.Contains(LabelPreview.Parse(baseTemplate, rendered).Elements, e => e.Text == "Шкаф ^~\"");
        Assert.DoesNotContain("^~\"", rendered);
    }

    [Fact]
    public async Task PrintAndPreviewUseTheEditedBarcodeSourceAndRejectOverflow()
    {
        var template = Template(); var bar = template.Layout!.Elements.Single(e => e.Kind == "Barcode");
        bar.Value = "{{Serial}}"; bar.Module = 2;
        var printer = new Printer(); var context = new TestContext(new RegisterState());
        PrintLabelStep Step() => new(NullLogger.Instance, "Test", "SerialNumber", "SerialShort", "Mac", true,
            "3200428", "C0:11:A6:20:01:AC", 1, true, true, printer, 1000, template: template);
        Assert.Equal(StepResult.True, await Step().ExecuteAsync(context, CancellationToken.None));
        Assert.Equal("3200428", context.GetVariable<string>("PrintLabel.Barcode"));
        var preview = LabelPreview.Parse(template, Assert.Single(printer.Commands));
        Assert.Equal("3200428", Decode(preview.Elements.Single(e => e.Bars.Length > 0)));
        bar.X = 0;
        Assert.Equal(StepResult.False, await Step().ExecuteAsync(context, CancellationToken.None));
        Assert.Single(printer.Commands); Assert.Contains("полями", context.GetVariable<string>("PrintLabel.Error"));
    }

    [Fact]
    public void PortableCatalogAndProfileRoundTripPreserveEveryElement()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var template = Template(); template.Layout!.Elements[0].X = 11;
            var file = LabelTemplateStore.LocalPath(folder); LabelTemplateStore.Save(file, new[] { template });
            var first = File.ReadAllText(file); template.Layout.Elements[0].Y = 22;
            LabelTemplateStore.Save(file, new[] { template }, preserveExisting: true);
            Assert.Equal(first, File.ReadAllText(file + ".bak"));
            var loaded = Assert.Single(LabelTemplateStore.Load(folder, new[] { LabelTemplateCatalog.Defaults()[0] }));
            Assert.Equal(JsonSerializer.Serialize(template), JsonSerializer.Serialize(loaded));
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
            vm.Nodes.Clear(); vm.Nodes.Add(new PrintLabelNodeViewModel { LabelTemplate = loaded });
            var json = GraphSerializer.Serialize(vm, "Макет"); Assert.Contains("Print Label (layout v1)", json);
            GraphSerializer.Deserialize(json, vm);
            Assert.Equal(JsonSerializer.Serialize(loaded), JsonSerializer.Serialize(Assert.Single(vm.Nodes.OfType<PrintLabelNodeViewModel>()).LabelTemplate));
            File.WriteAllText(file, "broken"); Assert.Throws<JsonException>(() => LabelTemplateStore.Save(file, new[] { template }, preserveExisting: true));
            Assert.Equal("broken", File.ReadAllText(file));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EditorMovesAndResizesElementsWithoutChangingTheSource(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var theme = Avalonia.Application.Current!.RequestedThemeVariant;
            Avalonia.Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var source = LabelTemplateCatalog.Defaults()[0]; var original = JsonSerializer.Serialize(source);
            var window = new LabelLayoutEditorWindow(source);
            try
            {
                window.Show(); Layout(window); var vm = window.Editor;
                var canvas = window.GetVisualDescendants().OfType<LabelLayoutCanvas>().Single();
                vm.Selected = vm.Elements.Single(e => e.Kind == "Barcode");
                var previous = vm.Selected.X; canvas.Focus(); window.KeyPress(Key.Right, RawInputModifiers.None); window.KeyRelease(Key.Right, RawInputModifiers.None); Layout(window);
                Assert.Equal(previous + 1, vm.Selected.X);
                var rect = canvas.ScreenBounds(vm.Selected); var scale = rect.Height / vm.Selected.Height;
                var point = canvas.TranslatePoint(rect.Center, window)!.Value;
                var moved = point + new Vector(3 * scale, -scale);
                window.MouseDown(point, MouseButton.Left); window.MouseMove(moved); window.MouseUp(moved, MouseButton.Left); Layout(window);
                Assert.Equal(previous + 4, vm.Selected!.X);
                Assert.Equal(source.BarcodeTopDots - 1, vm.Selected.Y);
                window.GetVisualDescendants().OfType<NumericUpDown>().Single(c => c.Name == "LayoutHeight").Value = 2.54m; Layout(window);
                Assert.Equal(30, vm.Selected.Height);
                vm.Center(); // The larger default barcode has only one spare dot beyond each quiet zone.
                vm.ScannedCode = "wrong"; Assert.Contains("не совпадает", vm.Diagnostics);
                vm.ScannedCode = vm.Template.Barcode(42); Assert.Contains("совпадает", vm.Diagnostics);
                var applied = vm.Apply(); Assert.NotNull(applied.Layout);
                Assert.Throws<InvalidOperationException>(() => vm.Module = 2.5m);
                var picker = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "LabelFieldPicker");
                var field = picker.Items[0]; picker.SelectedItem = field; Layout(window);
                Assert.Equal("{{Name}}", vm.Value); Assert.Null(picker.SelectedItem);
                vm.Value = "Другой текст"; picker.SelectedItem = field; Layout(window);
                Assert.Equal("{{Name}}", vm.Value); vm.Value = "{{Barcode}}";
                Assert.Equal(original, JsonSerializer.Serialize(source));
                using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(Path.GetTempPath(), $"tfortis-label-layout-{dark}.png"));
                window.Width = 1040; window.Height = 700; Layout(window);
                using var narrowFrame = window.CaptureRenderedFrame(); narrowFrame!.Save(Path.Combine(Path.GetTempPath(), $"tfortis-label-layout-narrow-{dark}.png"));
            }
            finally { window.Close(); Avalonia.Application.Current.RequestedThemeVariant = theme; }
        }, CancellationToken.None);
    }
    [Fact]
    public async Task ActualPreviewBitmapCanBeReadByAnIndependentDecoder()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var template = Template(); var preview = LabelPreview.FromInput(template, "3200428", "C0:11:A6:20:01:AC");
            var control = new LabelPreviewControl { Preview = preview, Width = template.WidthDots + 4, Height = template.HeightDots!.Value + 4 };
            control.Measure(Size.Infinity); control.Arrange(new Rect(control.DesiredSize));
            var w = (int)control.Width; var h = (int)control.Height;
            using var bitmap = new RenderTargetBitmap(new PixelSize(w, h)); bitmap.Render(control);
            var pixels = new byte[w * h * 4]; var pointer = Marshal.AllocHGlobal(pixels.Length);
            try { bitmap.CopyPixels(new PixelRect(0, 0, w, h), pointer, pixels.Length, w * 4); Marshal.Copy(pointer, pixels, 0, pixels.Length); }
            finally { Marshal.FreeHGlobal(pointer); }
            var reader = new BarcodeReaderGeneric { Options = new DecodingOptions { PossibleFormats = new[] { BarcodeFormat.CODE_128 }, TryHarder = true } };
            Assert.Equal("03200428", Assert.IsType<Result>(reader.Decode(pixels, w, h, RGBLuminanceSource.BitmapFormat.BGRA32)).Text);
            bitmap.Save(Path.Combine(Path.GetTempPath(), "tfortis-label-decode-proof.png"));
        }, CancellationToken.None);
    }
    private static void Layout(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); }
    private sealed class Printer : IRawLabelPrinter
    {
        public List<string> Commands { get; } = new();
        public RawLabelPrintResult Print(string name, byte[] bytes, CancellationToken token)
        { Commands.Add(System.Text.Encoding.ASCII.GetString(bytes)); return RawLabelPrintResult.Ok(); }
    }
}
