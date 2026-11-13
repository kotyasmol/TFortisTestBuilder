using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestBuilder.Models;

// Advisory document metadata. It never changes execution or makes a check pass.
public sealed record NodeModelReview(
    [property: JsonPropertyName("modelId")] string ModelId,
    [property: JsonPropertyName("modelName")] string ModelName,
    [property: JsonPropertyName("message")] string Message)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
