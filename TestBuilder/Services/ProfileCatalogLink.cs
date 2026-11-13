using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TestBuilder.Services;

/// <summary>The external catalog remains authoritative; the card in a profile is a portable fallback.</summary>
internal static class ProfileCatalogLink
{
    public static void Validate(EquipmentModel? card, string? id, string? name)
    {
        if (card == null) return;
        new EquipmentCatalog { Models = { card } }.Validate();
        var matches = !string.IsNullOrWhiteSpace(id)
            ? string.Equals(id, card.Id, StringComparison.OrdinalIgnoreCase)
            : EquipmentCatalog.Normalize(name ?? "") == EquipmentCatalog.Normalize(card.Name) ||
                (card.Aliases ?? Array.Empty<string>()).Any(alias => EquipmentCatalog.Normalize(alias) == EquipmentCatalog.Normalize(name ?? ""));
        if (!matches)
            throw new InvalidDataException("Карточка в JSON относится к другому изделию: проверьте deviceModelId и catalogModel.id.");
    }

    public static EquipmentModel? Resolve(EquipmentCatalog catalog, EquipmentModel? fallback, string? id, string? name)
    {
        Validate(fallback, id, name);
        return catalog.Resolve(id, name) ?? fallback;
    }

    public static bool Equivalent(EquipmentModel? left, EquipmentModel? right)
    {
        JsonNode? Data(EquipmentModel? model)
        {
            if (model == null) return null;
            var node = JsonSerializer.SerializeToNode(model, EquipmentCatalog.JsonOptions)!;
            node.AsObject().Remove("isFullOverride");
            return node;
        }
        return JsonNode.DeepEquals(Data(left), Data(right));
    }

    public static string Attach(string json, EquipmentCatalog catalog, GraphProfile? legacy = null)
    {
        var root = JsonNode.Parse(json, new JsonNodeOptions { PropertyNameCaseInsensitive = true })!.AsObject();
        var id = root["deviceModelId"]?.GetValue<string>() ?? legacy?.DeviceModelId;
        var name = root["deviceModel"]?.GetValue<string>() ?? legacy?.DeviceModel;
        var fallback = root["catalogModel"]?.Deserialize<EquipmentModel>(EquipmentCatalog.JsonOptions);
        var card = Resolve(catalog, fallback, id, name);
        if (card == null && root["useCatalogSerials"]?.GetValue<bool>() == true)
            throw new InvalidDataException("В справочнике нет карточки изделия. Добавьте её перед переносом профиля.");
        if (card == null) return json;
        return WithCard(root, card);
    }

    public static string WithCard(JsonObject root, EquipmentModel card)
    {
        var previous = root["deviceModel"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(previous) && previous != card.Name)
        {
            var description = root["configurationName"]?.GetValue<string>() ?? root["name"]?.GetValue<string>() ?? "";
            foreach (var separator in new[] { " · ", " — ", " - " })
                if (description.StartsWith(previous + separator, StringComparison.OrdinalIgnoreCase))
                { description = description[(previous.Length + separator.Length)..]; break; }
            if (description == previous) description = "";
            root["configurationName"] = description;
            root["name"] = GraphProfile.ComposeName(card.Name, description);
        }
        root["deviceModelId"] = card.Id;
        root["deviceModel"] = card.Name;
        root["catalogModel"] = JsonSerializer.SerializeToNode(card, EquipmentCatalog.JsonOptions);
        return root.ToJsonString(EquipmentCatalog.JsonOptions);
    }

    public static string Describe(EquipmentModel card)
    {
        var series = card.SerialSeries;
        var rtl = card.RtlReport;
        return $"{card.Name}\nID: {card.Id}\nТип на сервере: {card.EffectiveServerDeviceType ?? "не задан"}" +
            $"\nСерийные номера: {(series == null ? "выдаёт сервер / диапазон не задан" : $"{series.FirstSerialNumber}–{series.LastSerialNumber}")}" +
            $"\nКод этикетки: {card.LabelCode?.ToString("D3") ?? "не задан"}" +
            (series == null ? "" : $"\nБаза короткого номера: {series.SerialOffset}\nНомер для ошибок: {series.FailureReportSerialNumber?.ToString() ?? "не задан"}\nПрефикс MAC: {series.MacPrefix ?? "не задан"}\nИмя платы Pro: {series.BoardVersion ?? "не задан"}") +
            (rtl == null ? "" : $"\nТип отчёта RTL: {rtl.DeviceType}; номер {(rtl.AssignSerialOnReport ? "в ответе на отчёт" : "до отправки отчёта")}\nПеременная ID платы: {rtl.DeviceIdentifierVariable}\nID для ошибок: {rtl.FailureDeviceIdentifier ?? "не задан"}") +
            (card.LabelVariants == null ? "" : "\nКомплектации: " + string.Join(", ", card.LabelVariants.OrderBy(p => p.Key).Select(p => $"{p.Key} — {p.Value}"))) +
            (card.LabelCabinets == null ? "" : "\nШкафы: " + string.Join(", ", card.LabelCabinets.OrderBy(p => p.Key).Select(p => $"{p.Key} — {p.Value}")));
    }
}

public sealed class ProfileCatalogModelConverter : JsonConverter<EquipmentModel>
{
    public override EquipmentModel? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<EquipmentModel>(ref reader, EquipmentCatalog.JsonOptions);
    public override void Write(Utf8JsonWriter writer, EquipmentModel value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, EquipmentCatalog.JsonOptions);
}
