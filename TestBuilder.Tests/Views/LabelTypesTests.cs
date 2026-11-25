using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class LabelTypesTests
{
    private const string Mac = "C0:11:A6:20:01:AC";
    private static LabelTypesViewModel Types(Printer? printer = null, Func<bool>? canUse = null) =>
        new(canUse ?? (() => true), NullLogger.Instance, printer ?? new(), () => { });
    private static EquipmentModel Model() => EquipmentCatalog.LoadBundled().Models.Single(m => m.LabelCode == 32);
    private static LabelTypeItem Select(LabelTypesViewModel vm, EquipmentModel? model = null)
    { vm.EnsureLoaded(); return vm.Selected = vm.Types.Single(t => t.Definition.Id == (model?.RtlReport != null ? LabelTypeStore.BoardTypeId : LabelTypeStore.DeviceTypeId)); }
    private static PrintLabelStep Step(Printer printer, LabelTemplateDefinition fallback, bool preserve = false) =>
        new(NullLogger.Instance, "Test", "SerialNumber", "SerialShort", "Mac", true, "3200428", Mac, 1, true,
            true, printer, 1000, template: fallback, preserveTemplate: preserve);

    [Fact]
    public async Task DefaultDeviceLabelMatchesTheReferenceInPreviewAndPrinterCommands()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            var raw = LabelTemplateCatalog.Defaults()[0];
            var type = LabelTypeStore.UniversalTemplate(LabelTypeStore.DeviceTypeId);
            var elements = type.Layout!.Elements;
            Assert.Equal(300, type.WidthDots); Assert.Equal(158, type.HeightDots);
            Assert.Equal((0, 0), (elements.Single(e => e.Value == "{{Name}}").X, elements.Single(e => e.Value == "{{Name}}").Y));
            Assert.Equal((6, 44), (elements.Single(e => e.Value == "MAC: {{Mac}}").X, elements.Single(e => e.Value == "MAC: {{Mac}}").Y));
            Assert.Equal((6, 70), (elements.Single(e => e.Value == "SN: {{SerialText}}").X, elements.Single(e => e.Value == "SN: {{SerialText}}").Y));
            var bar = elements.Single(e => e.Kind == "Barcode");
            Assert.Equal((31, 104, 50, 3), (bar.X, bar.Y, bar.Height, bar.Module));
            var actual = LabelPrintContent.ForModel(type, Model(), 0);
            var visualPreview = LabelPreview.FromInput(actual, "3200428", Mac);
            var rawPreview = LabelPreview.FromInput(raw, "3200428", Mac);
            Assert.Null(rawPreview.BarcodeError); Assert.Null(visualPreview.BarcodeError);
            Assert.Equal(rawPreview.Elements.Select(e => (e.X, e.Y, e.Height, e.Text, e.EncodedData)),
                visualPreview.Elements.Select(e => (e.X, e.Y, e.Height, e.Text, e.EncodedData)));
            var barcode = visualPreview.Elements.Single(e => e.Bars.Length > 0);
            Assert.Equal(237, barcode.Width); Assert.Equal("03200428", barcode.EncodedData);
            Assert.True(bar.X >= 10 * bar.Module); Assert.True(bar.X + barcode.Width + 10 * bar.Module <= type.WidthDots);
            var folder = Environment.GetEnvironmentVariable("TFORTIS_DEFAULT_LABEL_EVIDENCE");
            if (folder != null)
            {
                Directory.CreateDirectory(folder);
                foreach (var id in new[] { LabelTypeStore.DeviceTypeId, LabelTypeStore.CabinetTypeId })
                {
                    var template = LabelTypeStore.UniversalTemplate(id);
                    var control = new LabelPreviewControl { Preview = LabelPreview.FromInput(template, "00000", "00:00:00:00:00:00"), Width = 800, Height = 425 };
                    control.Measure(Size.Infinity); control.Arrange(new Rect(control.DesiredSize));
                    using var bitmap = new RenderTargetBitmap(new PixelSize(800, 425)); bitmap.Render(control);
                    bitmap.Save(Path.Combine(folder, id == LabelTypeStore.DeviceTypeId ? "default-label.png" : "default-label-cabinet.png"));
                }
            }
        }, default);
    }

    [Fact]
    public void RestoringTheDefaultLayoutChangesOnlyTheDraftGeometryAndKeepsDataRules()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation(); using var vm = Types(); Select(vm);
        vm.TopMargin = .25m; vm.BarcodeHeight = 4;
        vm.Editor!.Template.SerialTextFormat = "{{ShortSerial:6}}";
        vm.Editor.Template.BarcodeFormat = "{{DeviceCode:3}}{{ShortSerial:5}}";
        vm.SaveCommand.Execute(null); Assert.Contains("Тип сохранён", vm.Status);
        var saved = File.ReadAllBytes(vm.StoragePath);
        vm.DefaultLayoutCommand.Execute(null);
        Assert.True(vm.IsDirty); Assert.Equal(0m, vm.TopMargin);
        Assert.Equal("{{ShortSerial:6}}", vm.Editor!.Template.SerialTextFormat);
        Assert.Equal(50, vm.Editor.Elements.Single(e => e.Kind == "Barcode").Height);
        Assert.Equal(saved, File.ReadAllBytes(vm.StoragePath));
        vm.DiscardCommand.Execute(null);
        Assert.False(vm.IsDirty); Assert.Equal(.254m, vm.TopMargin);
    }

    [Fact]
    public void OldStockCommandsAdoptTheNewLayoutButCustomGeometryAndSavedRepeatsStayIntact()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
        using var stream = typeof(LabelTemplateCatalog).Assembly.GetManifestResourceStream("TestBuilder.label-template-upgrades.json")!;
        var old = JsonSerializer.Deserialize<List<LabelTemplateDefinition>>(stream)!.Last(t => t.Id == "PswUpsBox8x2Pro" && t.SerialTextFormat != null);
        var before = JsonSerializer.Serialize(old);
        var oldCommand = LabelPrintContent.Render(old, "3200428", 428, Mac);
        Assert.Contains("^FO494,35^A0,36,15", oldCommand);
        var upgraded = old.CopyUpgraded();
        Assert.Equal((300, 158, 3, 104, 50), (upgraded.WidthDots, upgraded.HeightDots, upgraded.BarcodeModuleDots, upgraded.BarcodeTopDots, upgraded.BarcodeHeightDots));
        Assert.Equal(17, upgraded.NameFontMaxWidthDots); Assert.Equal(.65m, upgraded.NameCharacterWidthFactor);
        Assert.Equal(before, JsonSerializer.Serialize(old)); Assert.Equal(oldCommand, LabelPrintContent.Render(old.Copy(), "3200428", 428, Mac));
        var custom = old.Copy(); custom.WidthDots = 400; custom.HeightDots = 210; custom.BarcodeModuleDots = 1;
        custom.BarcodeTopDots = 80; custom.BarcodeHeightDots = 40; custom.NameFontMaxWidthDots = 13; custom.NameCharacterWidthFactor = .8m;
        var kept = custom.CopyUpgraded();
        Assert.Equal((400, 210, 1, 80, 40), (kept.WidthDots, kept.HeightDots, kept.BarcodeModuleDots, kept.BarcodeTopDots, kept.BarcodeHeightDots));
        Assert.Equal(13, kept.NameFontMaxWidthDots); Assert.Equal(.8m, kept.NameCharacterWidthFactor);
        old.Layout = LabelLayoutDefinition.FromTemplate(old); old.Layout.Elements[0].Y = 12;
        var visual = old.CopyUpgraded();
        Assert.Equal(old.Layout.Elements.Select(e => (e.X, e.Y, e.Width, e.Height, e.FontWidth)), visual.Layout!.Elements.Select(e => (e.X, e.Y, e.Width, e.Height, e.FontWidth)));
        Assert.Null(visual.NameCharacterWidthFactor);
        var noDimensions = old.Copy(); noDimensions.Layout = null; noDimensions.Command = old.Layout.OriginalCommand!;
        noDimensions.HeightDots = null; noDimensions.OriginXDots = 0;
        var restored = noDimensions.CopyUpgraded(); Assert.Equal(494, restored.OriginXDots); Assert.Equal(300, restored.WidthDots);
        Assert.True(LabelPreview.FromInput(restored, "3200428", Mac).HasContent);
        Assert.Null(LabelPreview.FromInput(restored, "3200428", Mac).BarcodeError);
    }

    [Fact]
    public void OpeningAndSwitchingTypesDoesNotWriteSettingsProfilesOrTypeCatalog()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
        var settings = JsonSerializer.Serialize(AppSettings.Instance);
        var legacy = JsonSerializer.Serialize(LabelTemplateCatalog.ConfiguredTemplates());
        using var vm = Types(); vm.EnsureLoaded();
        Assert.Equal(new[] { LabelTypeStore.BoardTypeId, LabelTypeStore.DeviceTypeId, LabelTypeStore.CabinetTypeId }, vm.Types.Select(t => t.Definition.Id));
        foreach (var item in vm.Types.ToArray())
        {
            vm.Selected = item;
            Assert.NotNull(vm.Editor); Assert.False(vm.IsDirty);
        }
        Assert.False(vm.HasDrafts);
        Assert.Empty(Directory.GetFiles(clean.Profiles, "*", SearchOption.AllDirectories));
        Assert.False(File.Exists(clean.SettingsFile));
        Assert.Equal(settings, JsonSerializer.Serialize(AppSettings.Instance));
        Assert.Equal(legacy, JsonSerializer.Serialize(LabelTemplateCatalog.ConfiguredTemplates()));
    }

    [Fact]
    public void AllThreeUniversalPreviewsHaveNeutralDataAndExactlyTheirDeclaredElements()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation(); using var vm = Types(); vm.EnsureLoaded();
        Assert.Equal(new[] { 2, 4, 5 }, vm.Types.Select(t => t.Definition.Template.Layout!.Elements.Count));
        foreach (var id in vm.Types.Select(t => t.Definition.Id).ToArray())
        {
            var type = vm.Types.Single(t => t.Definition.Id == id);
            vm.Selected = type;
            Assert.Null(type.Definition.ProductModelId); Assert.False(vm.IsDirty);
            Assert.Equal("название устройства", vm.Editor!.Template.Name);
            Assert.Equal("00000", vm.Editor.Serial); Assert.Equal("00:00:00:00:00:00", vm.Editor.Mac);
            var preview = vm.Editor.Preview!;
            Assert.True(preview.HasContent, preview.Message); Assert.Null(preview.BarcodeError);
            Assert.Equal("00000000", preview.Elements.Single(e => e.Bars.Length > 0).EncodedData);
            Assert.Contains(preview.Elements, e => e.Text == "00000" || e.Text == "SN: 00000");
            Assert.Equal(type.Definition.Id != LabelTypeStore.BoardTypeId, preview.Elements.Any(e => e.Text == "название устройства"));
            Assert.Equal(type.Definition.Id != LabelTypeStore.BoardTypeId, preview.Elements.Any(e => e.Text == "MAC: 00:00:00:00:00:00"));
            Assert.Equal(type.Definition.Id == LabelTypeStore.CabinetTypeId, preview.Elements.Any(e => e.Text == "Шкаф 1"));
            Assert.DoesNotContain(preview.Elements, e => e.Text.Contains("PSW", StringComparison.Ordinal) || e.Text.Contains("RTL", StringComparison.Ordinal));
            vm.SaveCommand.Execute(null); Assert.Contains("Тип сохранён", vm.Status);
        }
        using var print = new LabelPrintingViewModel(NullLogger.Instance, () => true, new Http(), new Printer());
        Assert.Equal(3, print.LabelOptions.Count);
        Assert.All(print.LabelOptions, option => Assert.Equal("00000000", option.Preview.Elements.Single(e => e.Bars.Length > 0).EncodedData));
        Assert.All(LabelTypeStore.Read(vm.StoragePath).Types, type => Assert.Null(type.ProductModelId));
    }

    [Fact]
    public void CabinetAndPlainDeviceTypesRemainIndependentAndRenderTheActualDeviceData()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation(); using var vm = Types(); Select(vm);
        vm.TopMargin = .5m; vm.Editor!.PaperHeight = 18; vm.SaveCommand.Execute(null);
        vm.Selected = vm.Types.Single(t => t.Definition.Id == LabelTypeStore.CabinetTypeId);
        vm.TopMargin = 1; vm.Editor!.PaperHeight = 18; vm.SaveCommand.Execute(null);
        var model = Model() with { LabelCabinets = new() { [0] = "Шкаф 7", [1] = "" } };
        var stock = LabelTemplateCatalog.Defaults()[0];
        var cabinet = LabelTypeStore.Resolve(stock, model, model.Name, 0);
        var plain = LabelTypeStore.Resolve(stock, model, model.Name, 1);
        Assert.Equal(12, cabinet.Layout!.Elements.Min(e => e.Y)); Assert.Equal(6, plain.Layout!.Elements.Min(e => e.Y));
        foreach (var (template, hasCabinet) in new[] { (cabinet, true), (plain, false) })
        {
            var preview = LabelPreview.FromInput(template, "3200428", Mac);
            Assert.Contains(preview.Elements, e => e.Text == model.Name);
            Assert.Contains(preview.Elements, e => e.Text == "MAC: " + Mac);
            Assert.Contains(preview.Elements, e => e.Text == "SN: 00428");
            Assert.Equal("03200428", preview.Elements.Single(e => e.Bars.Length > 0).EncodedData);
            Assert.Equal(hasCabinet, preview.Elements.Any(e => e.Text == "Шкаф 7"));
            Assert.DoesNotContain(preview.Elements, e => e.Text == "название устройства" || e.Text == "SN: 00000");
            Assert.True(template.MatchServerSerial);
        }
    }

    [Fact]
    public void OldModelSpecificTypesAreKeptOnDiskButDoNotAddRowsOrOverrideTheSavedUniversalType()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
        var old = LabelTemplateCatalog.Defaults()[0]; old.Name = "Старый тип модели";
        var legacy = new LabelTypeDefinition("product:" + Model().Id, "Тип конкретной модели", Model().Id, old);
        LabelTypeStore.Save(LabelTypeStore.LocalPath(clean.Profiles), new[] { legacy });
        using var vm = Types(); Select(vm); Assert.Equal(3, vm.Types.Count);
        vm.TopMargin = .5m; vm.Editor!.PaperHeight = 18; vm.SaveCommand.Execute(null);
        Assert.Equal(2, LabelTypeStore.Read(vm.StoragePath).Types.Count);
        var print = LabelTypeStore.Resolve(old, Model(), Model().Name);
        Assert.Equal(6, print.Layout!.Elements.Min(e => e.Y)); Assert.Equal(Model().Name, print.Name);
        using var next = Types(); next.EnsureLoaded(); Assert.Equal(3, next.Types.Count);
    }

    [Fact]
    public void SwitchingTypesKeepsUnsavedNameGeometryAndIndependentSamples()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
        using var vm = Types(); var item = Select(vm);
        var original = vm.Editor!.Elements.Select(e => e.Y).ToArray();
        vm.TypeName = "Мой макет"; vm.TopMargin = .5m; vm.BarcodeHeight = 4;
        vm.Editor.Serial = "3200428";
        var edited = vm.Editor.Elements.Select(e => e.Y).ToArray();
        Assert.All(edited.Zip(original), pair => Assert.Equal(edited[0] - original[0], pair.First - pair.Second));
        vm.Selected = vm.Types.First();
        Assert.True(vm.HasDrafts);
        vm.Selected = item;
        Assert.Equal("Мой макет", vm.TypeName); Assert.Equal(edited, vm.Editor!.Elements.Select(e => e.Y));
        Assert.Equal("3200428", vm.Editor.Serial); Assert.True(vm.IsDirty);
        vm.DiscardCommand.Execute(null);
        Assert.Equal("Название, MAC, SN и штрихкод", vm.TypeName); Assert.False(vm.IsDirty); Assert.False(vm.HasDrafts);
    }

    [Fact]
    public async Task SavedUniversalTypeUpdatesAllModelsButPreservedReprintKeepsItsGeometry()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
        var fallback = LabelTemplateCatalog.Defaults()[0]; var original = JsonSerializer.Serialize(fallback);
        using var vm = Types(); Select(vm); vm.TopMargin = .5m; vm.BarcodeHeight = 4;
        vm.SaveCommand.Execute(null);
        Assert.Contains("Тип сохранён", vm.Status); Assert.False(vm.IsDirty);
        Assert.Single(LabelTypeStore.Read(vm.StoragePath).Types);
        var printer = new Printer(); var context = new TestContext(new RegisterState()) { ProductionModel = Model(), DeviceModel = Model().Name };
        Assert.Equal(StepResult.True, await Step(printer, fallback).ExecuteAsync(context, default));
        var prepared = context.LabelPrinting.Last!;
        var saved = LabelTypeStore.Read(vm.StoragePath).Types[0].Template;
        Assert.Equal(saved.Layout!.Elements.Select(e => e.Y), prepared.Template.Layout!.Elements.Select(e => e.Y));
        var barcode = LabelPreview.Parse(prepared.Template, Assert.Single(printer.Jobs)).Elements.Single(e => e.Bars.Length > 0);
        Assert.Equal("03200428", barcode.EncodedData);
        Assert.Equal(47, prepared.Template.Layout.Elements.Single(e => e.Kind == "Barcode").Height);
        Assert.Equal(original, JsonSerializer.Serialize(fallback));
        var other = LabelTypeStore.Resolve(fallback, EquipmentCatalog.LoadBundled().Models.Single(m => m.LabelCode == 33), null);
        Assert.NotNull(other.Layout); Assert.Equal(6, other.Layout.Elements.Min(e => e.Y)); Assert.Equal(33, other.DeviceCode);
        Assert.Equal(StepResult.True, await Step(printer, fallback, preserve: true).ExecuteAsync(new(new RegisterState()), default));
        Assert.NotEqual(printer.Jobs.First(), printer.Jobs.Last());
        Assert.Contains("^BCN,50,", printer.Jobs.Last());
    }

    [Fact]
    public void GeneralAndProductOverridesHaveDefinedPriorityAndExportImportsAsDraft()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
        using var vm = Types(); Select(vm);
        vm.TopMargin = .5m; vm.Editor!.PaperHeight = 18; vm.SaveCommand.Execute(null);
        var family = LabelTypeStore.Resolve(LabelTemplateCatalog.Defaults()[0], Model(), Model().Name);
        Assert.Equal(6, family.Layout!.Elements.Min(e => e.Y));
        vm.Selected = vm.Types.Single(t => t.Definition.Id == LabelTypeStore.CabinetTypeId); vm.TopMargin = .25m; vm.SaveCommand.Execute(null);
        var cabinetModel = Model() with { LabelCabinets = new() { [0] = "Шкаф 2" } };
        Assert.Equal(3, LabelTypeStore.Resolve(LabelTemplateCatalog.Defaults()[0], cabinetModel, cabinetModel.Name).Layout!.Elements.Min(e => e.Y));
        Assert.Equal(6, LabelTypeStore.Resolve(LabelTemplateCatalog.Defaults()[0], Model(), Model().Name).Layout!.Elements.Min(e => e.Y));
        var exported = vm.Export();
        var copyFolder = Path.Combine(clean.Root, "profiles-B"); Directory.CreateDirectory(copyFolder);
        AppSettings.Instance.GraphsFolder = copyFolder;
        using var target = Types(); target.EnsureLoaded(); target.Import(exported);
        Assert.Contains("Импортировано в черновик", target.Status);
        Assert.False(File.Exists(target.StoragePath)); Assert.True(target.HasDrafts);
        target.SaveCommand.Execute(null);
        Assert.Single(LabelTypeStore.Read(target.StoragePath).Types);
        Assert.Equal(3, JsonSerializer.Deserialize<LabelTypeFile>(exported, EquipmentCatalog.JsonOptions)!.Types.Count);
    }

    [Fact]
    public void ASharedFamilyChangesPresentationWithoutReplacingALegacyNodesModelIdentity()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation(); using var vm = Types();
        Select(vm); vm.TopMargin = .5m; vm.Editor!.PaperHeight = 18; vm.SaveCommand.Execute(null);
        var legacy = LabelTemplateCatalog.Defaults().Single(t => t.Id == "Psw2G6FPlus");
        var resolved = LabelTypeStore.Resolve(legacy, null, null);
        Assert.Equal(legacy.SerialOffset, resolved.SerialOffset); Assert.Equal(legacy.DeviceCode, resolved.DeviceCode);
        Assert.Equal(legacy.Name, resolved.Name); Assert.NotNull(resolved.Layout);
        Assert.Equal("00600428", LabelPreview.FromInput(resolved, "600428", Mac).Elements.Single(e => e.Bars.Length > 0).EncodedData);
        vm.Selected = vm.Types.Single(t => t.Definition.Id == LabelTypeStore.BoardTypeId); vm.SaveCommand.Execute(null);
        var generic = LabelTemplateCatalog.Defaults().Single(t => t.Id == "SerialOnlyEpl");
        var unchanged = LabelTypeStore.Resolve(generic, null, null);
        Assert.Equal(JsonSerializer.Serialize(generic), JsonSerializer.Serialize(unchanged));
        Assert.Contains(LabelTemplateCatalog.WorkstationTemplates(out _), t => t.Id == "SerialOnlyEpl");
    }

    [Fact]
    public void InvalidBarcodeAndStaleFileDoNotReplaceSavedType()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
        using var vm = Types(); Select(vm); vm.SaveCommand.Execute(null);
        var before = File.ReadAllBytes(vm.StoragePath);
        vm.BarcodeModule = 10; vm.SaveCommand.Execute(null);
        Assert.Equal(before, File.ReadAllBytes(vm.StoragePath)); Assert.True(vm.IsDirty);
        vm.DiscardCommand.Execute(null);
        using var other = Types(); Select(other); other.TopMargin = .5m; other.Editor!.PaperHeight = 18; other.SaveCommand.Execute(null);
        var external = File.ReadAllBytes(vm.StoragePath);
        vm.TopMargin = .25m; vm.SaveCommand.Execute(null);
        Assert.Contains("изменены", vm.Status); Assert.Equal(external, File.ReadAllBytes(vm.StoragePath));
    }

    [Fact]
    public void ImportRejectsTheOldCatalogFormatAndDuplicateProductBindingsWithoutChangingDrafts()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation(); using var vm = Types(); Select(vm);
        var selected = vm.Selected; var before = vm.Types.Count;
        vm.Import(JsonSerializer.Serialize(new LabelTemplateFile { Templates = LabelTemplateCatalog.Defaults() }, EquipmentCatalog.JsonOptions));
        Assert.Same(selected, vm.Selected); Assert.False(vm.HasDrafts); Assert.Equal(before, vm.Types.Count);
        var definition = selected!.Definition with { Id = "legacy:one", ProductModelId = Model().Id };
        Assert.Throws<InvalidDataException>(() => LabelTypeStore.Validate(new() { Types = new() { definition, definition with { Id = "another" } } }));
        Assert.False(File.Exists(vm.StoragePath));
    }

    [Fact]
    public async Task SamplePrintUsesUnsavedDraftAndOneCopyWithoutSavingOrApplyingAnExistingType()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
        var printer = new Printer(); using var vm = Types(printer); Select(vm); vm.SaveCommand.Execute(null);
        var saved = File.ReadAllBytes(vm.StoragePath);
        vm.TopMargin = .5m; vm.BarcodeHeight = 4; vm.PrinterName = "Test";
        await vm.PrintSampleCommand.ExecuteAsync(null);
        Assert.Contains("Одна этикетка", vm.Status); Assert.Single(printer.Jobs);
        Assert.Equal(saved, File.ReadAllBytes(vm.StoragePath)); Assert.True(vm.IsDirty);
        var parsed = LabelPreview.Parse(vm.Editor.Template, printer.Jobs[0]);
        Assert.Equal("00000000", parsed.Elements.Single(e => e.Bars.Length > 0).EncodedData);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(printer.Jobs[0], "\\^XA").Count);
    }

    [Fact]
    public async Task CorruptSharedTypesBlockNewPrintButNotAnExactRepeat()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
        var path = LabelTypeStore.LocalPath(clean.Profiles); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "broken");
        var printer = new Printer(); var template = LabelTemplateCatalog.Defaults()[0]; var context = new TestContext(new RegisterState());
        Assert.Equal(StepResult.False, await Step(printer, template).ExecuteAsync(context, default)); Assert.Empty(printer.Jobs);
        Assert.Equal(StepResult.True, await Step(printer, template, preserve: true).ExecuteAsync(new(new RegisterState()), default));
        Assert.Single(printer.Jobs);
    }

    [Fact]
    public void OneUniversalBoardTypeUsesEachBoardsRealCodeAtPrintTime()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation(); using var vm = Types(); vm.EnsureLoaded();
        vm.Selected = vm.Types.Single(t => t.Definition.Id == LabelTypeStore.BoardTypeId);
        Assert.Equal("00000000", vm.Editor!.Preview!.Elements.Single(e => e.Bars.Length > 0).EncodedData);
        vm.SaveCommand.Execute(null); Assert.Contains("Тип сохранён", vm.Status);
        foreach (var board in EquipmentCatalog.LoadBundled().Models.Where(m => m.RtlReport != null && m.LabelCode != null))
        {
            var serial = board.LabelCode + "00428";
            var template = LabelTypeStore.Resolve(LabelTemplateCatalog.Defaults()[2], board, board.Name);
            var preview = LabelPreview.FromInput(template, serial, "");
            Assert.Contains(preview.Elements, e => e.Text == "00428");
            Assert.Equal(serial, preview.Elements.Single(e => e.Bars.Length > 0).EncodedData);
        }
        using var reload = Types(); reload.EnsureLoaded(); Assert.Equal(3, reload.Types.Count);
        Assert.All(reload.Types, type => Assert.Null(type.Definition.ProductModelId));
    }

    [Fact]
    public void PortableExportEmbedsTheSharedTypeWithoutEditingTheSourceProfile()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation(); using var vm = Types();
        Select(vm); vm.TopMargin = .5m; vm.Editor!.PaperHeight = 18; vm.SaveCommand.Execute(null);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..", "examples", "profiles", "Проверка 10 моделей"));
        var path = Directory.GetFiles(root, "*.json").Single(p => Path.GetFileName(p).StartsWith("PSW+UPS-Box"));
        var source = File.ReadAllText(path); var original = File.ReadAllBytes(path);
        var exported = ProfilePortableExport.Create(source, EquipmentCatalog.LoadBundled());
        var graph = System.Text.Json.Nodes.JsonNode.Parse(exported)!;
        var templates = Descendants(graph).OfType<System.Text.Json.Nodes.JsonObject>().Where(n => n["labelTemplate"] != null).ToArray();
        Assert.NotEmpty(templates);
        foreach (var node in templates)
        {
            var template = node["labelTemplate"]!.Deserialize<LabelTemplateDefinition>(EquipmentCatalog.JsonOptions)!;
            Assert.Equal(6, template.Layout!.Elements.Min(e => e.Y)); Assert.Equal("Print Label (layout v1)", node["type"]!.GetValue<string>());
        }
        Assert.Equal(original, File.ReadAllBytes(path));
        static IEnumerable<System.Text.Json.Nodes.JsonNode> Descendants(System.Text.Json.Nodes.JsonNode node)
        {
            yield return node;
            var children = node is System.Text.Json.Nodes.JsonObject obj ? obj.Select(p => p.Value) :
                node is System.Text.Json.Nodes.JsonArray array ? array : Enumerable.Empty<System.Text.Json.Nodes.JsonNode?>();
            foreach (var child in children.Where(c => c != null)) foreach (var descendant in Descendants(child!)) yield return descendant;
        }
    }

    [Fact]
    public async Task InvalidNumericTextCannotSaveAStaleValueAndBusyTrialBlocksEditing()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation(); using var vm = Types(); Select(vm);
            var view = new LabelTypesView { DataContext = vm }; var window = new Window { Content = view, Width = 1200, Height = 900 };
            try
            {
                window.Show(); Layout(window); vm.SaveCommand.Execute(null); Layout(window);
                var before = File.ReadAllBytes(vm.StoragePath);
                var field = window.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "LabelTopMargin");
                field.Text = "ошибка"; Layout(window);
                Assert.True(vm.HasInputErrors!());
                vm.SaveCommand.Execute(null);
                Assert.Contains("Исправьте", vm.Status); Assert.Equal(before, File.ReadAllBytes(vm.StoragePath));
            }
            finally { window.Close(); }
        }, default);
        using var next = new FreshUserJourneyTests.CleanWorkstation();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var print = new LabelPrintingViewModel(NullLogger.Instance, () => true, new Http(), new BlockingPrinter(gate));
        Select(print.Types); print.Types.PrinterName = "Test";
        var job = print.Types.PrintSampleCommand.ExecuteAsync(null);
        try { Assert.True(print.Types.IsBusy); Assert.False(print.CanEdit); Assert.False(print.Types.CanEdit); Assert.False(print.Types.SaveCommand.CanExecute(null)); }
        finally { gate.TrySetResult(true); await job; }
    }

    [Theory]
    [InlineData(false, 1440, 960)]
    [InlineData(true, 1024, 768)]
    public async Task MainInterfaceEditsAndSavesTypesWithoutOpeningAnotherWindow(bool light, int width, int height)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            Application.Current!.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
            using var vm = new LabelPrintingViewModel(NullLogger.Instance, () => true, new Http(), new Printer());
            var view = new LabelPrintingView { DataContext = vm }; var window = new Window { Content = view, Width = width, Height = height };
            try
            {
                window.Show(); Layout(window); vm.EditTemplatesCommand.Execute(null); Layout(window);
                Assert.Equal(1, vm.SelectedSection); Select(vm.Types); Layout(window);
                var canvas = window.GetVisualDescendants().OfType<LabelLayoutCanvas>().Single();
                Assert.True(canvas.IsEffectivelyVisible); Assert.True(canvas.Bounds.Width > 300); Assert.True(canvas.Bounds.Height >= 180);
                Assert.False(vm.Types.IsDirty); Assert.False(File.Exists(vm.Types.StoragePath));
                var top = window.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "LabelTopMargin");
                var barcode = window.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "LabelBarcodeHeight");
                top.Value = .5m; barcode.Value = 4; Layout(window);
                Assert.Equal(.508m, vm.Types.TopMargin); Assert.True(vm.Types.IsDirty);
                var save = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveLabelType");
                save.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); save.Command!.Execute(null); Layout(window);
                Assert.Contains("Тип сохранён", vm.Types.Status); Assert.False(vm.Types.IsDirty);
                Assert.Empty(window.OwnedWindows);
                using var capture = window.CaptureRenderedFrame();
                var folder = Environment.GetEnvironmentVariable("TFORTIS_LABEL_TYPES_EVIDENCE");
                if (folder != null)
                {
                    Directory.CreateDirectory(folder); capture!.Save(Path.Combine(folder, light ? "label-types-light.png" : "label-types-dark.png"));
                    if (!light)
                        foreach (var type in vm.Types.Types.ToArray())
                        {
                            vm.Types.Selected = type; Layout(window);
                            using var frame = window.CaptureRenderedFrame();
                            frame!.Save(Path.Combine(folder, "universal-" + type.Definition.Id.Replace(':', '-') + ".png"));
                        }
                }
                vm.SelectedSection = 0; Layout(window); vm.SelectedSection = 1; Layout(window);
                vm.Types.Editor!.Selected = vm.Types.Editor.Elements.Last(); Layout(window);
                Assert.NotNull(window.GetVisualDescendants().OfType<LabelLayoutCanvas>().Single().Preview);
            }
            finally { window.Close(); }
        }, default);
    }

    private static void Layout(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); Dispatcher.UIThread.RunJobs(); }
    private sealed class Printer : IRawLabelPrinter
    {
        public List<string> Jobs { get; } = new();
        public RawLabelPrintResult Print(string name, byte[] bytes, CancellationToken token) { token.ThrowIfCancellationRequested(); Jobs.Add(Encoding.UTF8.GetString(bytes)); return RawLabelPrintResult.Ok(); }
    }
    private sealed class Http : IHttpRequestService
    {
        public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout, CancellationToken token) => Task.FromResult(HttpRequestResult.Success(200, "[]", TimeSpan.Zero));
    }
    private sealed class BlockingPrinter(TaskCompletionSource<bool> gate) : IRawLabelPrinter
    {
        public RawLabelPrintResult Print(string name, byte[] bytes, CancellationToken token) { gate.Task.Wait(token); return RawLabelPrintResult.Ok(); }
    }
}
