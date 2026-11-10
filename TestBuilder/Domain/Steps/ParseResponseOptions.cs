using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace TestBuilder.Domain.Steps;
public sealed record ParseResponseOptions
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    [JsonPropertyName("sourceVariableName")] public string SourceVariableName { get; init; } = "Console.Response";
    [JsonPropertyName("parseMode")] public string ParseMode { get; init; } = "Json";
    [JsonPropertyName("selector")] public string Selector { get; init; } = "";
    [JsonPropertyName("valueType")] public string ValueType { get; init; } = "String";
    [JsonPropertyName("outputVariableName")] public string OutputVariableName { get; init; } = "Parsed.Value";
}
