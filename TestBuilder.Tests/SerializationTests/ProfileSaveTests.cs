using System.Text.Json;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

[Collection("Graph editor UI")]
public class ProfileSaveTests
{
    private const string OriginalJson = """
        {"name":"Исходный профиль","deviceModel":"Модель А","configurationName":"Полная проверка",
         "updatedAt":"2025-01-02T03:04:05Z","nodes":[{"id":"0","type":"Delay","milliseconds":10}],"connections":[]}
        """;

    [Fact]
    public async Task SavingUnchangedLegacyProfileKeepsItsExactBytesAndDate()
    {
        using var files = new ProfileFolder();
        var path = files.Write("original.json", OriginalJson);
        var bytes = File.ReadAllBytes(path);
        File.SetLastWriteTimeUtc(path, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var date = File.GetLastWriteTimeUtc(path);
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        await vm.SelectProfileAsync(GraphSerializer.ReadProfile(path));

        await vm.SaveGraphCommand.ExecuteAsync(null);

        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(date, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task SavingAnEditedProfileRefusesExternalChangesAndKeepsTheEditsInMemory()
    {
        using var files = new ProfileFolder();
        var path = files.Write("original.json", OriginalJson);
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        await vm.SelectProfileAsync(GraphSerializer.ReadProfile(path));
        var delay = Assert.IsType<DelayNodeViewModel>(Assert.Single(vm.Nodes));
        delay.Milliseconds = 20;
        var external = OriginalJson.Replace("milliseconds\":10", "milliseconds\":30");
        File.WriteAllText(path, external);

        await vm.SaveGraphCommand.ExecuteAsync(null);

        Assert.Contains("Файл изменился на диске", vm.StatusMessage);
        Assert.Equal(external, File.ReadAllText(path));
        Assert.Equal(20, delay.Milliseconds);
        Assert.Empty(Directory.GetFiles(files.Path, "*.tmp"));
    }

    [Fact]
    public async Task ProfileSwitchAndNewDraftRespectDiscardConfirmation()
    {
        using var files = new ProfileFolder();
        var original = files.Write("original.json", OriginalJson);
        var other = files.Write("other.json", OriginalJson.Replace("Исходный профиль", "Другой профиль"));
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        await vm.SelectProfileAsync(GraphSerializer.ReadProfile(original));
        var delay = Assert.IsType<DelayNodeViewModel>(Assert.Single(vm.Nodes));
        delay.Milliseconds = 20;
        int prompts = 0;
        vm.ConfirmDiscardProfileAsync = () => { prompts++; return Task.FromResult(false); };

        await vm.SelectProfileAsync(GraphSerializer.ReadProfile(other));
        await vm.NewProfileCommand.ExecuteAsync(null);

        Assert.Equal(2, prompts);
        Assert.Equal(original, vm.SelectedProfile!.FilePath);
        Assert.Same(delay, Assert.Single(vm.Nodes));
        vm.ConfirmDiscardProfileAsync = () => Task.FromResult(true);
        await vm.SelectProfileAsync(GraphSerializer.ReadProfile(other));
        Assert.Equal(other, vm.SelectedProfile!.FilePath);
    }

    [Fact]
    public async Task EditedSavePreservesUnknownFieldsAndExistingNodeIds()
    {
        using var files = new ProfileFolder();
        var path = files.Write("original.json", """
            {"name":"Профиль","vendorData":{"revision":3},"nodes":[
              {"id":"remove-me","type":"Delay"},
              {"id":"serial-node","type":"Serial Console","vendorNode":{"tags":["a"]},"serialConsole":{"extension":42}},
              {"id":"end-node","type":"End"}],"connections":[
              {"sourceNodeId":"serial-node","sourceConnector":"True","targetNodeId":"end-node","targetConnector":"Вход","vendorLink":true}]}
            """);
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        await vm.SelectProfileAsync(GraphSerializer.ReadProfile(path));
        Assert.DoesNotContain("Ошибка", vm.StatusMessage);
        vm.Nodes.Remove(Assert.IsType<DelayNodeViewModel>(vm.Nodes[0]));
        await vm.SaveGraphCommand.ExecuteAsync(null);

        using var saved = JsonDocument.Parse(File.ReadAllText(path));
        var root = saved.RootElement;
        Assert.Equal(3, root.GetProperty("vendorData").GetProperty("revision").GetInt32());
        var serial = root.GetProperty("nodes")[0];
        Assert.Equal("serial-node", serial.GetProperty("id").GetString());
        Assert.Equal("a", serial.GetProperty("vendorNode").GetProperty("tags")[0].GetString());
        Assert.Equal(42, serial.GetProperty("serialConsole").GetProperty("extension").GetInt32());
        Assert.True(root.GetProperty("connections")[0].GetProperty("vendorLink").GetBoolean());
    }

    [Fact]
    public async Task ImportDoesNotPublishInvalidProfilesOrOverwriteExistingFiles()
    {
        using var files = new ProfileFolder();
        var path = files.Write("existing.json", OriginalJson);
        using var source = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(OriginalJson));
        await Assert.ThrowsAsync<IOException>(() => ProfileFileStorage.ImportAsync(source, files.Path, "existing.json"));
        Assert.Equal(OriginalJson, File.ReadAllText(path));
        using var invalid = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(OriginalJson.Replace("Delay", "Unknown node")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProfileFileStorage.ImportAsync(invalid, files.Path, "broken.json"));
        Assert.Single(Directory.GetFiles(files.Path));
    }

    [Fact]
    public void LatestBadgeSkipsProfilesThatCannotBeOpened()
    {
        using var files = new ProfileFolder();
        var original = files.Write("original.json", OriginalJson);
        var invalid = files.Write("future.json", OriginalJson.Replace("2025-01-02", "2026-01-02").Replace("Delay", "Unknown node"));
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));

        Assert.True(vm.Profiles.Single(p => p.FilePath == original).IsLatest);
        Assert.False(vm.Profiles.Single(p => p.FilePath == invalid).IsLatest);
    }

    [Fact]
    public async Task TwoNameParts_PreserveLegacyOnOpen_AndComposeCopyWithUndo()
    {
        using var files = new ProfileFolder();
        var original = files.Write("original.json", OriginalJson);
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        vm.SelectedProfile = GraphSerializer.ReadProfile(original);
        Assert.Equal("Исходный профиль", vm.CurrentProfileName);
        Assert.Equal("Полная проверка", vm.ProfileDescription);
        vm.ProfileDescription = "Полная проверка"; // Leaving an unchanged field must not rename a legacy file.
        Assert.Equal("Исходный профиль", vm.CurrentProfileName);
        vm.ProfileDescription = "Проверка питания";
        Assert.Equal("Модель А · Проверка питания", vm.CurrentProfileName);
        vm.DeviceName = "PSV2G4H+";
        Assert.Equal("PSV2G4H+ · Проверка питания", vm.CurrentProfileName);
        vm.UndoCommand.Execute(null);
        Assert.Equal("Модель А · Проверка питания", vm.CurrentProfileName);
        vm.UndoCommand.Execute(null);
        Assert.Equal("Исходный профиль", vm.CurrentProfileName);
        Assert.Equal("Полная проверка", vm.ProfileDescription);
        vm.RedoCommand.Execute(null); vm.RedoCommand.Execute(null);
        await vm.SaveGraphCommand.ExecuteAsync(null);
        Assert.Equal(OriginalJson, File.ReadAllText(original));
        Assert.Equal("Проверка питания", vm.SelectedProfile!.ConfigurationName);
        var copy = vm.SelectedProfile;
        vm.SelectedProfile = GraphSerializer.ReadProfile(original); vm.SelectedProfile = copy;
        Assert.Equal("PSV2G4H+", vm.DeviceName);
        Assert.Equal("Проверка питания", vm.ProfileDescription);
        Assert.Equal("PSV2G4H+ · Проверка питания", vm.CurrentProfileName);
        Assert.Equal("PSV2G4H+", vm.EngineerProfileGroups.Single(g => g.Profiles.Contains(copy)).Name);
    }

    [Fact]
    public async Task RenamedSave_CreatesSelectedCopy_AndSubsequentSaveUpdatesOnlyCopy()
    {
        using var files = new ProfileFolder();
        var original = files.Write("original.json", OriginalJson);
        var originalBytes = File.ReadAllBytes(original);
        var originalDate = File.GetLastWriteTimeUtc(original);
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        vm.SelectedProfile = GraphSerializer.ReadProfile(original);
        var delay = vm.Nodes.OfType<DelayNodeViewModel>().Single();
        delay.Milliseconds = 20;
        vm.ProfileSearch = "Исходный";
        vm.CurrentProfileName = "Проверка для завода 2";
        await vm.SaveGraphCommand.ExecuteAsync(null);

        var copy = Assert.IsType<GraphProfile>(vm.SelectedProfile);
        Assert.NotEqual(original, copy.FilePath);
        Assert.Equal(originalBytes, File.ReadAllBytes(original));
        Assert.Equal(originalDate, File.GetLastWriteTimeUtc(original));
        Assert.Equal("Модель А", copy.DeviceModel);
        Assert.Equal("Проверка для завода 2", copy.ConfigurationName);
        Assert.Equal("", vm.ProfileSearch);
        Assert.Contains(copy, vm.Profiles);
        Assert.Same(delay, vm.Nodes.Single());
        Assert.Equal(2, Directory.GetFiles(files.Path, "*.json").Length);
        Assert.InRange(copy.UpdatedAt!.Value, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);

        delay.Milliseconds = 30;
        await vm.SaveGraphCommand.ExecuteAsync(null);
        Assert.Equal(copy.FilePath, vm.SelectedProfile!.FilePath);
        Assert.Equal(2, Directory.GetFiles(files.Path, "*.json").Length);
        Assert.Equal(originalBytes, File.ReadAllBytes(original));
        using var saved = JsonDocument.Parse(File.ReadAllText(copy.FilePath));
        Assert.Equal(30, saved.RootElement.GetProperty("nodes")[0].GetProperty("milliseconds").GetInt32());
        Assert.Equal(copy.UpdatedAt, vm.RootGraph.UpdatedAt);
    }

    [Fact]
    public async Task SameNameSave_UpdatesDateWithoutReplacingCatalogItem_AndBlankNameWritesNothing()
    {
        using var files = new ProfileFolder();
        var path = files.Write("original.json", OriginalJson);
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        vm.RefreshProfiles(); vm.SelectedProfile = Assert.Single(vm.Profiles);
        var profile = vm.SelectedProfile;
        var previousDate = profile.UpdatedAt;
        vm.CurrentProfileName = "  Исходный профиль  ";
        vm.Nodes.OfType<DelayNodeViewModel>().Single().Milliseconds = 42;
        await vm.SaveGraphCommand.ExecuteAsync(null);
        Assert.Same(profile, vm.SelectedProfile);
        Assert.True(profile.UpdatedAt > previousDate);
        Assert.Equal("Исходный профиль", vm.CurrentProfileName);
        Assert.Single(Directory.GetFiles(files.Path, "*.json"));
        var saved = File.ReadAllBytes(path);
        vm.CurrentProfileName = " ";
        await vm.SaveGraphCommand.ExecuteAsync(null);
        Assert.Equal(saved, File.ReadAllBytes(path));
        Assert.Equal("Введите название профиля.", vm.StatusMessage);
    }

    [Fact]
    public async Task NameCollisionAndLateFileCollision_DoNotOverwriteEitherProfile()
    {
        using var files = new ProfileFolder();
        var original = files.Write("original.json", OriginalJson);
        var existing = files.Write("Копия.json", "occupied");
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus));
        vm.SelectedProfile = GraphSerializer.ReadProfile(original);
        vm.CurrentProfileName = "Копия";
        await vm.SaveGraphCommand.ExecuteAsync(null);
        Assert.Equal("Копия (2).json", System.IO.Path.GetFileName(vm.SelectedProfile!.FilePath));
        Assert.Equal("occupied", File.ReadAllText(existing));
        Assert.Equal(OriginalJson, File.ReadAllText(original));
        var selected = vm.SelectedProfile;
        var savedDate = vm.RootGraph.UpdatedAt;
        await vm.SaveGraphToPathAsync(existing, createNew: true);
        Assert.StartsWith("Ошибка сохранения:", vm.StatusMessage);
        Assert.Same(selected, vm.SelectedProfile);
        Assert.Equal(savedDate, vm.RootGraph.UpdatedAt);
        Assert.Equal("occupied", File.ReadAllText(existing));
        Assert.Empty(Directory.GetFiles(files.Path, "*.tmp"));
    }

    [Theory]
    [InlineData("../Заказ: * / ?", ".._Заказ_ _ _ _.json")]
    [InlineData("CON", "_CON.json")]
    [InlineData("   ...  ", "Профиль.json")]
    public void CopyFileNames_ArePortableAndStayInsideProfileFolder(string name, string expected)
    {
        using var files = new ProfileFolder();
        var path = ProfileFileStorage.NewCopyPath(files.Path, name);
        Assert.Equal(files.Path, System.IO.Path.GetDirectoryName(path));
        Assert.Equal(expected, System.IO.Path.GetFileName(path));
    }

    [Fact]
    public void ModificationDate_PreservesEmbeddedDateAcrossCopies_AndFallsBackForLegacyFiles()
    {
        using var files = new ProfileFolder();
        var path = files.Write("original.json", OriginalJson);
        var copied = System.IO.Path.Combine(files.Path, "copied.json");
        File.Copy(path, copied);
        File.SetLastWriteTimeUtc(copied, new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc));
        Assert.Equal(DateTimeOffset.Parse("2025-01-02T03:04:05Z"), GraphSerializer.ReadProfile(copied).UpdatedAt);
        var legacy = files.Write("legacy.json", """{"name":"Старый профиль","nodes":[],"connections":[]}""");
        var date = new DateTime(2024, 3, 2, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(legacy, date);
        Assert.Equal(new DateTimeOffset(date), GraphSerializer.ReadProfile(legacy).UpdatedAt);
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        GraphSerializer.Deserialize(OriginalJson, vm);
        using var json = JsonDocument.Parse(GraphSerializer.Serialize(vm, vm.CurrentProfileName));
        Assert.Equal(DateTimeOffset.Parse("2025-01-02T03:04:05Z"), json.RootElement.GetProperty("updatedAt").GetDateTimeOffset());
        vm.NewProfileCommand.Execute(null);
        Assert.Null(vm.RootGraph.UpdatedAt);
    }

    private sealed class ProfileFolder : IDisposable
    {
        private readonly string _previous = AppSettings.Instance.GraphsFolder;
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "profile-save-" + Guid.NewGuid().ToString("N"));
        public ProfileFolder() { Directory.CreateDirectory(Path); AppSettings.Instance.GraphsFolder = Path; }
        public string Write(string name, string text) { var path = System.IO.Path.Combine(Path, name); File.WriteAllText(path, text); return path; }
        public void Dispose() { AppSettings.Instance.GraphsFolder = _previous; Directory.Delete(Path, true); }
    }
}
