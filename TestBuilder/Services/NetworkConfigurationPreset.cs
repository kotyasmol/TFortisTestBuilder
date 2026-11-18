using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TestBuilder.Services;

public sealed record NetworkConfigurationPreset(string Id, string Name, BenchNetworkPlan Plan)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 100 || Id.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(Name) || Name.Length > 100 || Name.Any(char.IsControl))
            throw new InvalidDataException("Укажите название набора длиной до 100 символов.");
        if (Plan == null) throw new InvalidDataException("В наборе отсутствует план сети.");
        Plan.Validate();
        Plan.BuildDataTestPairs();
    }

    public BenchNetworkPlan CopyPlan() => JsonSerializer.Deserialize<BenchNetworkPlan>(
        JsonSerializer.Serialize(Plan, NetworkPresetCatalog.JsonOptions), NetworkPresetCatalog.JsonOptions)!;
}

public sealed class NetworkPresetCatalog
{
    public int SchemaVersion { get; set; } = 1;
    public List<NetworkConfigurationPreset> Presets { get; set; } = new();
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    public static string BundledPath => Path.Combine(AppContext.BaseDirectory, "reference", "network", "presets.json");

    public static NetworkPresetCatalog Read(string json)
    {
        var result = JsonSerializer.Deserialize<NetworkPresetCatalog>(json, JsonOptions)
            ?? throw new InvalidDataException("Пустой файл наборов сети.");
        if (result.SchemaVersion != 1) throw new InvalidDataException("Эта версия файла наборов сети не поддерживается.");
        if (result.Presets == null || result.Presets.Count is < 1 or > 100)
            throw new InvalidDataException("Файл должен содержать от 1 до 100 наборов сети.");
        foreach (var preset in result.Presets)
        {
            if (preset == null) throw new InvalidDataException("Пустой набор сети.");
            preset.Validate();
        }
        if (result.Presets.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Presets.Count ||
            result.Presets.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Presets.Count)
            throw new InvalidDataException("Названия и идентификаторы наборов не должны повторяться.");
        return result;
    }

    public static IReadOnlyList<NetworkConfigurationPreset> LoadBundled() =>
        File.Exists(BundledPath) ? Read(File.ReadAllText(BundledPath)).Presets : Array.Empty<NetworkConfigurationPreset>();

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
