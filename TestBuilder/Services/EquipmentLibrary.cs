using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using TestBuilder.Domain.Modbus;

namespace TestBuilder.Services;

public sealed record EquipmentTemplate(string Id, DeviceDefinition Device, bool IsBuiltIn = false)
{
    [JsonIgnore]
    public string Display => Device.Name + (IsBuiltIn ? " · встроенное" : " · своё");
    [JsonIgnore]
    public string Details => $"Регистров: {Device.Registers.Count}. Тип: " +
        (Device.ExpectedIdentificationValues.Count == 0 ? "не задан" :
        $"регистр {Device.IdentificationAddress} = {string.Join(" или ", Device.ExpectedIdentificationValues)}");
}

/// <summary>Reusable definitions; profiles always receive independent snapshots.</summary>
public sealed class EquipmentLibrary
{
    private readonly string _path;
    public string StoragePath => Path.GetFullPath(_path);
    public EquipmentLibrary(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TFortisTestBuilder", "equipment-library.json");
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    public IReadOnlyList<EquipmentTemplate> Load()
        => HardwarePresets.Load().Select(d => new EquipmentTemplate("builtin:" + d.Name, d, true))
            .Concat(ReadCustom()).ToArray();
    private List<EquipmentTemplate> ReadCustom()
    {
        if (!File.Exists(_path)) return new();
        var entries = JsonSerializer.Deserialize<List<EquipmentTemplate>>(File.ReadAllText(_path))
            ?? throw new InvalidOperationException("Каталог оборудования пуст или повреждён.");
        ValidateEntries(entries);
        return entries;
    }
    private static void ValidateEntries(List<EquipmentTemplate> entries)
    {
        foreach (var entry in entries)
        {
            if (entry == null || entry.IsBuiltIn || !Guid.TryParse(entry.Id, out _))
                throw new InvalidOperationException("Некорректная запись каталога оборудования.");
            Validate(entry.Device);
        }
        if (entries.Select(e => e.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
            throw new InvalidOperationException("В каталоге повторяются идентификаторы оборудования.");
        if (entries.Select(e => e.Device.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
            throw new InvalidOperationException("В каталоге повторяются названия оборудования.");
    }

    public string ExportJson() => JsonSerializer.Serialize(new Transfer(1, ReadCustom()), new JsonSerializerOptions { WriteIndented = true });

    public EquipmentLibraryImport PreviewImport(string json)
    {
        using var document = JsonDocument.Parse(json);
        List<EquipmentTemplate> incoming;
        if (document.RootElement.ValueKind == JsonValueKind.Array)
            incoming = JsonSerializer.Deserialize<List<EquipmentTemplate>>(json) ?? throw new InvalidOperationException("Пустой каталог.");
        else
        {
            var transfer = JsonSerializer.Deserialize<Transfer>(json) ?? throw new InvalidOperationException("Пустой каталог.");
            if (transfer.SchemaVersion != 1) throw new InvalidOperationException("Неизвестная версия каталога оборудования.");
            incoming = transfer.Entries ?? throw new InvalidOperationException("В файле нет записей каталога.");
        }
        ValidateEntries(incoming);
        var current = ReadCustom();
        var original = JsonSerializer.Serialize(current);
        var added = 0; var updated = 0; var unchanged = 0;
        var changes = new List<string>();
        foreach (var entry in incoming)
        {
            var copy = Copy(entry); copy.Device.SlaveId = 1; copy.Device.Name = copy.Device.Name.Trim();
            var index = current.FindIndex(e => e.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
            if (index < 0) { current.Add(copy); added++; changes.Add("Добавить: " + copy.Device.Name); }
            else if (JsonSerializer.Serialize(current[index]) == JsonSerializer.Serialize(copy)) unchanged++;
            else { current[index] = copy; updated++; changes.Add("Обновить: " + copy.Device.Name); }
        }
        ValidateEntries(current);
        return new EquipmentLibraryImport(added, updated, unchanged, original, current, changes);
    }

    public void ApplyImport(EquipmentLibraryImport preview)
    {
        if (JsonSerializer.Serialize(ReadCustom()) != preview.Original)
            throw new InvalidOperationException("Каталог изменился. Отмените импорт и выберите файл заново.");
        ValidateEntries(preview.Entries);
        Write(Copy(preview.Entries));
    }

    private sealed record Transfer(int SchemaVersion, List<EquipmentTemplate> Entries);
    public EquipmentTemplate Save(DeviceDefinition device, string? id = null)
    {
        var snapshot = Copy(device);
        snapshot.SlaveId = 1; // A bus address belongs to the profile, not to the template.
        snapshot.Name = snapshot.Name.Trim();
        Validate(snapshot);
        var entries = ReadCustom();
        if (id != null && !entries.Any(e => e.Id == id))
            throw new InvalidOperationException("Запись каталога не найдена. Сохраните как новое оборудование.");
        if (entries.Any(e => e.Id != id && string.Equals(e.Device.Name, snapshot.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Оборудование с таким названием уже есть в каталоге.");
        var entry = new EquipmentTemplate(id ?? Guid.NewGuid().ToString(), snapshot);
        entries.RemoveAll(e => e.Id == id);
        entries.Add(entry);
        Write(entries);
        return Copy(entry);
    }
    public static DeviceDefinition Instantiate(EquipmentTemplate entry, IEnumerable<byte> usedIds)
    {
        var used = usedIds.ToHashSet();
        var id = Enumerable.Range(1, 247).FirstOrDefault(i => !used.Contains((byte)i));
        if (id == 0) throw new InvalidOperationException("Все адреса slave заняты.");
        var result = Copy(entry.Device);
        result.SlaveId = (byte)id;
        return result;
    }
    private static void Validate(DeviceDefinition device) =>
        new HardwareConfiguration { Devices = new() { device } }.Validate();
    private void Write(List<EquipmentTemplate> entries)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, Guid.NewGuid() + ".tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed record EquipmentLibraryImport(int Added, int Updated, int Unchanged,
    string Original, List<EquipmentTemplate> Entries, List<string> Changes);
