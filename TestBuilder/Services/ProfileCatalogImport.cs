using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace TestBuilder.Services;

public enum CatalogImportChoice { Cancel, Local, Profile }
public sealed record CatalogImportConflict(EquipmentModel Local, EquipmentModel Profile);

internal sealed record ProfileCatalogImport(string Json, EquipmentModel? Expected, EquipmentModel? Card, bool WriteCard)
{
    public static async Task<ProfileCatalogImport?> PrepareAsync(string json, string folder,
        Func<CatalogImportConflict, Task<CatalogImportChoice>>? resolve)
    {
        var root = JsonNode.Parse(json, new JsonNodeOptions { PropertyNameCaseInsensitive = true })!.AsObject();
        var incoming = root["catalogModel"]?.Deserialize<EquipmentModel>(EquipmentCatalog.JsonOptions);
        var catalog = EquipmentCatalog.Load(folder);
        var current = incoming != null ? catalog.Resolve(incoming.Id, null)
            : catalog.Resolve(root["deviceModelId"]?.GetValue<string>(), root["deviceModel"]?.GetValue<string>());
        if (incoming == null)
        {
            if (root["useCatalogSerials"]?.GetValue<bool>() == true && current == null)
                throw new InvalidDataException("В JSON нет карточки изделия, и её нет в справочнике этого компьютера. На исходном компьютере выберите «Экспорт профиля» и импортируйте полученный JSON.");
            return new(json, null, null, false);
        }
        var chosen = incoming;
        if (current != null && !ProfileCatalogLink.Equivalent(current, incoming))
        {
            if (resolve == null) throw new InvalidDataException($"Карточка «{incoming.Name}» отличается от справочника. Выберите данные для импорта.");
            var choice = await resolve(new(current, incoming));
            if (choice == CatalogImportChoice.Cancel) return null;
            if (choice == CatalogImportChoice.Local) chosen = current;
            else if (choice != CatalogImportChoice.Profile) throw new InvalidDataException("Неизвестный выбор карточки изделия.");
        }
        return new(ProfileCatalogLink.WithCard(root, chosen), current, chosen,
            !ProfileCatalogLink.Equivalent(chosen, current));
    }

    public void Publish(string folder, Action publish)
    {
        if (Card == null) { publish(); return; }
        EquipmentCatalog.ImportAndPublish(folder, Card, Expected, WriteCard, publish);
    }
}

public sealed partial class EquipmentCatalog
{
    internal static void ImportAndPublish(string folder, EquipmentModel card, EquipmentModel? expected, bool writeCard, Action publish)
    {
        var path = LocalPath(folder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var editLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var resolved = Load(folder);
        var current = resolved.Resolve(card.Id, null);
        if (!ProfileCatalogLink.Equivalent(current, expected))
            throw new IOException("Карточка изменилась во время импорта. Повторите импорт и сравните данные заново.");
        if (!writeCard) { publish(); return; }
        var other = resolved.Models.FirstOrDefault(m => !m.Id.Equals(card.Id, StringComparison.OrdinalIgnoreCase) &&
            Keys(m).Intersect(Keys(card)).Any());
        if (other != null)
            throw new InvalidDataException($"Название «{card.Name}» уже связано с другим ID ({other.Id}). Проверьте карточки перед импортом.");
        var existed = File.Exists(path);
        var original = existed ? File.ReadAllBytes(path) : null;
        var catalog = Read(path);
        var index = catalog.Models.FindIndex(m => m.Id.Equals(card.Id, StringComparison.OrdinalIgnoreCase));
        var imported = card with { IsFullOverride = true };
        if (index >= 0) catalog.Models[index] = imported;
        else catalog.Models.Add(imported);
        catalog.Validate();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var installed = false;
        try
        {
            File.WriteAllText(temporary, catalog.ToJson());
            File.Move(temporary, path, overwrite: true);
            installed = true;
            publish();
        }
        catch
        {
            // The catalog lock also protects rollback, so no other editor's card is reverted.
            if (installed)
            {
                if (original == null) File.Delete(path);
                else { File.WriteAllBytes(temporary, original); File.Move(temporary, path, overwrite: true); }
            }
            throw;
        }
        finally { File.Delete(temporary); }
    }
}
