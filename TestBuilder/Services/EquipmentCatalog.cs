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
    public Dictionary<int, string>? LabelCabinets { get; init; }
    public ModelSerialSeries? SerialSeries { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsFullOverride { get; init; }
    internal string? EffectiveServerDeviceType => SerialSeries?.ServerDeviceType.Trim() ?? ServerDeviceType?.Trim();
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; init; }
    public override string ToString() => Name;
}

public sealed record RtlReportSettings(int DeviceType, bool AssignSerialOnReport)
{
    public string DeviceIdentifierVariable { get; init; } = "CpuId";
    public string? FailureDeviceIdentifier { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed class EquipmentCatalog
{
    public int SchemaVersion { get; set; } = 1;
    public string Revision { get; set; } = "";
    public List<EquipmentModel> Models { get; set; } = new();
    internal static string RelativePath { get; } = Path.Combine("catalog", "equipment-models.json");
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    [JsonIgnore] internal string BundledRevision { get; private set; } = "";
    [JsonIgnore] internal string LocalRevision { get; private set; } = "";
    [JsonIgnore] internal Dictionary<string, string> Sources { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
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
        var bundled = LoadBundled();
        var local = Read(LocalPath(profilesFolder));
        var models = bundled.Models.ToDictionary(model => model.Id, StringComparer.OrdinalIgnoreCase);
        var sources = bundled.Models.ToDictionary(model => model.Id, _ => "Поставка", StringComparer.OrdinalIgnoreCase);
        foreach (var model in local.Models)
        {
            // Older installations retain their original local catalog after updates.
            if (!model.IsFullOverride && models.TryGetValue(model.Id, out var baseline) && Keys(baseline).Contains(Normalize(model.Name)))
            {
                sources[model.Id] = "Поставка + локальные поля";
                var series = model.SerialSeries;
                var bundledSeries = baseline.SerialSeries;
                if (series is { FailureReportSerialNumber: null } &&
                    bundledSeries?.FailureReportSerialNumber is { } fallback &&
                    string.Equals(series.ServerDeviceType, bundledSeries.ServerDeviceType, StringComparison.OrdinalIgnoreCase) &&
                    series.Check(fallback) == null)
                    series = series with { FailureReportSerialNumber = fallback };
                models[model.Id] = model with
                {
                    LabelCode = model.LabelCode ?? baseline.LabelCode,
                    ServerDeviceType = model.ServerDeviceType ?? baseline.ServerDeviceType,
                    RtlReport = model.RtlReport is { } report && baseline.RtlReport is { } baselineReport &&
                        report.DeviceType == baselineReport.DeviceType
                        ? report with { FailureDeviceIdentifier = report.FailureDeviceIdentifier ?? baselineReport.FailureDeviceIdentifier }
                        : model.RtlReport ?? baseline.RtlReport,
                    LabelVariants = model.LabelVariants ?? baseline.LabelVariants,
                    LabelCabinets = model.LabelCabinets ?? (model.LabelVariants == null || baseline.LabelVariants != null &&
                        model.LabelVariants.OrderBy(pair => pair.Key).SequenceEqual(baseline.LabelVariants.OrderBy(pair => pair.Key))
                        ? baseline.LabelCabinets : null),
                    SerialSeries = series ?? (model.ServerDeviceType == null ? bundledSeries : null)
                };
            }
            else
            {
                models[model.Id] = model;
                sources[model.Id] = "Локальная карточка";
            }
        }
        local.BundledRevision = bundled.Revision;
        local.LocalRevision = local.Revision;
        local.Sources = sources;
        local.Models = models.Values.ToList();
        return local;
    }

    internal static EquipmentCatalog LoadBundled() => Read(BundledPath);
    internal string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    private static EquipmentCatalog Read(string file)
    {
        if (!File.Exists(file)) return new EquipmentCatalog();
        var catalog = JsonSerializer.Deserialize<EquipmentCatalog>(File.ReadAllText(file), JsonOptions)
            ?? throw new InvalidDataException("Пустой каталог изделий.");
        catalog.Validate();
        return catalog;
    }

    internal static void SaveModel(string profilesFolder, EquipmentModel model, bool createNew = false, EquipmentModel? original = null)
    {
        var file = LocalPath(profilesFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var editLock = new FileStream(file + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (createNew || original != null)
        {
            if (Normalize(model.Name).Length == 0) throw new InvalidDataException("Укажите название изделия.");
            var models = Load(profilesFolder).Models;
            var current = models.FirstOrDefault(item => string.Equals(item.Id, model.Id, StringComparison.OrdinalIgnoreCase));
            if (original != null && JsonSerializer.Serialize(current, JsonOptions) != JsonSerializer.Serialize(original, JsonOptions))
                throw new InvalidDataException("Карточка уже изменена. Откройте её заново.");
            var newKeys = Keys(model).Except(current == null ? Array.Empty<string>() : Keys(current)).ToArray();
            if (createNew && current != null || models.Any(item =>
                !string.Equals(item.Id, model.Id, StringComparison.OrdinalIgnoreCase) && Keys(item).Intersect(newKeys).Any()))
                throw new InvalidDataException("Такое изделие уже есть в справочнике. Выберите его в списке.");
        }
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
            if (model.LabelCode is < 0 or > 999)
                throw new InvalidDataException($"Для «{model.Name}» код модели на этикетке должен быть от 000 до 999.");
            if (model.LabelCode.HasValue && !LabelTemplateDefinition.IsSafeLabelText(model.Name))
                throw new InvalidDataException($"Название изделия «{model.Name}» не должно содержать команды принтера (^, ~, кавычки и переносы строк).");
            if (model.LabelVariants?.Any(pair => pair.Key is < 0 or > 9 || string.IsNullOrWhiteSpace(pair.Value) ||
                !LabelTemplateDefinition.IsSafeLabelText(pair.Value)) == true)
                throw new InvalidDataException($"Для «{model.Name}» задайте коды комплектаций от 0 до 9 и названия без команд принтера.");
            if (model.LabelCabinets?.Any(pair => pair.Key is < 0 or > 9 || string.IsNullOrWhiteSpace(pair.Value) ||
                !LabelTemplateDefinition.IsSafeLabelText(pair.Value) || model.LabelVariants?.ContainsKey(pair.Key) != true) == true)
                throw new InvalidDataException($"Для каждой подписи шкафа изделия «{model.Name}» задайте комплектацию с тем же кодом 0–9. Подпись не должна содержать команды принтера.");
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
    public static bool Matches(EquipmentModel model, string? query) => Matches(model.Name, query) ||
        (model.Aliases?.Any(alias => Matches(alias, query)) == true) ||
        model.LabelCode is { } code && code.ToString("D3").Contains((query ?? "").Trim(), StringComparison.Ordinal);

    public static string BundledPath => Path.Combine(AppContext.BaseDirectory, "reference", "serials", "equipment-models.json");
    public static string LocalPath(string profilesFolder) => Path.Combine(profilesFolder, RelativePath);

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
