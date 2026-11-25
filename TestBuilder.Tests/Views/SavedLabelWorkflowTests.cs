using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class SavedLabelWorkflowTests
{
    [Fact]
    public void TenCalibrationTemplatesFitAndKeepDynamicIdentifiers()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..", "docs/validation/label-calibration-2026-11-25-types.json"));
        var file = LabelTypeStore.Read(path); Assert.Equal(10, file.Types.Count);
        var model = EquipmentCatalog.LoadBundled().Models.Single(m => m.LabelCode == 32);
        foreach (var type in file.Types)
        {
            Assert.Equal("название устройства", type.Template.Name); Assert.Equal(0, type.Template.DeviceCode);
            Assert.Equal("00000000", type.Template.RenderBarcode("00000", 0, "00:00:00:00:00:00"));
            var real = LabelTypeStore.ApplyPresentation(type.Template, LabelTemplateCatalog.Defaults()[0], model, model.Name);
            var command = LabelPrintContent.Render(real, "3200428", 428, "C0:11:A6:20:01:AC");
            var preview = LabelPreview.Parse(real, command); Assert.Null(preview.BarcodeError);
            Assert.Equal("03200428", preview.Elements.Single(e => e.Bars.Length > 0).EncodedData);
            Assert.Contains(preview.Elements, e => e.Text == "SN: 00428");
            var bar = Assert.Single(real.Layout!.Elements, e => e.Kind == "Barcode");
            Assert.InRange(bar.Y + bar.Height, 1, real.HeightDots!.Value);
            Assert.InRange(bar.X, 10 * bar.Module, real.WidthDots - 178);
        }
    }

    [Fact]
    public void OldProfileLayoutCanBecomeASharedDraftWithoutChangingTheNodeOrSavingItImplicitly()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
        var source = LabelTemplateCatalog.Defaults()[0]; source.Layout = LabelLayoutDefinition.FromTemplate(source);
        source.Layout.Elements.Single(e => e.Kind == "Barcode").Height = 36;
        var before = JsonSerializer.Serialize(source);
        using var types = new LabelTypesViewModel(() => true, NullLogger.Instance, new Printer(), () => { });
        types.CopyFromProfile(source, "Старый профиль");
        Assert.True(types.IsDirty); Assert.StartsWith("custom:", types.Selected!.Definition.Id);
        Assert.Equal(36, types.Editor!.Elements.Single(e => e.Kind == "Barcode").Height);
        Assert.Equal("название устройства", types.Editor.Template.Name); Assert.Equal(0, types.Editor.Template.SerialOffset);
        Assert.False(File.Exists(types.StoragePath)); Assert.Equal(before, JsonSerializer.Serialize(source));
        types.SaveCommand.Execute(null); Assert.False(types.IsDirty); Assert.True(File.Exists(types.StoragePath));
        Assert.Equal(before, JsonSerializer.Serialize(source));
    }

    [Fact]
    public async Task ExplicitTemplateUsesLatestSavedLayoutAndPortableSnapshotWithoutChangingSerialIdentity()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
        var source = LabelTemplateCatalog.Defaults()[0];
        var template = LabelTypeStore.UniversalTemplate(LabelTypeStore.DeviceTypeId); template.Id = "custom:chosen";
        var file = new LabelTypeFile { Types = new() { new(template.Id, "Мой шаблон", null, template) } };
        var path = LabelTypeStore.LocalPath(clean.Profiles); LabelTypeStore.Save(path, file);
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        vm.Nodes.Clear(); var node = new PrintLabelNodeViewModel { LabelTemplate = source.Copy(), UseManualSerialNumber = true,
            ManualSerialNumber = "3200428", ManualMacAddress = "C0:11:A6:20:01:AC", Copies = 1 };
        node.RefreshLabelTypes(); node.SelectedLabelType = node.LabelTypeChoices.Single(t => t.Id == template.Id); vm.Nodes.Add(node);
        Assert.Equal(32, node.LabelTemplate!.DeviceCode);
        // A subsequent shared edit must reach this node without reopening or resaving it.
        template.Layout!.Elements.Single(e => e.Kind == "Barcode").Height = 40; LabelTypeStore.Save(path, file);
        var printer = new Printer(); var context = new TestContext(new RegisterState());
        Assert.Equal(StepResult.True, await node.CreateStep(NullLogger.Instance, printer).ExecuteAsync(context, default));
        Assert.Equal("03200428", context.LabelPrinting.Last!.Template.RenderBarcode("3200428", 428, "C0:11:A6:20:01:AC"));
        Assert.Equal(40, context.LabelPrinting.Last.Template.Layout!.Elements.Single(e => e.Kind == "Barcode").Height);
        var serialized = GraphSerializer.Serialize(vm, "Test");
        var exported = ProfilePortableExport.Create(serialized, new EquipmentCatalog());
        Assert.Equal(serialized, GraphSerializer.Serialize(vm, "Test"));
        File.Delete(path); GraphSerializer.Deserialize(exported, vm);
        var restored = Assert.IsType<PrintLabelNodeViewModel>(Assert.Single(vm.Nodes));
        Assert.Equal(template.Id, restored.LabelTypeId); Assert.Contains("Print Label (type v1)", exported);
        context = new TestContext(new RegisterState());
        Assert.Equal(StepResult.True, await restored.CreateStep(NullLogger.Instance, printer).ExecuteAsync(context, default));
        Assert.Equal(40, context.LabelPrinting.Last!.Template.Layout!.Elements.Single(e => e.Kind == "Barcode").Height);
        Assert.Equal("03200428", context.LabelPrinting.Last.Template.RenderBarcode("3200428", 428, "C0:11:A6:20:01:AC"));
    }

    [Fact]
    public void ExplicitChoiceWinsOverProductAssignmentAndMissingSnapshotFailsClearly()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
        var model = EquipmentCatalog.LoadBundled().Models.Single(m => m.LabelCode == 32);
        var chosen = LabelTypeStore.UniversalTemplate(LabelTypeStore.DeviceTypeId); chosen.Id = "custom:chosen";
        var other = chosen.Copy(); other.Id = "custom:assigned";
        var file = new LabelTypeFile { Types = new() { new(chosen.Id, "Выбранный", null, chosen), new(other.Id, "Другой", null, other) },
            Assignments = new() { new(model.Id, other.Id) } };
        Assert.Equal(chosen.Id, LabelTypeStore.Resolve(LabelTemplateCatalog.Defaults()[0], model, model.Name,
            file: file, preferredTypeId: chosen.Id).Id);
        Assert.Throws<InvalidDataException>(() => LabelTypeStore.Resolve(LabelTemplateCatalog.Defaults()[0], model, model.Name,
            file: new(), preferredTypeId: "custom:missing"));
    }

    [Fact]
    public void CenteredMediaKeepsThePrintersPrintAreaInsteadOfNarrowingItToTheLabelRightEdge()
    {
        var template = LabelTypeStore.UniversalTemplate(LabelTypeStore.DeviceTypeId);
        Assert.True(template.OriginXDots > 0);
        var command = LabelPrintContent.Render(template, "00000", 0, "00:00:00:00:00:00");
        Assert.DoesNotContain("^PW", command); Assert.Contains("^LH0,0", command);
        template.PrintAreaWidthDots = 1248;
        Assert.Contains("^PW1248", LabelPrintContent.Render(template, "00000", 0, "00:00:00:00:00:00"));
        template.PrintAreaWidthDots = null;
        template.OriginXDots = 0;
        Assert.Contains("^PW300", LabelPrintContent.Render(template, "00000", 0, "00:00:00:00:00:00"));
    }

    [Fact]
    public async Task CanvasStartsLockedAndScrollingAnUnfocusedNumberDoesNotChangeItsValue()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            var editor = new LabelLayoutEditorViewModel(LabelTypeStore.UniversalTemplate(LabelTypeStore.DeviceTypeId));
            var view = new LabelTemplateEditorView(editor); var window = new Window { Content = view, Width = 1200, Height = 800 };
            try
            {
                window.Show(); Layout(window);
                var canvas = view.GetVisualDescendants().OfType<LabelLayoutCanvas>().Single();
                editor.Selected = editor.Elements.Single(e => e.Kind == "Barcode"); var bar = editor.Selected;
                var before = JsonSerializer.Serialize(editor.Template); canvas.Focus();
                window.KeyPress(Key.Right, RawInputModifiers.None); window.KeyRelease(Key.Right, RawInputModifiers.None);
                var rect = canvas.ScreenBounds(bar); var start = canvas.TranslatePoint(rect.Center, window)!.Value;
                window.MouseDown(start, MouseButton.Left); window.MouseMove(start + new Vector(30, 15)); window.MouseUp(start + new Vector(30, 15), MouseButton.Left);
                Assert.Equal(before, JsonSerializer.Serialize(editor.Template)); Assert.False(editor.AllowCanvasEditing);
                var input = view.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "LabelTopMargin");
                var point = input.TranslatePoint(new Point(input.Bounds.Width / 2, input.Bounds.Height / 2), window)!.Value;
                window.MouseWheel(point, new Vector(0, 2)); Layout(window);
                Assert.Equal(before, JsonSerializer.Serialize(editor.Template));
                editor.AllowCanvasEditing = true; editor.Move(10000, 10000);
                var bounds = editor.BoundsOf(bar); Assert.True(bounds.Right + 10 * bar.Module <= editor.Template.WidthDots);
                Assert.True(bounds.Bottom <= editor.Template.HeightDots);
                editor.Move(-100, -100); Assert.Equal(10 * bar.Module, bar.X); Assert.Equal(0, bar.Y);
            }
            finally { window.Close(); }
        }, default);
    }

    private static void Layout(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); Dispatcher.UIThread.RunJobs(); }
    private sealed class Printer : IRawLabelPrinter
    { public RawLabelPrintResult Print(string name, byte[] data, CancellationToken token) => RawLabelPrintResult.Ok(); }
}
