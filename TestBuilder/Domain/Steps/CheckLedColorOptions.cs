using System.Text.Json.Serialization;
namespace TestBuilder.Domain.Steps;
public sealed record CheckLedColorOptions
{
    [JsonPropertyName("slaveId")] public byte SlaveId { get; init; } = 1;
    [JsonPropertyName("useCurrentSlaveId")] public bool UseCurrentSlaveId { get; init; } = false;
    [JsonPropertyName("redAddress")] public ushort RedAddress { get; init; } = 2437;
    [JsonPropertyName("greenAddress")] public ushort GreenAddress { get; init; } = 2453;
    [JsonPropertyName("blueAddress")] public ushort BlueAddress { get; init; } = 2469;
    [JsonPropertyName("whiteAddress")] public ushort WhiteAddress { get; init; } = 2421;
    [JsonPropertyName("expectedColor")] public string ExpectedColor { get; init; } = "green";
    [JsonPropertyName("colorMargin")] public int ColorMargin { get; init; } = 20;
    [JsonPropertyName("minIntensity")] public int MinIntensity { get; init; } = 50;
    [JsonPropertyName("outputPrefix")] public string OutputPrefix { get; init; } = "Led";
}
