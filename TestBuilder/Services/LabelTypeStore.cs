using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TestBuilder.Services;

public sealed record LabelTypeDefinition(string Id, string Name, string? ProductModelId, LabelTemplateDefinition Template, string? BaseTypeId = null)
{
    public LabelTypeDefinition Copy() => this with { Template = Template.Copy() };
}

public sealed record LabelTypeAssignment(string ProductModelId, string LabelTypeId);

public sealed class LabelTypeFile
{
    [System.Text.Json.Serialization.JsonRequired]
    public int SchemaVersion { get; set; } = 2;
    [System.Text.Json.Serialization.JsonRequired]
    public List<LabelTypeDefinition> Types { get; set; } = new();
    public List<LabelTypeAssignment> Assignments { get; set; } = new();
}

/// <summary>Shared print types, separate from snapshots embedded in profiles and print history.</summary>
public static class LabelTypeStore
{
    public const string DeviceTypeId = "shared:device";
    public const string BoardTypeId = "shared:board";
    public const string CabinetTypeId = "shared:device-cabinet";
    public static bool IsUniversal(string id) => id is BoardTypeId or DeviceTypeId or CabinetTypeId;
    public static LabelTypeDefinition[] UniversalDefaults() => new[]
    {
        new LabelTypeDefinition(BoardTypeId, "Штрихкод и серийный номер", null, UniversalTemplate(BoardTypeId)),
        new LabelTypeDefinition(DeviceTypeId, "Название, MAC, SN и штрихкод", null, UniversalTemplate(DeviceTypeId)),
        new LabelTypeDefinition(CabinetTypeId, "Название, MAC, SN, штрихкод и шкаф", null, UniversalTemplate(CabinetTypeId))
    };
    public static string LocalPath(string profilesFolder) => Path.Combine(profilesFolder, "catalog", "label-types.json");
    public static string BundledPath => Path.Combine(AppContext.BaseDirectory, "reference", "labels", "types.json");
    public static List<LabelTypeDefinition> Load(string profilesFolder) => LoadFile(profilesFolder).Types;
    public static LabelTypeFile LoadFile(string profilesFolder) => Merge(Read(BundledPath), Read(LocalPath(profilesFolder)));
    public static List<LabelTypeDefinition> Available(LabelTypeFile file)
        => UniversalDefaults().Select(t => file.Types.FirstOrDefault(s => s.Id == t.Id)?.Copy() ?? t)
            .Concat(file.Types.Where(t => !IsUniversal(t.Id) && t.ProductModelId == null).Select(t => t.Copy())).ToList();
    public static string? AssignedTypeId(LabelTypeFile file, string? modelId)
        => file.Assignments.FirstOrDefault(a => string.Equals(a.ProductModelId, modelId, StringComparison.OrdinalIgnoreCase))?.LabelTypeId is { Length: > 0 } id ? id : null;

    public static LabelTypeFile Read(string path)
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 4_000_000) throw new InvalidDataException("Каталог типов этикеток больше 4 МБ.");
        var file = JsonSerializer.Deserialize<LabelTypeFile>(File.ReadAllText(path), EquipmentCatalog.JsonOptions)
            ?? throw new InvalidDataException("Пустой файл типов этикеток.");
        Validate(file); return file;
    }

    public static void Validate(LabelTypeFile file)
    {
        if (file.SchemaVersion is not (1 or 2) || file.Types == null || file.Assignments == null || file.Types.Any(t => t == null ||
            string.IsNullOrWhiteSpace(t.Id) || string.IsNullOrWhiteSpace(t.Name) || t.Template == null) ||
            file.Types.Select(t => t.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != file.Types.Count)
            throw new InvalidDataException("Неподдерживаемый формат или повторяющиеся типы этикеток.");
        var bindings = file.Types.Where(t => t.ProductModelId != null).ToArray();
        if (bindings.Any(t => string.IsNullOrWhiteSpace(t.ProductModelId)) ||
            bindings.Select(t => t.ProductModelId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != bindings.Length)
            throw new InvalidDataException("Для изделия можно сохранить только один тип этикетки.");
        foreach (var type in file.Types)
        {
            if (IsUniversal(type.Id) && type.ProductModelId != null)
                throw new InvalidDataException("Общий тип этикетки нельзя привязать к одному изделию.");
            if (type.BaseTypeId != null && !IsUniversal(type.BaseTypeId))
                throw new InvalidDataException("Заготовка типа этикетки должна быть одним из трёх универсальных типов.");
            type.Template.Validate();
        }
        if (file.SchemaVersion == 1 && (file.Assignments.Count > 0 || file.Types.Any(t => t.Template.DataBindings != null || t.BaseTypeId != null)))
            throw new InvalidDataException("Источники данных и привязки изделий требуют каталог типов версии 2.");
        if (file.Assignments.Any(a => a == null || string.IsNullOrWhiteSpace(a.ProductModelId) || a.LabelTypeId == null) ||
            file.Assignments.Select(a => a.ProductModelId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != file.Assignments.Count)
            throw new InvalidDataException("Повторяющиеся или пустые привязки изделий к типам этикеток.");
        foreach (var assignment in file.Assignments.Where(a => a.LabelTypeId.Length > 0))
            if (!IsUniversal(assignment.LabelTypeId) && !file.Types.Any(t => t.ProductModelId == null && string.Equals(t.Id, assignment.LabelTypeId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Для изделия '{assignment.ProductModelId}' не найден тип этикетки '{assignment.LabelTypeId}'. Импортируйте его вместе с привязкой.");
    }

    public static LabelTypeFile Merge(IEnumerable<LabelTypeDefinition> existing, LabelTypeFile incoming)
        => Merge(new LabelTypeFile { Types = existing.ToList() }, incoming);

    public static LabelTypeFile Merge(LabelTypeFile existing, LabelTypeFile incoming)
    {
        Validate(incoming);
        var result = existing.Types.Select(t => t.Copy()).ToList();
        foreach (var type in incoming.Types)
        {
            var index = result.FindIndex(t => string.Equals(t.Id, type.Id, StringComparison.OrdinalIgnoreCase) ||
                type.ProductModelId != null && string.Equals(t.ProductModelId, type.ProductModelId, StringComparison.OrdinalIgnoreCase));
            if (index < 0) result.Add(type.Copy()); else result[index] = type.Copy();
        }
        var assignments = existing.Assignments.ToList();
        foreach (var assignment in incoming.Assignments)
        {
            assignments.RemoveAll(a => string.Equals(a.ProductModelId, assignment.ProductModelId, StringComparison.OrdinalIgnoreCase));
            assignments.Add(assignment);
        }
        var file = new LabelTypeFile { Types = result, Assignments = assignments }; Validate(file); return file;
    }

    public static void Save(string path, IEnumerable<LabelTypeDefinition> types, string? expectedRevision = null)
        => Save(path, new LabelTypeFile { Types = types.Select(t => t.Copy()).ToList(), Assignments = Read(path).Assignments }, expectedRevision);

    public static void Save(string path, LabelTypeFile file, string? expectedRevision = null)
    {
        Validate(file);
        var json = JsonSerializer.Serialize(file, EquipmentCatalog.JsonOptions);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 4_000_000) throw new InvalidDataException("Каталог типов этикеток больше 4 МБ.");
        path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var editLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (expectedRevision != null && LabelTemplateStore.Revision(path) != expectedRevision)
            throw new InvalidDataException("Типы этикеток изменены в другом окне или на другом компьютере. Экспортируйте свой черновик и откройте каталог заново.");
        Read(path); // Never silently replace a damaged catalog.
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, json); if (File.Exists(path)) File.Copy(path, path + ".bak", true); File.Move(temporary, path, true); }
        finally { File.Delete(temporary); }
    }

    public static LabelTemplateDefinition FamilyDefault(bool board)
        => UniversalTemplate(board ? BoardTypeId : DeviceTypeId);

    public static LabelTemplateDefinition UniversalTemplate(string id, LabelTemplateDefinition? source = null)
    {
        if (!IsUniversal(id)) throw new InvalidOperationException("Неизвестный универсальный тип этикетки.");
        var board = id == BoardTypeId;
        var template = (source ?? LabelTemplateCatalog.Defaults().Single(t => t.Id == (board ? "SerialOnlyEpl" : "PswUpsBox8x2Pro"))).CopyUpgraded();
        if (board && source == null)
        {
            template = LabelPrintContent.ForModel(template, new EquipmentModel("board", "название устройства") { LabelCode = 0 }, 0);
        }
        template.Layout = LabelLayoutDefinition.FromTemplate(template);
        if (id != CabinetTypeId) template.Layout.Elements.RemoveAll(e => e.Value.Contains("{{Cabinet}}", StringComparison.Ordinal));
        else if (!template.Layout.Elements.Any(e => e.Value.Contains("{{Cabinet}}", StringComparison.Ordinal)))
            template.Layout.Elements.Add(new() { Name = "Шкаф", Value = "{{Cabinet}}", X = Math.Max(0, template.WidthDots - template.CabinetWidthDots),
                Y = template.Layout.Elements.FirstOrDefault(e => e.Value.Contains("{{SerialText}}", StringComparison.Ordinal))?.Y ?? 95,
                Width = template.CabinetWidthDots, Height = template.CabinetFontHeightDots, FontWidth = 12 });
        template.Id = id; template.Name = "название устройства";
        template.UseShortSerial = !board; template.RequiresMac = !board; template.MatchServerSerial = false;
        template.SerialOffset = 0; template.FirstSerialNumber = null; template.ShortSerialMax = 65535;
        template.DeviceCode = 0; template.BarcodePrefix = "000"; template.VariantCode = 0; template.VariantName = null;
        template.CabinetName = id == CabinetTypeId ? "Шкаф 1" : null;
        template.DataBindings?.ApplyPresentation(template);
        template.Validate();
        return template;
    }

    internal static string TypeIdFor(LabelTemplateDefinition fallback, EquipmentModel? model, int variant = 0)
    {
        if (model?.RtlReport != null || model == null && !fallback.UseShortSerial) return BoardTypeId;
        var cabinet = model != null ? model.LabelCabinets?.GetValueOrDefault(variant) : fallback.CabinetName;
        return string.IsNullOrWhiteSpace(cabinet) ? DeviceTypeId : CabinetTypeId;
    }

    internal static LabelTemplateDefinition Resolve(LabelTemplateDefinition fallback, EquipmentModel? model,
        string? deviceName, int variant = 0, IReadOnlyList<LabelTypeDefinition>? types = null, LabelTypeFile? file = null)
    {
        file ??= types == null ? LoadFile(AppSettings.Instance.ResolvedGraphsFolder) : new() { Types = types.ToList() };
        types ??= file.Types;
        if (model == null && (types.Count > 0 || file.Assignments.Count > 0) && !string.IsNullOrWhiteSpace(deviceName))
            model = EquipmentCatalog.Load(AppSettings.Instance.ResolvedGraphsFolder).Resolve(null, deviceName);
        var typeId = TypeIdFor(fallback, model, variant);
        var assignedId = AssignedTypeId(file, model?.Id);
        var assigned = assignedId == null ? null : Available(file).FirstOrDefault(t => string.Equals(t.Id, assignedId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Не найден назначенный тип этикетки '{assignedId}'.");
        var chosen = assigned ?? types.FirstOrDefault(t => (model != null || fallback.UseShortSerial || fallback.DeviceCode > 0) && t.Id == typeId)
            ?? types.FirstOrDefault(t => model != null && string.Equals(t.ProductModelId, model.Id, StringComparison.OrdinalIgnoreCase))
            ?? types.FirstOrDefault(t => t.Id == fallback.Id)
            // Before cabinet became a separate universal type the device family included it.
            ?? types.FirstOrDefault(t => typeId == CabinetTypeId && t.Id == DeviceTypeId &&
                (t.Template.Layout?.Elements.Any(e => e.Value.Contains("{{Cabinet}}", StringComparison.Ordinal)) == true || t.Template.Command.Contains("{{CabinetField}}", StringComparison.Ordinal)));
        var template = chosen?.Template ?? fallback;
        return ApplyPresentation(template, fallback, model, deviceName, variant);
    }

    internal static LabelTemplateDefinition ApplyPresentation(LabelTemplateDefinition template, LabelTemplateDefinition fallback,
        EquipmentModel? model, string? deviceName, int variant = 0)
    {
        if (model != null)
        {
            var resolved = LabelPrintContent.ForModel(template, model, variant);
            resolved.MatchServerSerial = fallback.MatchServerSerial && resolved.UseShortSerial;
            resolved.DataBindings?.ApplyPresentation(resolved);
            return resolved;
        }
        var copy = LabelPrintContent.ForDevice(template, string.IsNullOrWhiteSpace(deviceName) ? fallback.Name : deviceName);
        if (IsUniversal(template.Id) || template.Id.StartsWith("custom:", StringComparison.Ordinal))
        {
            // Legacy nodes can carry their model's identity without an external catalog card.
            // A family supplies presentation, never a representative model's serial range or type.
            copy.DeviceCode = fallback.DeviceCode; copy.SerialOffset = fallback.SerialOffset;
            copy.MatchServerSerial = fallback.MatchServerSerial && copy.UseShortSerial;
            copy.FirstSerialNumber = fallback.FirstSerialNumber; copy.ShortSerialMax = fallback.ShortSerialMax;
            copy.BarcodePrefix = fallback.BarcodePrefix; copy.VariantCode = variant;
            copy.VariantName = fallback.VariantName; copy.CabinetName = fallback.CabinetName;
        }
        copy.DataBindings?.ApplyPresentation(copy);
        copy.Validate(); return copy;
    }
}
