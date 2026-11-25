using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using TestBuilder.Domain.Steps;

namespace TestBuilder.Services;

internal static class ProfilePortableExport
{
    public static string Create(string json, EquipmentCatalog catalog, GraphProfile? profile = null)
    {
        var root = JsonNode.Parse(ProfileCatalogLink.Attach(json, catalog, profile), new JsonNodeOptions { PropertyNameCaseInsensitive = true })!.AsObject();
        root["name"] ??= profile?.Name ?? "Новый профиль";
        var types = LabelTypeStore.LoadFile(AppSettings.Instance.ResolvedGraphsFolder);
        var product = ProfileCatalogLink.Resolve(catalog, root["catalogModel"]?.Deserialize<EquipmentModel>(EquipmentCatalog.JsonOptions),
            root["deviceModelId"]?.GetValue<string>() ?? profile?.DeviceModelId, root["deviceModel"]?.GetValue<string>() ?? profile?.DeviceModel);
        foreach (var node in Nodes(root))
        {
            var type = node["type"]?.GetValue<string>() ?? node["NodeType"]?.GetValue<string>();
            if (!(type?.Contains("Print Label") == true || type is "PRINT_LABEL" or "Печать этикетки") || node["useLastTestLabel"]?.GetValue<bool>() == true) continue;
            var pro = node["useQtProZplFormat"]?.GetValue<bool>() == true;
            var model = node["labelModel"];
            var id = !pro ? "SerialOnlyEpl" : model == null ? "PswUpsBox8x2Pro"
                : model.GetValueKind() == JsonValueKind.Number ? ((DeviceLabelModel)model.GetValue<int>()).ToString() : model.GetValue<string>();
            var template = node["labelTemplate"]?.Deserialize<LabelTemplateDefinition>(EquipmentCatalog.JsonOptions)
                ?? LabelTemplateCatalog.Legacy(id).CopyUpgraded();
            if (types.Types.Count > 0 || types.Assignments.Count > 0) template = LabelTypeStore.Resolve(template, product, product?.Name, file: types);
            node["labelTemplate"] = JsonSerializer.SerializeToNode(template, EquipmentCatalog.JsonOptions);
            if (template.DataBindings != null) node["type"] = "Print Label (bindings v1)";
            else if (template.Layout != null) node["type"] = "Print Label (layout v1)";
            else if (template.SerialTextFormat != null || template.BarcodeFormat != null) node["type"] = "Print Label (data v1)";
        }
        var result = root.ToJsonString(EquipmentCatalog.JsonOptions);
        GraphSerializer.ValidateProfile(result);
        return result;
    }

    private static IEnumerable<JsonObject> Nodes(JsonObject graph)
    {
        if (graph["nodes"] is JsonArray nodes)
            foreach (var entry in nodes)
                if (entry is JsonObject node)
                {
                    yield return node;
                    foreach (var key in new[] { "body", "bodyGraph" })
                        if (node[key] is JsonObject body)
                            foreach (var child in Nodes(body)) yield return child;
                }
        if (graph["quickActions"] is JsonArray actions)
            foreach (var entry in actions)
                if (entry is JsonObject action)
                    foreach (var key in new[] { "graph", "offGraph" })
                        if (action[key] is JsonObject body)
                            foreach (var child in Nodes(body)) yield return child;
    }
}
