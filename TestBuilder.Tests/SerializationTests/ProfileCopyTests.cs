using System.Text.Json.Nodes;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

[Collection("Graph editor UI")]
public class ProfileCopyTests
{
    internal const string SimpleJson = """
        {"name":"Исходный","deviceModel":"Модель А","configurationName":"Проверка",
         "nodes":[{"id":"0","type":"Delay","milliseconds":10,"x":130,"y":270}],"connections":[]}
        """;

    [Fact]
    public async Task CopyUnopenedProfile_PreservesAllGraphsHardwareButtonsAndUnknownFields_AndGetsUniqueName()
    {
        using var files = new ProfileFolder();
        var original = JsonNode.Parse(File.ReadAllText(TestProfilePaths.Find("PSW_UPS_Box_8x2Pro_parallel_start.json")))!;
        original["extensionData"] = JsonNode.Parse("""{"keep":[1,2,3]}""");
        var sourcePath = files.Write("original.json", original.ToJsonString());
        var originalBytes = File.ReadAllBytes(sourcePath);
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        var source = Assert.Single(vm.Profiles);
        vm.PromptProfileNameAsync = (_, _, name, _) => Task.FromResult<string?>(name);
        await vm.CopyProfileCommand.ExecuteAsync(source);
        var firstCopy = vm.SelectedProfile!;
        Assert.NotEqual(sourcePath, firstCopy.FilePath);
        Assert.Equal(source.DisplayName + " — копия", firstCopy.DisplayName);
        Assert.Equal(source.DeviceModel, firstCopy.DeviceModel);
        Assert.Equal(3, vm.RootGraph.QuickActions.Count);
        var copied = JsonNode.Parse(File.ReadAllText(firstCopy.FilePath))!;
        foreach (var key in new[] { "nodes", "connections", "hardware", "quickActions", "extensionData" })
            Assert.True(JsonNode.DeepEquals(original[key], copied[key]), key);
        await vm.CopyProfileCommand.ExecuteAsync(source);
        Assert.Equal(source.DisplayName + " — копия (2)", vm.SelectedProfile!.DisplayName);
        Assert.Equal(3, Directory.GetFiles(files.Path, "*.json").Length);
        Assert.Equal(originalBytes, File.ReadAllBytes(sourcePath));
        Assert.False(bus.IsConnected);
    }

    [Fact]
    public async Task CopyOpenEdits_ThenRenameAndSave_OnlyChangesCopy()
    {
        using var files = new ProfileFolder();
        var sourcePath = files.Write("original.json", SimpleJson);
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        vm.SelectedProfile = Assert.Single(vm.Profiles);
        vm.Nodes.OfType<DelayNodeViewModel>().Single().Milliseconds = 20;
        vm.PromptProfileNameAsync = (_, _, name, _) => Task.FromResult<string?>(name);
        await vm.CopyProfileCommand.ExecuteAsync(vm.SelectedProfile);
        var copyPath = vm.SelectedProfile!.FilePath;
        Assert.NotEqual(sourcePath, copyPath);
        Assert.Equal(20, vm.Nodes.OfType<DelayNodeViewModel>().Single().Milliseconds);
        vm.Nodes.OfType<DelayNodeViewModel>().Single().Milliseconds = 30;
        vm.PromptProfileNameAsync = (_, _, _, _) => Task.FromResult<string?>("Питание");
        await vm.RenameProfileCommand.ExecuteAsync(vm.SelectedProfile);
        Assert.Equal(copyPath, vm.SelectedProfile!.FilePath);
        Assert.Equal("Модель А · Питание", vm.SelectedProfile.DisplayName);
        Assert.Equal("Питание", vm.ProfileDescription);
        Assert.Equal(30, vm.Nodes.OfType<DelayNodeViewModel>().Single().Milliseconds);
        Assert.Equal(20, JsonNode.Parse(File.ReadAllText(copyPath))!["nodes"]![0]!["milliseconds"]!.GetValue<int>());
        await vm.SaveGraphCommand.ExecuteAsync(null);
        Assert.Equal(30, JsonNode.Parse(File.ReadAllText(copyPath))!["nodes"]![0]!["milliseconds"]!.GetValue<int>());
        Assert.Equal(2, Directory.GetFiles(files.Path, "*.json").Length);
        Assert.Equal(SimpleJson, File.ReadAllText(sourcePath));
    }

    [Fact]
    public async Task CopyAnotherProfile_KeepsCurrentUnsavedEdits_AndRenameDoesNotSwitchSelection()
    {
        using var files = new ProfileFolder();
        var sourcePath = files.Write("source.json", SimpleJson);
        var otherPath = files.Write("other.json", SimpleJson.Replace("Проверка", "Другая"));
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        var source = vm.Profiles.Single(p => p.FilePath == sourcePath);
        vm.SelectedProfile = vm.Profiles.Single(p => p.FilePath == otherPath);
        var node = vm.Nodes.OfType<DelayNodeViewModel>().Single(); node.Milliseconds = 99;
        vm.PromptProfileNameAsync = (_, _, name, _) => Task.FromResult<string?>(name);
        await vm.CopyProfileCommand.ExecuteAsync(source);
        Assert.Equal(otherPath, vm.SelectedProfile!.FilePath);
        Assert.Same(node, vm.Nodes.Single()); Assert.Equal(99, node.Milliseconds);
        vm.PromptProfileNameAsync = (_, _, _, _) => Task.FromResult<string?>("Новое название");
        await vm.RenameProfileCommand.ExecuteAsync(source);
        Assert.Equal(otherPath, vm.SelectedProfile!.FilePath);
        Assert.Same(node, vm.Nodes.Single()); Assert.Equal(99, node.Milliseconds);
        Assert.Contains(vm.Profiles, p => p.FilePath == sourcePath && p.ConfigurationName == "Новое название");
    }

    [Fact]
    public async Task CancelInvalidNameAndRunDuringPrompt_WriteNothing()
    {
        using var files = new ProfileFolder();
        var sourcePath = files.Write("source.json", SimpleJson);
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        var source = Assert.Single(vm.Profiles);
        foreach (var name in new string?[] { null, " ", "Проверка" })
        {
            vm.PromptProfileNameAsync = (_, _, _, _) => Task.FromResult(name);
            await vm.CopyProfileCommand.ExecuteAsync(source);
            Assert.Single(Directory.GetFiles(files.Path, "*.json"));
        }
        var gate = new TaskCompletionSource<string?>();
        vm.PromptProfileNameAsync = (_, _, _, _) => gate.Task;
        var pending = vm.CopyProfileCommand.ExecuteAsync(source);
        vm.IsTestRunning = true;
        gate.SetResult("Новая копия"); await pending;
        Assert.Single(Directory.GetFiles(files.Path, "*.json"));
        Assert.Equal(SimpleJson, File.ReadAllText(sourcePath));
        Assert.False(vm.RenameProfileCommand.CanExecute(source));
        vm.IsTestRunning = false; vm.IsOperatorView = true;
        Assert.False(vm.CopyProfileCommand.CanExecute(source));
    }

    internal sealed class ProfileFolder : IDisposable
    {
        private readonly string _previous = AppSettings.Instance.GraphsFolder;
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "profile-copy-" + Guid.NewGuid().ToString("N"));
        public ProfileFolder() { Directory.CreateDirectory(Path); AppSettings.Instance.GraphsFolder = Path; }
        public string Write(string name, string json) { var path = System.IO.Path.Combine(Path, name); File.WriteAllText(path, json); return path; }
        public void Dispose() { AppSettings.Instance.GraphsFolder = _previous; Directory.Delete(Path, true); }
    }
}
