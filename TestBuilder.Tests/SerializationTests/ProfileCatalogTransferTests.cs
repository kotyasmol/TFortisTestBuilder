using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.SerializationTests;

[Collection("Graph editor UI")]
public class ProfileCatalogTransferTests
{
    internal static EquipmentModel Card => new("portable-test-product", "Переносимое изделие")
    {
        LabelCode = 90,
        SerialSeries = new()
        {
            ServerDeviceType = "Portable-test-server-type", FirstSerialNumber = 9000001, LastSerialNumber = 9065535,
            SerialOffset = 9000000, MacPrefix = "C0:11:A6:5A", FailureReportSerialNumber = 9000001
        },
        LabelVariants = new() { [0] = "Без шкафа", [1] = "Шкаф" }, LabelCabinets = new() { [1] = "Metal" },
        ExtensionData = new() { ["future"] = JsonDocument.Parse("{\"keep\":[1,2]}").RootElement.Clone() }
    };

    internal static string Json(EquipmentModel? card = null, bool embedded = true)
    {
        card ??= Card;
        var root = JsonNode.Parse("""{"name":"Проверка","configurationName":"Проверка","useCatalogSerials":true,"nodes":[{"id":"0","type":"Start (catalog serials v1)","x":80,"y":80},{"id":"1","type":"End","x":400,"y":80}],"connections":[{"sourceNodeId":"0","sourceConnector":"Выход","targetNodeId":"1","targetConnector":"Вход"}],"futureGraph":{"keep":true}}""")!.AsObject();
        root["deviceModelId"] = card.Id; root["deviceModel"] = card.Name;
        root["productKind"] = card.RtlReport == null ? "Switch" : "Board";
        if (embedded) root["catalogModel"] = JsonSerializer.SerializeToNode(card, EquipmentCatalog.JsonOptions);
        return root.ToJsonString();
    }

    static MemoryStream Stream(string json) => new(Encoding.UTF8.GetBytes(json));
    static EquipmentModel Resolve(string folder) => EquipmentCatalog.Load(folder).Resolve(Card.Id, null)!;
    static string ProfilePath(string folder) => Directory.GetFiles(folder, "*.json").Single();

    [Fact]
    public void TenQualificationProfilesExportWithTheirCardAndKeepAllOtherData()
    {
        var folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..", "examples", "profiles", "Проверка 10 моделей"));
        var files = Directory.GetFiles(folder, "*.json"); Assert.Equal(10, files.Length);
        var output = Environment.GetEnvironmentVariable("TFORTIS_PORTABLE_EXPORT_ARTIFACTS");
        foreach (var path in files)
        {
            var original = File.ReadAllText(path);
            var exported = ProfilePortableExport.Create(original, EquipmentCatalog.Load(folder));
            var expected = JsonNode.Parse(original)!.AsObject(); var actual = JsonNode.Parse(exported)!.AsObject();
            expected["catalogModel"] = actual["catalogModel"]!.DeepClone();
            CopyTemplates(expected, actual);
            Assert.True(JsonNode.DeepEquals(expected, actual), Path.GetFileName(path));
            Assert.Equal(actual["deviceModelId"]!.GetValue<string>(), actual["catalogModel"]!["id"]!.GetValue<string>());
            if (!string.IsNullOrEmpty(output))
            { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, Path.GetFileName(path)), exported); }
        }
        static void CopyTemplates(JsonNode expected, JsonNode actual)
        {
            if (expected is JsonObject obj && actual is JsonObject other)
            {
                if (other["labelTemplate"] != null && obj["labelTemplate"] == null) obj["labelTemplate"] = other["labelTemplate"]!.DeepClone();
                foreach (var property in obj) if (property.Value != null && other[property.Key] is { } value) CopyTemplates(property.Value, value);
            }
            else if (expected is JsonArray array && actual is JsonArray values)
                for (var i = 0; i < array.Count; i++) if (array[i] != null && values[i] != null) CopyTemplates(array[i]!, values[i]!);
        }
    }

    [Fact]
    public async Task ExportIncludesUnsavedChangesAndCurrentNameWithoutRewritingTheWorkingProfile()
    {
        using var files = new ProfileCopyTests.ProfileFolder();
        var original = Json(); var path = files.Write("source.json", original);
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
        var profile = vm.Profiles.Single(); vm.SelectedProfile = profile;
        vm.ProfileDescription = "Несохранённое название";
        vm.Nodes.Add(new DelayNodeViewModel { Milliseconds = 12345 });
        string? written = null;
        vm.WriteProfileExportAsync = (info, json) =>
        {
            Assert.Equal(Card.Name + " · Несохранённое название", info.DisplayName);
            written = json; return Task.FromResult(true);
        };
        await vm.ExportProfileCommand.ExecuteAsync(profile);
        Assert.NotNull(written); Assert.Contains("12345", written);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public async Task SavingUnchangedLegacyProfileKeepsBytesButItsCopyIncludesTheCard()
    {
        using var files = new ProfileCopyTests.ProfileFolder();
        EquipmentCatalog.SaveModel(files.Path, Card, createNew: true);
        var original = Json(embedded: false); var path = files.Write("source.json", original);
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
        vm.SelectedProfile = vm.Profiles.Single();
        await vm.SaveGraphCommand.ExecuteAsync(null);
        Assert.Equal(original, File.ReadAllText(path));
        vm.PromptProfileNameAsync = (_, _, suggested, _) => Task.FromResult<string?>(suggested);
        await vm.CopyProfileCommand.ExecuteAsync(vm.SelectedProfile);
        Assert.NotEqual(path, vm.SelectedProfile!.FilePath);
        Assert.Contains("catalogModel", File.ReadAllText(vm.SelectedProfile.FilePath));
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void SingleJsonResolvesAnUnknownModelAndPreservesCardAndGraphData()
    {
        using var files = new ProfileCopyTests.ProfileFolder();
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        Assert.Null(Resolve(files.Path));
        GraphSerializer.Deserialize(Json(), vm);
        Assert.Equal(Card.Id, vm.SelectedEquipmentModel!.Id);
        Assert.Contains(vm.EquipmentModels, m => m.Id == Card.Id);
        Assert.Equal("Из JSON профиля", vm.CurrentEquipmentCatalog.Sources[Card.Id]);
        Assert.True(ProfileCatalogLink.Equivalent(Card, vm.ResolveRunSerialModels().Production));
        var saved = JsonNode.Parse(GraphSerializer.Serialize(vm, "Проверка"))!;
        Assert.Equal(2, saved["catalogModel"]!["future"]!["keep"]![1]!.GetValue<int>());
        Assert.True(saved["futureGraph"]!["keep"]!.GetValue<bool>());
        Assert.False(File.Exists(EquipmentCatalog.LocalPath(files.Path))); // Opening does not write the catalog.
    }

    [Fact]
    public void RuntimeRereadsExternalCatalogAndPrefersItToPortableFallback()
    {
        using var files = new ProfileCopyTests.ProfileFolder();
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        GraphSerializer.Deserialize(Json(), vm);
        var updated = Card with { SerialSeries = Card.SerialSeries! with { ServerDeviceType = "Updated-server-type" } };
        EquipmentCatalog.SaveModel(files.Path, updated, createNew: true);
        Assert.Equal("Updated-server-type", vm.ResolveRunSerialModels().Production!.EffectiveServerDeviceType);
        vm.ReloadProfileCatalogLink();
        Assert.Contains("отличается", vm.CatalogStatus);
        Assert.Contains("Portable-test-server-type", ProfileDependencies.Describe(vm.RootGraph, vm.CurrentEquipmentCatalog));
        Assert.Contains("Updated-server-type", ProfileDependencies.Describe(vm.RootGraph, vm.CurrentEquipmentCatalog));
    }

    [Theory]
    [InlineData("rtl-poe")]
    [InlineData("rtl-sw")]
    public async Task BoardCardPreservesServerTypeAssignmentPolicyAndFailureIdentifier(string id)
    {
        using var files = new ProfileCopyTests.ProfileFolder();
        var original = EquipmentCatalog.LoadBundled().Models.Single(m => m.Id == id);
        var card = original with { Id = "portable-" + id, Name = "Переносимая " + original.Name, Aliases = Array.Empty<string>() };
        await using var stream = Stream(Json(card));
        Assert.True(await ProfileFileStorage.ImportAsync(stream, files.Path, "board.json"));
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        GraphSerializer.Deserialize(File.ReadAllText(ProfilePath(files.Path)), vm);
        var effective = vm.ResolveRunSerialModels().Production!;
        Assert.Equal(card.RtlReport, effective.RtlReport);
        Assert.Equal(card.LabelCode, effective.LabelCode);
        Assert.Equal(card.EffectiveServerDeviceType, effective.EffectiveServerDeviceType);
        Assert.Equal("Board", vm.RootGraph.ProductKind);
    }

    [Fact]
    public void PortableOnlyCardCanBeEditedIntoLocalCatalogWithoutSilentlyOverwritingAnotherEdit()
    {
        using var files = new ProfileCopyTests.ProfileFolder();
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
        GraphSerializer.Deserialize(Json(), vm);
        var original = vm.ReadCatalogModelForEditing();
        vm.SaveCatalogModel(original with { Name = "Новое имя" }, false, original: original);
        Assert.Equal("Новое имя", Resolve(files.Path).Name);
        Assert.False(vm.HasCatalogStatus);
    }

    [Fact]
    public async Task ImportInstallsOnlyItsCardWithPermanentIdAndAllUnknownFields()
    {
        using var files = new ProfileCopyTests.ProfileFolder();
        await using var stream = Stream(Json());
        Assert.True(await ProfileFileStorage.ImportAsync(stream, files.Path, "profile.json"));
        Assert.True(ProfileCatalogLink.Equivalent(Card, Resolve(files.Path)));
        Assert.True(Resolve(files.Path).IsFullOverride);
        var root = JsonNode.Parse(File.ReadAllText(ProfilePath(files.Path)))!;
        Assert.Equal(Card.Id, root["deviceModelId"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Json())!["nodes"], root["nodes"]));
        Assert.True(root["futureGraph"]!["keep"]!.GetValue<bool>());
        Assert.Single(JsonNode.Parse(File.ReadAllText(EquipmentCatalog.LocalPath(files.Path)))!["models"]!.AsArray());
    }

    [Theory]
    [InlineData(CatalogImportChoice.Local)]
    [InlineData(CatalogImportChoice.Profile)]
    [InlineData(CatalogImportChoice.Cancel)]
    public async Task ConflictingCardRequiresAnExplicitChoiceAndPreservesOtherCards(CatalogImportChoice choice)
    {
        using var files = new ProfileCopyTests.ProfileFolder();
        var local = Card with { Name = "Местное изделие", SerialSeries = Card.SerialSeries! with { ServerDeviceType = "Local-server-type" } };
        EquipmentCatalog.SaveModel(files.Path, local, createNew: true);
        var other = new EquipmentModel("other-test-model", "Другое изделие");
        EquipmentCatalog.SaveModel(files.Path, other, createNew: true);
        var before = File.ReadAllBytes(EquipmentCatalog.LocalPath(files.Path));
        await using var stream = Stream(Json());
        var asked = false;
        var imported = await ProfileFileStorage.ImportAsync(stream, files.Path, "profile.json", conflict =>
        {
            asked = true;
            Assert.Equal("Local-server-type", conflict.Local.EffectiveServerDeviceType);
            Assert.Equal(Card.Name, conflict.Profile.Name);
            return Task.FromResult(choice);
        });
        Assert.True(asked);
        Assert.Equal(choice != CatalogImportChoice.Cancel, imported);
        Assert.True(ProfileCatalogLink.Equivalent(other, EquipmentCatalog.Load(files.Path).Resolve(other.Id, null)));
        if (choice == CatalogImportChoice.Cancel)
        { Assert.Empty(Directory.GetFiles(files.Path, "*.json")); Assert.Equal(before, File.ReadAllBytes(EquipmentCatalog.LocalPath(files.Path))); return; }
        var expected = choice == CatalogImportChoice.Local ? local : Card;
        Assert.True(ProfileCatalogLink.Equivalent(expected, Resolve(files.Path)));
        var importedJson = JsonNode.Parse(File.ReadAllText(ProfilePath(files.Path)))!;
        Assert.True(ProfileCatalogLink.Equivalent(expected, importedJson["catalogModel"]!.Deserialize<EquipmentModel>(EquipmentCatalog.JsonOptions)));
        Assert.Equal(expected.Name + " · Проверка", GraphSerializer.ReadProfile(ProfilePath(files.Path)).DisplayName);
        if (choice == CatalogImportChoice.Local) Assert.Equal(before, File.ReadAllBytes(EquipmentCatalog.LocalPath(files.Path)));
    }

    [Fact]
    public async Task CatalogEditWhileChoosingOrChangedRunStateDoesNotPublishOrOverwrite()
    {
        using var files = new ProfileCopyTests.ProfileFolder();
        var local = Card with { Name = "Местная" };
        EquipmentCatalog.SaveModel(files.Path, local, createNew: true);
        var edited = local with { Name = "Чужая правка" };
        await using var stream = Stream(Json());
        await Assert.ThrowsAsync<IOException>(() => ProfileFileStorage.ImportAsync(stream, files.Path, "profile.json", _ =>
        {
            EquipmentCatalog.SaveModel(files.Path, edited);
            return Task.FromResult(CatalogImportChoice.Profile);
        }));
        Assert.Empty(Directory.GetFiles(files.Path, "*.json")); Assert.Equal(edited.Name, Resolve(files.Path).Name);
        await using var next = Stream(Json());
        await Assert.ThrowsAsync<IOException>(() => ProfileFileStorage.ImportAsync(next, files.Path, "profile.json",
            _ => Task.FromResult(CatalogImportChoice.Profile), () => false));
        Assert.Equal(edited.Name, Resolve(files.Path).Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicationFailureRollsBackCatalogExactly(bool existing)
    {
        using var files = new ProfileCopyTests.ProfileFolder();
        if (existing) EquipmentCatalog.SaveModel(files.Path, Card, createNew: true);
        var path = EquipmentCatalog.LocalPath(files.Path);
        var before = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var expected = Resolve(files.Path);
        Assert.Throws<IOException>(() => EquipmentCatalog.ImportAndPublish(files.Path, Card with { Name = "Импорт" }, expected, true,
            () => throw new IOException("simulated publication failure")));
        if (before == null) Assert.False(File.Exists(path));
        else Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task MismatchedIdMissingCardAndCollisionAreRejectedBeforeCatalogChanges()
    {
        using var files = new ProfileCopyTests.ProfileFolder();
        var wrong = JsonNode.Parse(Json())!; wrong["deviceModelId"] = "different-id";
        await using var a = Stream(wrong.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => ProfileFileStorage.ImportAsync(a, files.Path, "profile.json"));
        await using var b = Stream(Json(embedded: false));
        await Assert.ThrowsAsync<InvalidDataException>(() => ProfileFileStorage.ImportAsync(b, files.Path, "profile.json"));
        File.WriteAllText(ProfileFileStorage.DisplayPath(files.Path, Card.Name + " · Проверка"), "original");
        await using var c = Stream(Json());
        await Assert.ThrowsAsync<IOException>(() => ProfileFileStorage.ImportAsync(c, files.Path, "profile.json"));
        Assert.False(File.Exists(EquipmentCatalog.LocalPath(files.Path)));
        Assert.Equal("original", File.ReadAllText(ProfilePath(files.Path)));
    }

    [Fact]
    public async Task ExportCapturesSharedPrintLayoutsInsideNestedAndButtonGraphsWithoutEditingSource()
    {
        using var files = new ProfileCopyTests.ProfileFolder();
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
        GraphSerializer.Deserialize(Json(), vm);
        var sub = new SubtestNodeViewModel(); sub.BodyGraph.Nodes.Add(new PrintLabelNodeViewModel { UseQtProZplFormat = true });
        vm.Nodes.Add(sub);
        var action = new TestBuilder.Models.ProfileActionDefinition();
        action.OffGraph.Nodes.Add(new PrintLabelNodeViewModel()); vm.RootGraph.QuickActions.Add(action);
        var before = GraphSerializer.Serialize(vm, "Проверка");
        var exported = ProfilePortableExport.Create(before, EquipmentCatalog.Load(files.Path));
        Assert.Equal(before, GraphSerializer.Serialize(vm, "Проверка"));
        using var target = new TestViewModel(bus, new SlaveManager(bus));
        GraphSerializer.Deserialize(exported, target);
        var prints = TestBuilder.Services.Graph.ProfileModelAdaptation.All(target.RootGraph).OfType<PrintLabelNodeViewModel>().ToArray();
        Assert.Equal(2, prints.Length); Assert.All(prints, p => Assert.NotNull(p.LabelTemplate));
        Assert.Equal(AppSettings.Instance.LabelTemplates.Single(t => t.Id == "PswUpsBox8x2Pro").Command, prints[0].LabelTemplate!.Command);
        Assert.Equal("EPL", prints[1].LabelTemplate!.Language);
        Assert.True(ProfileCatalogLink.Equivalent(Card, target.ResolveRunSerialModels().Production));
        var context = new TestContext(new RegisterState()) { ProductionModel = target.ResolveRunSerialModels().Production, DeviceModel = Card.Name };
        context.SetVariable("SerialNumber", 9000123); context.SetVariable("SerialNumber.Assigned", 9000123);
        context.SetVariable("Dut.default_mac", "C0:11:A6:5A:00:7B");
        var printer = new CapturePrinter();
        Assert.Equal(StepResult.True, await prints[0].CreateStep(NullLogger.Instance, printer).ExecuteAsync(context, default));
        Assert.Equal(Card.Name, context.GetVariable<string>("PrintLabel.DeviceName"));
        Assert.Equal("009000123", context.GetVariable<string>("PrintLabel.Barcode"));
        Assert.Contains("9000123", Encoding.Latin1.GetString(printer.Bytes));
        var report = new BuildTestReportStep(NullLogger.Instance, "Report", "Test-stand", "SerialNumber", "test-session", "1", false);
        Assert.Equal(StepResult.True, await report.ExecuteAsync(context, default));
        Assert.Contains("9000123", context.GetVariable<string>("Report"));
        var sourcePath = files.Write("source.json", before); vm.RefreshProfiles();
        var selected = vm.Profiles.Single(p => p.FilePath == sourcePath);
        string? written = null;
        vm.WriteProfileExportAsync = (_, json) => { written = json; return Task.FromResult(true); };
        await vm.ExportProfileCommand.ExecuteAsync(selected);
        Assert.NotNull(written); Assert.Equal(before, File.ReadAllText(sourcePath));
    }

    sealed class CapturePrinter : IRawLabelPrinter
    {
        public byte[] Bytes { get; private set; } = Array.Empty<byte>();
        public RawLabelPrintResult Print(string printerName, byte[] bytes, CancellationToken token)
        { Bytes = bytes; return RawLabelPrintResult.Ok(); }
    }
}
