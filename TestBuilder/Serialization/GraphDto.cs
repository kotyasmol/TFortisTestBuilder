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

        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        // --- Delay ---
        [JsonPropertyName("milliseconds")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Milliseconds { get; set; }

        // --- Label ---
        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Text { get; set; }

        // --- Write Register / Check Register Range ---
        [JsonPropertyName("slaveId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public byte? SlaveId { get; set; }

        [JsonPropertyName("useCurrentSlaveId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? UseCurrentSlaveId { get; set; }

        [JsonPropertyName("address")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ushort? Address { get; set; }

        // --- Write Register ---
        [JsonPropertyName("value")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ushort? Value { get; set; }

        // --- Check Register Range ---
        [JsonPropertyName("min")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Min { get; set; }

        [JsonPropertyName("max")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Max { get; set; }

        // --- HTTP Request ---
        [JsonPropertyName("url")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Url { get; set; }

        [JsonPropertyName("timeoutMs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? TimeoutMs { get; set; }

        [JsonPropertyName("outputVariableName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OutputVariableName { get; set; }

        [JsonPropertyName("requireSuccessStatusCode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? RequireSuccessStatusCode { get; set; }

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

        [JsonPropertyName("body")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public GraphDto? Body { get; set; }
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
