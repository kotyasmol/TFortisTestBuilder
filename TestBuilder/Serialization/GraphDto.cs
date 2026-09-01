using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TestBuilder.Serialization
{
    public class GraphDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "Новый профиль";

        [JsonPropertyName("nodes")]
        public List<NodeDto> Nodes { get; set; } = new();

        [JsonPropertyName("connections")]
        public List<ConnectionDto> Connections { get; set; } = new();
    }

    public class NodeDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("NodeType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? NodeType { get; set; }

        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("color")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Color { get; set; }

        // --- Delay ---
        [JsonPropertyName("milliseconds")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Milliseconds { get; set; }

        // --- Label ---
        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Text { get; set; }

        [JsonPropertyName("labelWidth")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? LabelWidth { get; set; }

        [JsonPropertyName("labelHeight")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? LabelHeight { get; set; }

        // --- Subtest ---
        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Description { get; set; }

        [JsonPropertyName("isEnabled")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? IsEnabled { get; set; }

        // --- Write Register / Check Register Range ---
        [JsonPropertyName("slaveId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public byte? SlaveId { get; set; }

        [JsonPropertyName("useCurrentSlaveId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? UseCurrentSlaveId { get; set; }

        [JsonPropertyName("liveRead")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? LiveRead { get; set; }

        [JsonPropertyName("address")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ushort? Address { get; set; }

        // --- Write Register ---
        [JsonPropertyName("value")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ushort? Value { get; set; }

        [JsonPropertyName("verifyWrite")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? VerifyWrite { get; set; }

        // --- Check Register Range ---
        [JsonPropertyName("min")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Min { get; set; }

        [JsonPropertyName("max")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Max { get; set; }

        // --- Check Register Equality / Wait Until ---
        [JsonPropertyName("expectedValue")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? ExpectedValue { get; set; }

        // --- Wait Until ---
        [JsonPropertyName("durationMs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? DurationMs { get; set; }

        // --- Poll Register ---
        [JsonPropertyName("sampleCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? SampleCount { get; set; }

        // --- Selftest / HTTP-backed steps ---
        [JsonPropertyName("url")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Url { get; set; }

        [JsonPropertyName("timeoutMs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? TimeoutMs { get; set; }

        [JsonPropertyName("pollIntervalMs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? PollIntervalMs { get; set; }

        [JsonPropertyName("outputVariableName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OutputVariableName { get; set; }

        [JsonPropertyName("validationRules")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ValidationRules { get; set; }

        [JsonPropertyName("baseUrl")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? BaseUrl { get; set; }

        [JsonPropertyName("retryCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? RetryCount { get; set; }

        [JsonPropertyName("retryDelayMs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? RetryDelayMs { get; set; }

        [JsonPropertyName("failOnError")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? FailOnError { get; set; }

        [JsonPropertyName("outputPrefix")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OutputPrefix { get; set; }

        // --- Check Variable Equality / Range ---
        [JsonPropertyName("variableName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? VariableName { get; set; }

        [JsonPropertyName("comparisonType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ComparisonType { get; set; }

        [JsonPropertyName("failMessage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FailMessage { get; set; }

        [JsonPropertyName("inclusive")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Inclusive { get; set; }

        // --- Compare Variables ---
        [JsonPropertyName("leftVariableName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LeftVariableName { get; set; }

        [JsonPropertyName("rightVariableName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RightVariableName { get; set; }

        // --- Build MAC ---
        [JsonPropertyName("serialOffset")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? SerialOffset { get; set; }

        [JsonPropertyName("macPrefix")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MacPrefix { get; set; }

        [JsonPropertyName("serialShortVariableName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SerialShortVariableName { get; set; }

        // --- Wait Variable Until ---
        [JsonPropertyName("pollAction")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PollAction { get; set; }

        [JsonPropertyName("requestTimeoutMs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? RequestTimeoutMs { get; set; }

        [JsonPropertyName("intervalMs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? IntervalMs { get; set; }

        [JsonPropertyName("failOnTimeout")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? FailOnTimeout { get; set; }

        // --- Clear ARP Cache ---
        [JsonPropertyName("runArpdBat")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? RunArpdBat { get; set; }

        [JsonPropertyName("arpdBatPath")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ArpdBatPath { get; set; }

        [JsonPropertyName("command")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Command { get; set; }

        [JsonPropertyName("arguments")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Arguments { get; set; }

        // --- Server / API ---
        [JsonPropertyName("serverBaseUrl")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ServerBaseUrl { get; set; }

        [JsonPropertyName("deviceType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? DeviceType { get; set; }

        [JsonPropertyName("cpuIdVariableName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CpuIdVariableName { get; set; }

        // --- UDP set MAC ---
        [JsonPropertyName("targetIp")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TargetIp { get; set; }

        [JsonPropertyName("targetPort")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? TargetPort { get; set; }

        [JsonPropertyName("macVariableName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MacVariableName { get; set; }

        [JsonPropertyName("repeatCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? RepeatCount { get; set; }

        [JsonPropertyName("delayBetweenRepeatsMs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? DelayBetweenRepeatsMs { get; set; }

        [JsonPropertyName("failOnSendError")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? FailOnSendError { get; set; }

        // --- Data Test ---
        [JsonPropertyName("mode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Mode { get; set; }

        [JsonPropertyName("expectedPackets")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ExpectedPackets { get; set; }

        [JsonPropertyName("packetSizeBytes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? PacketSizeBytes { get; set; }

        [JsonPropertyName("udpPort")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? UdpPort { get; set; }

        [JsonPropertyName("maxPortTestTimeMs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? MaxPortTestTimeMs { get; set; }

        [JsonPropertyName("targetBandwidthMbps")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? TargetBandwidthMbps { get; set; }

        [JsonPropertyName("warmupMs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? WarmupMs { get; set; }

        [JsonPropertyName("interPairDelayMs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? InterPairDelayMs { get; set; }

        [JsonPropertyName("allowedLossPercent")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? AllowedLossPercent { get; set; }

        [JsonPropertyName("allowedTxDeficitPercent")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? AllowedTxDeficitPercent { get; set; }

        [JsonPropertyName("bidirectional")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Bidirectional { get; set; }

        [JsonPropertyName("portsText")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PortsText { get; set; }

        [JsonPropertyName("ports")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<DataTestPortDto>? Ports { get; set; }

        // --- Print Label ---
        [JsonPropertyName("printerName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PrinterName { get; set; }

        [JsonPropertyName("deviceName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? DeviceName { get; set; }

        [JsonPropertyName("serialVariableName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SerialVariableName { get; set; }

        [JsonPropertyName("copies")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Copies { get; set; }

        [JsonPropertyName("includeMac")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? IncludeMac { get; set; }

        [JsonPropertyName("equipmentFieldUse")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? EquipmentFieldUse { get; set; }

        [JsonPropertyName("equipmentType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? EquipmentType { get; set; }

        [JsonPropertyName("equipmentText")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? EquipmentText { get; set; }

        [JsonPropertyName("failOnPrinterError")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? FailOnPrinterError { get; set; }

        // --- Send Report ---
        [JsonPropertyName("reportVariableName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ReportVariableName { get; set; }

        [JsonPropertyName("endpoint")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Endpoint { get; set; }

        [JsonPropertyName("saveLocalCopy")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? SaveLocalCopy { get; set; }

        [JsonPropertyName("localReportsDirectory")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LocalReportsDirectory { get; set; }

        // --- Build Report ---
        [JsonPropertyName("includeAllVariables")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? IncludeAllVariables { get; set; }

        // --- For Slaves ---
        [JsonPropertyName("fromSlaveId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public byte? FromSlaveId { get; set; }

        [JsonPropertyName("toSlaveId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public byte? ToSlaveId { get; set; }

        [JsonPropertyName("step")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public byte? Step { get; set; }

        [JsonPropertyName("stopOnError")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? StopOnError { get; set; }

        [JsonPropertyName("runOnFailure")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? RunOnFailure { get; set; }

        [JsonPropertyName("body")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public GraphDto? Body { get; set; }

        [JsonPropertyName("bodyGraph")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public GraphDto? BodyGraph { get; set; }
    }

    public class DataTestPortDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("inIp")]
        public string InIp { get; set; } = "";

        [JsonPropertyName("outIp")]
        public string OutIp { get; set; } = "";

        [JsonPropertyName("bandwidthMbps")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? BandwidthMbps { get; set; }
    }

    public class ConnectionDto
    {
        [JsonPropertyName("sourceNodeId")]
        public string SourceNodeId { get; set; } = "";

        [JsonPropertyName("sourceConnector")]
        public string SourceConnector { get; set; } = "";

        [JsonPropertyName("targetNodeId")]
        public string TargetNodeId { get; set; } = "";

        [JsonPropertyName("targetConnector")]
        public string TargetConnector { get; set; } = "";
    }
}
