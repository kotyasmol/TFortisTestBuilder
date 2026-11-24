using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TestBuilder.Services;

public sealed class LabelTemplateFile
{
    public int SchemaVersion { get; set; } = 1;
    public bool IsComplete { get; set; }
    public List<LabelTemplateDefinition> Templates { get; set; } = new();
}

public static class LabelTemplateStore
{
    public static string BundledPath => Path.Combine(AppContext.BaseDirectory, "reference", "labels", "templates.json");
    public static string LocalPath(string profilesFolder) => Path.Combine(profilesFolder, "catalog", "label-templates.json");
    public static List<LabelTemplateDefinition> Load(string profilesFolder, IEnumerable<LabelTemplateDefinition> legacy)
    {
        var result = legacy.Select(t => t.CopyUpgraded()).ToList();
        foreach (var file in new[] { BundledPath, LocalPath(profilesFolder) })
        {
            var catalog = Read(file);
            if (catalog.IsComplete) result.Clear();
            foreach (var template in catalog.Templates)
            {
                var index = result.FindIndex(t => t.Id == template.Id);
                if (index >= 0) result[index] = template.CopyUpgraded(); else result.Add(template.CopyUpgraded());
            }
        }
        LabelTemplateCatalog.Validate(result);
        return result;
    }
    public static LabelTemplateFile Read(string file)
    {
        if (!File.Exists(file)) return new();
        if (new FileInfo(file).Length > 4_000_000) throw new InvalidDataException("Каталог этикеток больше 4 МБ.");
        var result = JsonSerializer.Deserialize<LabelTemplateFile>(File.ReadAllText(file), EquipmentCatalog.JsonOptions)
            ?? throw new InvalidDataException("Пустой каталог этикеток.");
        if (result.SchemaVersion != 1 || result.Templates == null) throw new InvalidDataException("Неподдерживаемая версия каталога этикеток.");
        if (result.Templates.Count > 0 || result.IsComplete) LabelTemplateCatalog.Validate(result.Templates);
        return result;
    }
    public static string Revision(string file)
    {
        if (!File.Exists(file)) return "missing";
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }
    public static void Save(string file, IEnumerable<LabelTemplateDefinition> templates, bool preserveExisting = false, string? expectedRevision = null)
    {
        var values = templates.Select(t => t.Copy()).ToList(); LabelTemplateCatalog.Validate(values);
        var json = JsonSerializer.Serialize(new LabelTemplateFile { IsComplete = true, Templates = values }, EquipmentCatalog.JsonOptions);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 4_000_000) throw new InvalidDataException("Каталог этикеток больше 4 МБ.");
        var path = Path.GetFullPath(file); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var editLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (expectedRevision != null && Revision(path) != expectedRevision) throw new InvalidDataException("Каталог изменён другим пользователем. Откройте его заново, чтобы сохранить обе правки.");
        if (preserveExisting) Read(path); // A corrupt local catalog requires explicit import/repair, never silent replacement.
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, json); if (File.Exists(path)) File.Copy(path, path + ".bak", true); File.Move(temporary, path, true); }
        finally { File.Delete(temporary); }
    }
    public static List<LabelTemplateDefinition> Merge(IEnumerable<LabelTemplateDefinition> existing, LabelTemplateFile incoming)
    {
        if (incoming.SchemaVersion != 1 || incoming.Templates == null || incoming.Templates.Count == 0) throw new InvalidDataException("Неподдерживаемая версия или пустой каталог этикеток.");
        LabelTemplateCatalog.Validate(incoming.Templates);
        var result = existing.Select(t => t.Copy()).ToList();
        foreach (var template in incoming.Templates)
        {
            var index = result.FindIndex(t => t.Id == template.Id);
            if (index >= 0) result[index] = template.CopyUpgraded(); else result.Add(template.CopyUpgraded());
        }
        LabelTemplateCatalog.Validate(result); return result;
    }
}
