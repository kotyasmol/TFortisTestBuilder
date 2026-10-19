using System.Text.Json.Serialization;
namespace TestBuilder.Domain.Steps;
public sealed record RunProgramOptions
{
    [JsonPropertyName("programPath")] public string ProgramPath { get; init; } = "";
    [JsonPropertyName("arguments")] public string Arguments { get; init; } = "";
    [JsonPropertyName("workingDirectory")] public string WorkingDirectory { get; init; } = "";
    [JsonPropertyName("timeoutMs")] public int TimeoutMs { get; init; } = 60000;
    [JsonPropertyName("expectedExitCode")] public int ExpectedExitCode { get; init; } = 0;
    [JsonPropertyName("outputPrefix")] public string OutputPrefix { get; init; } = "Program";
}
