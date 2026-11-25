using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Services.Http;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class LabelTypeBindingsTests
{
    private const string Mac = "C0:11:A6:20:01:AC";
    private static EquipmentModel Model => EquipmentCatalog.LoadBundled().Models.Single(m => m.LabelCode == 32);
    private static LabelTypesViewModel Workspace() => new(() => true, NullLogger.Instance, new Printer(), () => { });
    private static LabelFieldSourceViewModel Field(LabelTypesViewModel vm, string name) => vm.Sources.Single(r => r.Field == name);
    private static void Source(LabelTypesViewModel vm, string field, string source, string value)
    {
        var row = Field(vm, field); row.Value = value; row.Selected = row.Options.Single(o => o.Id == source);
    }
    private static string CreateAndAssign(LabelTypesViewModel vm)
    {
        vm.EnsureLoaded(); vm.Prototype = vm.Prototypes.Single(p => p.Id == LabelTypeStore.CabinetTypeId);
        vm.NewTypeCommand.Execute(null); vm.TypeName = "Этикетка производства";
        vm.SaveCommand.Execute(null); Assert.Contains("Тип сохранён", vm.Status);
        var id = vm.Selected!.Definition.Id;
        var row = vm.Assignments.Single(a => a.ModelId == Model.Id);
        row.Selected = row.Choices.Single(c => c.Id == id); vm.SaveAssignmentsCommand.Execute(null);
        Assert.Contains("Привязки сохранены", vm.Status); return id;
    }
    private static PrintLabelStep Step(Printer printer, bool repeat = false, bool manual = false, LabelTemplateDefinition? template = null)
        => new(NullLogger.Instance, "Test", "Legacy.SN", "Legacy.Short", "Legacy.Mac", manual, "3200428", Mac,
            1, true, true, printer, 1000, template: template ?? LabelTemplateCatalog.Defaults()[0], useLastTestLabel: repeat, preserveTemplate: manual);
    private static TestContext Context()
    {
        var context = new TestContext(new RegisterState()) { ProductionModel = Model, DeviceModel = Model.Name };
        context.SetVariable("SerialNumber.Assigned", 3200428);
        context.SetVariable("New.SN", 3200428); context.SetVariable("New.Short", 428); context.SetVariable("New.Mac", Mac);
        context.SetVariable("New.Name", "Новое название"); return context;
    }

    [Fact]
    public void NewAndCopiedTypesStayDraftsAndAssignmentsPersistSeparatelyWithoutChangingProfiles()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation(); using var vm = Workspace(); vm.EnsureLoaded();
        var profile = Path.Combine(clean.Profiles, "user.json"); File.WriteAllText(profile, "user profile");
        var before = File.ReadAllBytes(profile);
        vm.NewTypeCommand.Execute(null); var id = vm.Selected!.Definition.Id;
        Assert.StartsWith("custom:", id); Assert.True(vm.IsDirty); Assert.Equal(4, vm.Types.Count);
        Assert.False(File.Exists(vm.StoragePath)); Assert.True(vm.CanRename);
        vm.TypeName = "Свой тип"; vm.TopMargin = .5m; vm.Editor!.PaperHeight = 18;
        vm.SaveCommand.Execute(null); Assert.Contains("Тип сохранён", vm.Status); Assert.False(vm.IsDirty);
        var row = vm.Assignments.Single(a => a.ModelId == Model.Id);
        row.Selected = row.Choices.Single(c => c.Id == id);
        Assert.True(vm.HasAssignmentDrafts); Assert.Empty(LabelTypeStore.Read(vm.StoragePath).Assignments);
        vm.SaveAssignmentsCommand.Execute(null); Assert.False(vm.HasAssignmentDrafts);
        Assert.Equal(id, LabelTypeStore.AssignedTypeId(LabelTypeStore.Read(vm.StoragePath), Model.Id));
        vm.CopyTypeCommand.Execute(null); Assert.NotEqual(id, vm.Selected!.Definition.Id); Assert.Contains("копия", vm.TypeName);
        Assert.Equal(.508m, vm.TopMargin); Assert.True(vm.IsDirty);
        vm.DiscardCommand.Execute(null); Assert.Equal(4, vm.Types.Count);
        using var reload = Workspace(); reload.EnsureLoaded(); Assert.Equal(4, reload.Types.Count);
        Assert.Equal(id, reload.Assignments.Single(a => a.ModelId == Model.Id).Selected.Id);
        Assert.Equal(before, File.ReadAllBytes(profile));
        row.Selected = row.Choices[0]; vm.SaveAssignmentsCommand.Execute(null);
        Assert.Null(LabelTypeStore.AssignedTypeId(LabelTypeStore.Read(vm.StoragePath), Model.Id));
    }

    [Theory]
    [InlineData(LabelTypeStore.CabinetTypeId)]
    [InlineData(LabelTypeStore.BoardTypeId)]
    public async Task AssigningAnUnmodifiedStarterWorksForLegacyProfilesWithoutSavingAnyType(string typeId)
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation(); using var vm = Workspace(); vm.EnsureLoaded();
        var row = vm.Assignments.Single(a => a.ModelId == Model.Id);
        row.Selected = row.Choices.Single(c => c.Id == typeId); vm.SaveAssignmentsCommand.Execute(null);
        Assert.Contains("Привязки сохранены", vm.Status); Assert.Empty(LabelTypeStore.Read(vm.StoragePath).Types);
        var resolved = LabelTypeStore.Resolve(LabelTemplateCatalog.Defaults()[0], null, Model.Name);
        Assert.Equal(typeId, resolved.Id); Assert.NotNull(resolved.Layout); Assert.Equal(32, resolved.DeviceCode);
        Assert.Equal(typeId == LabelTypeStore.CabinetTypeId, resolved.Layout.Elements.Any(e => e.Value == "{{Cabinet}}"));
        var printer = new Printer(); var context = Context(); context.SetVariable("Legacy.SN", 3200428); context.SetVariable("Legacy.Mac", Mac);
        Assert.Equal(StepResult.True, await Step(printer).ExecuteAsync(context, default));
        Assert.Equal(resolved.Language, context.GetVariable<string>("PrintLabel.Language")); Assert.Single(printer.Jobs);
    }

    [Fact]
    public async Task AssignedTypeUsesSelectedVariablesAndConstantsAndRepeatKeepsTheResolvedSnapshot()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation(); using var vm = Workspace(); var id = CreateAndAssign(vm);
        Source(vm, "Serial", "Variable", "New.SN"); Source(vm, "ShortSerial", "Variable", "New.Short");
        Source(vm, "Mac", "Variable", "New.Mac"); Source(vm, "Name", "Variable", "New.Name");
        Source(vm, "DeviceCode", "Constant", "32"); Source(vm, "Cabinet", "Constant", "Шкаф 2");
        vm.SaveCommand.Execute(null); Assert.Contains("Тип сохранён", vm.Status);
        var context = Context(); var printer = new Printer();
        Assert.Equal(StepResult.True, await Step(printer).ExecuteAsync(context, default));
        Assert.Equal("New.SN", context.GetVariable<string>("PrintLabel.SerialSource"));
        Assert.Equal("New.Mac", context.GetVariable<string>("PrintLabel.MacSource"));
        var label = context.LabelPrinting.Last!;
        Assert.Equal("Новое название", label.Template.Name); Assert.Equal("Шкаф 2", label.Template.CabinetName);
        Assert.Equal("03200428", context.GetVariable<string>("PrintLabel.Barcode")); Assert.Equal(id, label.Template.Id);
        var previous = Assert.Single(printer.Jobs);
        Source(vm, "Name", "Constant", "После изменения"); vm.SaveCommand.Execute(null);
        var repeat = new TestContext(new RegisterState()) { LastTestLabel = label, DeviceModel = "Другое изделие" };
        Assert.Equal(StepResult.True, await Step(printer, repeat: true).ExecuteAsync(repeat, default));
        Assert.Equal(previous, printer.Jobs.Last());
        // Other models still get their automatic type and their own identity.
        var other = EquipmentCatalog.LoadBundled().Models.Single(m => m.LabelCode == 33);
        var resolved = LabelTypeStore.Resolve(LabelTemplateCatalog.Defaults()[0], other, other.Name);
        Assert.NotEqual(id, resolved.Id); Assert.Equal(other.Name, resolved.Name); Assert.Null(resolved.DataBindings);
    }

    [Theory]
    [InlineData("New.SN")]
    [InlineData("New.Short")]
    [InlineData("New.Mac")]
    [InlineData("New.Name")]
    public async Task MissingOrInconsistentSourcePreventsPrinting(string missing)
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation(); using var vm = Workspace(); CreateAndAssign(vm);
        foreach (var (field, value) in new[] { ("Serial", "New.SN"), ("ShortSerial", "New.Short"), ("Mac", "New.Mac"), ("Name", "New.Name") })
            Source(vm, field, "Variable", value);
        vm.SaveCommand.Execute(null); var context = Context(); context.Variables.Remove(missing); var printer = new Printer();
        Assert.Equal(StepResult.False, await Step(printer).ExecuteAsync(context, default)); Assert.Empty(printer.Jobs);
        Assert.Contains(missing, context.GetVariable<string>("PrintLabel.Error"));
        context.SetVariable(missing, missing == "New.Mac" ? Mac : missing == "New.Name" ? "Имя" : missing == "New.Short" ? 428 : 3200428);
        context.SetVariable("New.Short", 429);
        Assert.Equal(StepResult.False, await Step(printer).ExecuteAsync(context, default)); Assert.Empty(printer.Jobs);
        Assert.Contains("не согласованы", context.GetVariable<string>("PrintLabel.Error"));
    }

    [Fact]
    public void DataSourceDefaultsDoNotMaterializeOnOpenAndInvalidBindingsDoNotReplaceTheFile()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation(); using var vm = Workspace(); CreateAndAssign(vm);
        var before = File.ReadAllBytes(vm.StoragePath); Assert.Null(vm.Editor!.Template.DataBindings); Assert.False(vm.IsDirty);
        vm.Selected = vm.Types[0]; vm.Selected = vm.Types.Last(); Assert.Null(vm.Editor!.Template.DataBindings); Assert.False(vm.IsDirty);
        Source(vm, "Serial", "Variable", ""); vm.SaveCommand.Execute(null);
        Assert.Equal(before, File.ReadAllBytes(vm.StoragePath)); Assert.True(vm.IsDirty);
        Assert.Contains("имя переменной", vm.Status); vm.DiscardCommand.Execute(null); Assert.Null(vm.Editor!.Template.DataBindings);
        Source(vm, "Name", "Constant", "Bad^XZ"); vm.SaveCommand.Execute(null); Assert.Equal(before, File.ReadAllBytes(vm.StoragePath));
    }

    [Fact]
    public void VersionOneMigratesAndExportCarriesTypesSourcesAndAssignmentsForReview()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation(); using var source = Workspace(); source.EnsureLoaded();
        Directory.CreateDirectory(Path.GetDirectoryName(source.StoragePath)!);
        File.WriteAllText(source.StoragePath, "{\"schemaVersion\":1,\"types\":[]}"); source.ReloadCommand.Execute(null);
        var id = CreateAndAssign(source); Source(source, "Mac", "Variable", "Dut.CustomMac"); source.SaveCommand.Execute(null);
        var json = source.Export(); var exported = JsonSerializer.Deserialize<LabelTypeFile>(json, EquipmentCatalog.JsonOptions)!;
        Assert.Equal(2, exported.SchemaVersion); Assert.Equal(id, Assert.Single(exported.Assignments).LabelTypeId);
        var targetFolder = Path.Combine(clean.Root, "target"); Directory.CreateDirectory(targetFolder); AppSettings.Instance.GraphsFolder = targetFolder;
        using var target = Workspace(); target.EnsureLoaded(); target.Import(json);
        Assert.False(File.Exists(target.StoragePath)); Assert.True(target.HasDrafts); Assert.True(target.HasAssignmentDrafts);
        target.SaveAssignmentsCommand.Execute(null); Assert.False(File.Exists(target.StoragePath)); Assert.Contains("не найден тип", target.Status);
        foreach (var type in target.Types.ToArray()) { target.Selected = type; target.SaveCommand.Execute(null); Assert.Contains("Тип сохранён", target.Status); }
        target.SaveAssignmentsCommand.Execute(null); Assert.Contains("Привязки сохранены", target.Status);
        var file = LabelTypeStore.Read(target.StoragePath);
        Assert.Equal(id, Assert.Single(file.Assignments).LabelTypeId);
        Assert.Equal("Dut.CustomMac", file.Types.Single(t => t.Id == id).Template.DataBindings!.Mac.Value);
        file.Assignments[0] = new(Model.Id, "missing"); Assert.Throws<InvalidDataException>(() => LabelTypeStore.Validate(file));
    }

    [Fact]
    public async Task ManualPrintingSelectsAssignedTypeAndUsesEnteredDataInsteadOfTestVariables()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation(); using var types = Workspace(); var id = CreateAndAssign(types);
        Source(types, "Serial", "Variable", "NotAvailable.SN"); Source(types, "Mac", "Variable", "NotAvailable.Mac");
        Source(types, "Name", "Constant", "Своё название"); types.SaveCommand.Execute(null);
        var printer = new Printer();
        using var printing = new LabelPrintingViewModel(NullLogger.Instance, () => true, new Http(), printer);
        printing.SelectedProductName = Model.Name;
        printing.RefreshTemplates();
        Assert.Equal(id, printing.LabelOptions[printing.SelectedType].Id); Assert.Equal(4, printing.LabelOptions.Count);
        printing.SerialNumber = "3200428"; printing.MacAddress = Mac; printing.PrinterName = "Test";
        await printing.PrintCommand.ExecuteAsync(null);
        Assert.Single(printer.Jobs); Assert.True(printing.CanRepeat); Assert.Equal("Своё название", printing.Preview!.Elements.First(e => e.Text == "Своё название").Text);
    }

    [Fact]
    public void PortableProfilesEmbedBindingsWithANewCompatibilityMarker()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation(); using var vm = Workspace(); CreateAndAssign(vm);
        Source(vm, "Serial", "Variable", "New.SN"); vm.SaveCommand.Execute(null);
        using var bus = new ModbusService(); using var graph = new TestViewModel(bus, new SlaveManager(bus));
        graph.Nodes.Clear(); graph.Nodes.Add(new PrintLabelNodeViewModel { LabelTemplate = LabelTemplateCatalog.Defaults()[0] });
        var json = GraphSerializer.Serialize(graph, "Test");
        var profile = new GraphProfile("test.json", "Test", Model.Name, deviceModelId: Model.Id);
        var portable = ProfilePortableExport.Create(json, EquipmentCatalog.LoadBundled(), profile);
        Assert.Contains("Print Label (bindings v1)", portable);
        GraphSerializer.Deserialize(portable, graph);
        Assert.Equal("New.SN", Assert.Single(graph.Nodes.OfType<PrintLabelNodeViewModel>()).LabelTemplate!.DataBindings!.Serial.Value);
        Assert.DoesNotContain("DataBindings", json);
    }

    [Theory]
    [InlineData(false, 1440, 960)]
    [InlineData(true, 1024, 768)]
    public async Task InterfaceCreatesTypeEditsSourcesAndAssignsItWithoutAdditionalWindows(bool light, int width, int height)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation(); using var vm = Workspace(); vm.EnsureLoaded();
            Application.Current!.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
            var window = new Window { Content = new LabelTypesView { DataContext = vm }, Width = width, Height = height };
            try
            {
                window.Show(); Layout(window);
                var create = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NewLabelType"); create.Command!.Execute(null); Layout(window);
                var name = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "LabelTypeName"); Assert.False(name.IsReadOnly); name.Text = "Новый макет";
                var tabs = window.GetVisualDescendants().OfType<TabControl>().Single(t => t.Name == "LabelTypeEditorTabs"); tabs.SelectedIndex = 1; Layout(window);
                var picker = window.GetVisualDescendants().OfType<ComboBox>().Single(t => t.Name == "LabelSourceSerial");
                picker.SelectedItem = Field(vm, "Serial").Options.Single(o => o.Id == "Variable"); Layout(window);
                var variable = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "LabelSourceValueSerial"); Assert.True(variable.IsEffectivelyVisible); variable.Text = "SerialNumber";
                vm.SaveCommand.Execute(null); Layout(window); Assert.Contains("Тип сохранён", vm.Status); var id = vm.Selected!.Definition.Id;
                window.GetVisualDescendants().OfType<TabControl>().Single(t => t.Name == "LabelTypeEditorTabs").SelectedIndex = 1; Layout(window);
                Evidence(window, light, "sources");
                vm.SelectedSection = 1; vm.AssignmentSearch = "PSW+UPS-Box"; Layout(window);
                var row = vm.Assignments.Single(a => a.ModelId == Model.Id);
                var typePicker = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "ProductLabelType" && c.DataContext == row);
                Assert.True(typePicker.IsEffectivelyVisible); typePicker.SelectedItem = row.Choices.Single(c => c.Id == id);
                vm.SaveAssignmentsCommand.Execute(null); Layout(window); Assert.Contains("Привязки сохранены", vm.Status);
                var grid = window.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "LabelProductAssignments"); Assert.True(grid.IsEffectivelyVisible); Assert.True(grid.Bounds.Height > 100);
                Assert.Same(vm.Assignments, grid.ItemsSource); Assert.Equal(id, row.Selected.Id);
                Assert.Empty(window.OwnedWindows); Evidence(window, light, "assignments");
            }
            finally { window.Close(); }
        }, default);
    }
    private static void Evidence(Window window, bool light, string section)
    {
        if (Environment.GetEnvironmentVariable("TFORTIS_LABEL_BINDINGS_EVIDENCE") is not { } folder) return;
        Directory.CreateDirectory(folder); using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(folder, section + (light ? "-light.png" : "-dark.png")));
    }
    private static void Layout(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); Dispatcher.UIThread.RunJobs(); }
    private sealed class Printer : IRawLabelPrinter
    {
        public List<string> Jobs { get; } = new();
        public RawLabelPrintResult Print(string name, byte[] data, CancellationToken token)
        { Jobs.Add(Encoding.UTF8.GetString(data)); return RawLabelPrintResult.Ok(); }
    }
    private sealed class Http : TestBuilder.Services.Http.IHttpRequestService
    {
        public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout, CancellationToken token)
            => Task.FromResult(HttpRequestResult.Success(200, "[]", TimeSpan.Zero));
    }
}
