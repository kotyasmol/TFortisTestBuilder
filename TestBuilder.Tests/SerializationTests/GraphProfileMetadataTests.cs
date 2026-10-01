using System.Text.Json;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;

namespace TestBuilder.Tests.SerializationTests;

public class GraphProfileMetadataTests
{
    [Theory]
    [InlineData("PSW_2G6F_plus_full_algorithm", "PSW-2G6F+", "Полная проверка")]
    [InlineData("PSW_UPS_Box_8x2Pro_full_algorithm_polling", "PSW+UPS-Box 8x2Pro", "Полная проверка")]
    [InlineData("Ручная печать 4 этикеток PSW+UPS-Box 8x2Pro", "PSW+UPS-Box 8x2Pro", "Ручная печать этикеток")]
    [InlineData("Произвольный тест", "Без модели", "Произвольный тест")]
    public void LegacyProfiles_HaveUsableGroups(string name, string group, string configuration)
    {
        var profile = new GraphProfile("test.json", name);
        Assert.Equal(group, profile.ModelGroup);
        Assert.Equal(configuration, profile.ConfigurationName);
    }

    [Fact]
    public void Metadata_OverridesLegacyName_AndSupportsNewModels()
    {
        var profile = new GraphProfile("test.json", "PSW_2G6F_plus_full_algorithm", "  Новая модель  ", "  Только питание  ");
        Assert.Equal("Новая модель", profile.DeviceModel);
        Assert.Equal("Только питание", profile.ConfigurationName);
    }

    [Fact]
    public void Metadata_IsReadFromCatalog_AndSurvivesSaveAs()
    {
        const string json = """
            { "name": "internal-name", "DeviceModel": "Модель 42", "configurationName": "Быстрая проверка",
              "nodes": [], "connections": [] }
            """;
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            var profile = GraphSerializer.ReadProfile(path);
            Assert.Equal("Модель 42", profile.DeviceModel);
            Assert.Equal("Быстрая проверка", profile.ConfigurationName);
            using var modbus = new ModbusService();
            using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
            GraphSerializer.Deserialize(json, vm);
            using var saved = JsonDocument.Parse(GraphSerializer.Serialize(vm, "renamed-file"));
            Assert.Equal("Модель 42", saved.RootElement.GetProperty("deviceModel").GetString());
            Assert.Equal("Быстрая проверка", saved.RootElement.GetProperty("configurationName").GetString());
            GraphSerializer.Deserialize("""{ "name": "legacy", "nodes": [], "connections": [] }""", vm);
            Assert.Null(vm.RootGraph.DeviceModel);
            Assert.Null(vm.RootGraph.ConfigurationName);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void UnreadableProfile_RemainsInUnassignedGroup()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "invalid JSON");
            var profile = GraphSerializer.ReadProfile(path);
            Assert.Equal("Без модели", profile.ModelGroup);
            Assert.Equal(Path.GetFileNameWithoutExtension(path), profile.Name);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LegacyModel_KeepsItsGroupAfterRenamingFile()
    {
        using var modbus = new ModbusService();
        using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        GraphSerializer.Deserialize("""{ "name": "PSW_2G6F_plus_full_algorithm", "nodes": [], "connections": [] }""", vm);
        using var saved = JsonDocument.Parse(GraphSerializer.Serialize(vm, "new-configuration"));
        Assert.Equal("PSW-2G6F+", saved.RootElement.GetProperty("deviceModel").GetString());
        Assert.Equal("Полная проверка", saved.RootElement.GetProperty("configurationName").GetString());
    }
}
