using System.Text.Json.Serialization;
namespace TestBuilder.Domain.Steps;
public sealed record SerialConsoleOptions
{
    [JsonPropertyName("deviceId")] public string DeviceId { get; init; } = "";
    [JsonPropertyName("portName")] public string PortName { get; init; } = "";
    [JsonPropertyName("baudRate")] public int BaudRate { get; init; } = 115200;
    [JsonPropertyName("command")] public string Command { get; init; } = "";
    [JsonPropertyName("responseTerminator")] public string ResponseTerminator { get; init; } = "";
    [JsonPropertyName("quietMs")] public int QuietMs { get; init; } = 500;
    [JsonPropertyName("timeoutMs")] public int TimeoutMs { get; init; } = 10000;
    [JsonPropertyName("outputVariableName")] public string OutputVariableName { get; init; } = "Console.Response";
}
