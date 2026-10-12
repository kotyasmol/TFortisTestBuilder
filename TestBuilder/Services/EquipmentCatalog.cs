using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace TestBuilder.Services;

public sealed record EquipmentModel(string Id, string Name)
{
    public string[] Aliases { get; init; } = Array.Empty<string>();
    public override string ToString() => Name;
}

public sealed class EquipmentCatalog
{
    public int SchemaVersion { get; set; } = 1;
    public string Revision { get; set; } = "";
    public List<EquipmentModel> Models { get; set; } = new();
    public static EquipmentCatalog Load(string profilesFolder)
    {
        var file = Path.Combine(profilesFolder, "catalog", "equipment-models.json");
        if (!File.Exists(file)) return new EquipmentCatalog();
        var catalog = JsonSerializer.Deserialize<EquipmentCatalog>(File.ReadAllText(file),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Пустой каталог изделий.");
        if (catalog.SchemaVersion != 1 || catalog.Models == null || catalog.Models.Any(m => m == null || string.IsNullOrWhiteSpace(m.Id) || string.IsNullOrWhiteSpace(m.Name)) ||
            catalog.Models.Select(m => m.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != catalog.Models.Count)
            throw new InvalidDataException("Некорректная версия каталога или повторяющиеся ID изделий.");
        return catalog;
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
