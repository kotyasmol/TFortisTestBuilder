using Avalonia;
using System.Text.Json;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Graph;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

public class EditorDocumentTests
{
    [Fact]
    public void History_IsChronologicalAcrossNestedGraphs_AndNavigatesOnReplay()
    {
        using var modbus = new ModbusService();
        using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        vm.AddNodeAtLocation("Подтест", new Point(20, 30));
        var sub = Assert.IsType<SubtestNodeViewModel>(Assert.Single(vm.Nodes));
        vm.OpenCompositeNodeBodyCommand.Execute(sub);
        vm.AddNodeAtLocation("Задержка", new Point(100, 200));
        var delay = Assert.Single(sub.BodyGraph.Nodes.OfType<DelayNodeViewModel>());
        var oldMs = delay.Milliseconds;
        delay.Milliseconds = 7654;
        vm.GoBackGraphCommand.Execute(null);
        vm.AddNodeAtLocation("Задержка", new Point(500, 200));
        vm.UndoCommand.Execute(null);
        Assert.Single(vm.RootGraph.Nodes);
        vm.UndoCommand.Execute(null);
        Assert.Same(sub.BodyGraph, vm.CurrentGraph);
        Assert.Equal(oldMs, delay.Milliseconds);
        vm.UndoCommand.Execute(null);
        Assert.DoesNotContain(delay, sub.BodyGraph.Nodes);
        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.RootGraph.Nodes);
        Assert.False(vm.CanUndo);
        for (var i = 0; i < 4; i++) vm.RedoCommand.Execute(null);
        Assert.Equal(2, vm.RootGraph.Nodes.Count);
        Assert.Contains(delay, sub.BodyGraph.Nodes);
        Assert.Equal(7654, delay.Milliseconds);
        Assert.False(vm.CanRedo);
    }

    [Fact]
    public void History_EvictsOldestEdits_WithoutReorderingRemainingOnes()
    {
        var history = new UndoRedoManager();
        var values = new List<int>();
        for (int i = 0; i < 80; i++) history.Execute(new Append(values, i));
        for (int i = 79; i >= 30; i--) { Assert.Equal(i, values[^1]); history.Undo(); }
        Assert.False(history.CanUndo);
        Assert.Equal(Enumerable.Range(0, 30), values);
        for (int i = 30; i < 80; i++) { history.Redo(); Assert.Equal(i, values[^1]); }
        Assert.Equal(Enumerable.Range(0, 80), values);
    }
    private sealed class Append(List<int> values, int value) : IGraphCommand
    { public void Execute() => values.Add(value); public void Undo() => values.RemoveAt(values.Count - 1); }

    [Fact]
    public void History_TracksPositionColorAndParameters_ButNotRuntimeState()
    {
        using var modbus = new ModbusService();
        using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        var node = new DelayNodeViewModel(); vm.RootGraph.Nodes.Add(node);
        node.IsSelected = true; node.IsExecuting = true; node.HasExecutionSucceeded = true;
        Assert.False(vm.CanUndo);
        node.Location = new Point(20, 30); node.Location = new Point(40, 50);
        node.NodeColor = "red";
        vm.UndoCommand.Execute(null); Assert.Equal("blue", node.NodeColor);
        vm.UndoCommand.Execute(null); Assert.Equal(default, node.Location);
        Assert.False(vm.CanUndo);
        vm.RedoCommand.Execute(null); Assert.Equal(new Point(40, 50), node.Location);
        node.Milliseconds = 7654;
        Assert.False(vm.CanRedo);
    }

    [Theory]
    [InlineData("Проверка питания изделия")]
    [InlineData("Полный тест")]
    public async Task ProfileTitle_SurvivesSaveWithDifferentFileName_AndReload(string title)
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(folder);
        try
        {
            using var modbus = new ModbusService();
            using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
            vm.CurrentProfileName = title;
            var file = Path.Combine(folder, "unrelated-name.json");
            await vm.SaveGraphToPathAsync(file);
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            Assert.Equal(title, json.RootElement.GetProperty("name").GetString());
            vm.SelectedProfile = GraphSerializer.ReadProfile(file);
            Assert.Equal(title, vm.CurrentProfileName);
            Assert.False(vm.CanUndo);
            vm.CurrentProfileName = "Новое название";
            vm.UndoCommand.Execute(null);
            Assert.Equal(title, vm.CurrentProfileName);
            vm.RedoCommand.Execute(null);
            Assert.Equal("Новое название", vm.CurrentProfileName);
            vm.NewProfileCommand.Execute(null);
            Assert.False(vm.CanUndo); Assert.False(vm.CanRedo);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void EngineerProfiles_GroupByModel_WithoutLosingSelectionOnFiltering()
    {
        using var modbus = new ModbusService();
        using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        vm.Profiles.Clear();
        vm.Profiles.Add(new GraphProfile("a", "Питание", "Модель А"));
        vm.Profiles.Add(new GraphProfile("b", "Порты", "Модель А"));
        vm.Profiles.Add(new GraphProfile("c", "Порты", "Модель Б"));
        vm.Profiles.Add(new GraphProfile("d", "Черновик"));
        Assert.Equal(new[] { "Модель А", "Модель Б", "Без модели" }, vm.EngineerProfileGroups.Select(g => g.Name));
        Assert.Equal(2, vm.EngineerProfileGroups[0].Profiles.Count);
    }

    [Fact]
    public void Catalog_ResolvesStableIdAndRejectsDuplicateIds()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(folder, "catalog"));
        var file = Path.Combine(folder, "catalog", "equipment-models.json");
        try
        {
            File.WriteAllText(file, """{"schemaVersion":1,"revision":"v1","models":[{"id":"model-a","name":"Новое имя"}]}""");
            var catalog = EquipmentCatalog.Load(folder);
            Assert.Equal("Новое имя", catalog.Resolve("model-a", "Старое имя")!.Name);
            Assert.Null(catalog.Resolve("unknown", "Новое имя"));
            File.WriteAllText(file, """{"schemaVersion":1,"models":[{"id":"a","name":"A"},{"id":"A","name":"B"}]}""");
            Assert.Throws<InvalidDataException>(() => EquipmentCatalog.Load(folder));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void StableModelId_RoundTripsWithOfflineDisplayName()
    {
        using var modbus = new ModbusService();
        using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        GraphSerializer.Deserialize("""{"name":"Проверка","deviceModelId":"custom-model","deviceModel":"Изделие","nodes":[],"connections":[]}""", vm);
        using var json = JsonDocument.Parse(GraphSerializer.Serialize(vm, vm.CurrentProfileName));
        Assert.Equal("custom-model", json.RootElement.GetProperty("deviceModelId").GetString());
        Assert.Equal("Изделие", json.RootElement.GetProperty("deviceModel").GetString());
    }
    private static EquipmentCatalog ModelCatalog() => new()
    {
        Models = new()
        {
            new EquipmentModel("pro", "PSW+UPS-Box 8x2Pro") { Aliases = new[] { "PSW UPS Box 8x2 Pro" } },
            new EquipmentModel("psw", "PSW-2G6F+")
        }
    };

    [Theory]
    [InlineData("pSw+uPS_box 8×2pRO")]
    [InlineData("PSW UPS Box 8х2 PRO")]
    [InlineData("psw+ups—box 8x2pro")]
    public void Catalog_NormalizesCaseSeparatorsAndMultiplicationSign(string name)
    {
        Assert.Equal("pro", ModelCatalog().Resolve(null, name)!.Id);
    }

    [Theory]
    [InlineData("проверка pSw_2g6f_plus полная", "psw")]
    [InlineData("PSW uPS box 8x2pro проверка питания", "pro")]
    [InlineData("PSW-2G6F+Pro проверка", null)]
    [InlineData("PSW-2G6F+Ex проверка", null)]
    [InlineData("PSW-2G6F+ и PSW UPS Box 8x2Pro", null)]
    public void Catalog_InfersKnownSignatures_ButPreservesUnknownOrAmbiguousModels(string title, string? id)
    {
        Assert.Equal(id, ModelCatalog().Resolve(null, null, title)?.Id);
        Assert.Null(ModelCatalog().Resolve("unrecognised-explicit-id", null, title));
    }

    [Fact]
    public void UnknownModels_GroupAcrossCaseAndSeparators_AndSortNumbersNaturally()
    {
        using var modbus = new ModbusService();
        using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        vm.Profiles.Clear();
        vm.Profiles.Add(new GraphProfile("a", "Один", "uPS-10"));
        vm.Profiles.Add(new GraphProfile("b", "Два", "UPS 10"));
        vm.Profiles.Add(new GraphProfile("c", "Три", "ups 2"));
        Assert.Equal(2, vm.EngineerProfileGroups.Count);
        Assert.Equal("ups 2", vm.EngineerProfileGroups[0].Name);
        Assert.Equal(2, vm.EngineerProfileGroups[1].Profiles.Count);
        Assert.True(EquipmentCatalog.Matches("PSW+UPS-Box 8x2Pro", "uPS box 8×2pRO"));
    }

}
