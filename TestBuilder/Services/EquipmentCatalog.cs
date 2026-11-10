using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace TestBuilder.Services;

public sealed record EquipmentModel(string Id, string Name)
{
    public string[] Aliases { get; init; } = Array.Empty<string>();
    public int? LabelCode { get; init; }
    public string? ServerDeviceType { get; init; }
    public RtlReportSettings? RtlReport { get; init; }
    public Dictionary<int, string>? LabelVariants { get; init; }
    public ModelSerialSeries? SerialSeries { get; init; }
    internal string? EffectiveServerDeviceType => SerialSeries?.ServerDeviceType.Trim() ?? ServerDeviceType?.Trim();
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; init; }
    public override string ToString() => Name;
}

public sealed record RtlReportSettings(int DeviceType, bool AssignSerialOnReport)
{
    public string DeviceIdentifierVariable { get; init; } = "CpuId";
    public string? FailureDeviceIdentifier { get; init; }
}

public sealed class EquipmentCatalog
{
    public int SchemaVersion { get; set; } = 1;
    public string Revision { get; set; } = "";
    public List<EquipmentModel> Models { get; set; } = new();
    internal static string RelativePath { get; } = Path.Combine("catalog", "equipment-models.json");
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static EquipmentCatalog Load(string profilesFolder)
    {
        var bundled = Read(Path.Combine(AppContext.BaseDirectory, "reference", "serials", "equipment-models.json"));
        var local = Read(Path.Combine(profilesFolder, RelativePath));
        var models = bundled.Models.ToDictionary(model => model.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var model in local.Models)
        {
            // Older installations retain their original local catalog after updates.
            if (models.TryGetValue(model.Id, out var baseline) && Keys(baseline).Contains(Normalize(model.Name)))
                models[model.Id] = model with
                {
                    LabelCode = model.LabelCode ?? baseline.LabelCode,
                    ServerDeviceType = model.ServerDeviceType ?? baseline.ServerDeviceType,
                    RtlReport = model.RtlReport is { } report && baseline.RtlReport is { } baselineReport &&
                        report.DeviceType == baselineReport.DeviceType
                        ? report with { FailureDeviceIdentifier = report.FailureDeviceIdentifier ?? baselineReport.FailureDeviceIdentifier }
                        : model.RtlReport ?? baseline.RtlReport,
                    LabelVariants = model.LabelVariants ?? baseline.LabelVariants,
                    SerialSeries = model.SerialSeries ?? (model.ServerDeviceType == null ? baseline.SerialSeries : null)
                };
            else
                models[model.Id] = model;
        }
        local.Models = models.Values.ToList();
        return local;
    }

    private static EquipmentCatalog Read(string file)
    {
        if (!File.Exists(file)) return new EquipmentCatalog();
        var catalog = JsonSerializer.Deserialize<EquipmentCatalog>(File.ReadAllText(file), JsonOptions)
            ?? throw new InvalidDataException("Пустой каталог изделий.");
        catalog.Validate();
        return catalog;
    }

    internal static void SaveModel(string profilesFolder, EquipmentModel model)
    {
        var file = Path.Combine(profilesFolder, RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var editLock = new FileStream(file + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var catalog = Read(file);
        var index = catalog.Models.FindIndex(item => string.Equals(item.Id, model.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) catalog.Models[index] = model;
        else catalog.Models.Add(model);
        catalog.Validate();
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(catalog, JsonOptions));
            File.Move(temporary, file, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private void Validate()
    {
        if (SchemaVersion != 1 || Models == null || Models.Any(m => m == null || string.IsNullOrWhiteSpace(m.Id) || string.IsNullOrWhiteSpace(m.Name)) ||
            Models.Select(m => m.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Models.Count)
            throw new InvalidDataException("Некорректная версия каталога или повторяющиеся ID изделий.");
        foreach (var model in Models)
        {
            if (model.LabelCode is < 0 or > 999 ||
                model.LabelCode.HasValue && !LabelTemplateDefinition.IsSafeLabelText(model.Name) ||
                model.LabelVariants?.Any(pair => pair.Key is < 0 or > 9 || string.IsNullOrWhiteSpace(pair.Value) ||
                    !LabelTemplateDefinition.IsSafeLabelText(pair.Value)) == true)
                throw new InvalidDataException($"Проверьте код этикетки и комплектации изделия «{model.Name}».");
            model.SerialSeries?.Validate(model.Name);
            if (model.RtlReport is { } report &&
                (report.DeviceType <= 0 || string.IsNullOrWhiteSpace(report.DeviceIdentifierVariable) ||
                 (report.FailureDeviceIdentifier != null && string.IsNullOrWhiteSpace(report.FailureDeviceIdentifier))))
                throw new InvalidDataException($"Проверьте серверный код отчёта и идентификаторы «{model.Name}».");
        }
    }
    // Separators/case are presentation; +, port counts and model suffixes are identity.
    public static string Normalize(string? value)
    {
        var text = (value ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant()
            .Replace('×', 'x').Replace('х', 'x');
        text = Regex.Replace(text, @"(?<![\p{L}])plus(?![\p{L}])", "+");
        return Regex.Replace(text, @"[^\p{L}\p{Nd}+]+", "");
    }
    public static string SortKey(string? value) => Regex.Replace(Normalize(value), @"\d+", m => m.Value.PadLeft(12, '0'));
    public static bool Matches(string? value, string? query) => Normalize(value).Contains(Normalize(query), StringComparison.Ordinal);

    private static IEnumerable<string> Keys(EquipmentModel model) =>
        new[] { model.Name }.Concat(model.Aliases ?? Array.Empty<string>()).Select(Normalize).Where(k => k.Length > 0).Distinct();

    public EquipmentModel? Resolve(string? id, string? name, string? profileName = null)
    {
        // Never substitute a different known product for an unrecognised explicit ID.
        if (!string.IsNullOrWhiteSpace(id)) return Models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(name))
        {
            var exact = Models.Where(m => Keys(m).Contains(Normalize(name))).ToArray();
            return exact.Length == 1 ? exact[0] : null;
        }
        var text = Normalize(profileName);
        var candidates = Models.SelectMany(m => Keys(m).Where(k => ContainsModel(text, k)).Select(k => (Model: m, Length: k.Length))).ToArray();
        if (candidates.Length == 0) return null;
        var longest = candidates.Max(c => c.Length);
        var matches = candidates.Where(c => c.Length == longest).Select(c => c.Model).Distinct().ToArray();
        // Multiple product signatures in a profile title are ambiguous, regardless of their lengths.
        if (candidates.Select(c => c.Model).Distinct().Count() > 1) return null;
        return matches.Length == 1 ? matches[0] : null;
    }
    private static bool ContainsModel(string text, string key)
    {
        var index = text.IndexOf(key, StringComparison.Ordinal);
        if (index < 0) return false;
        var tail = text[(index + key.Length)..];
        // Preserve unknown revisions/port variants rather than matching a shorter base model.
        return !(tail.Length > 0 && (char.IsDigit(tail[0]) || tail[0] == '+')) &&
               !new[] { "pro", "ex", "ups", "box" }.Any(s => tail.StartsWith(s, StringComparison.Ordinal));
    }
}
