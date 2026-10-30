using System;
using System.Net;
using System.Text.Json.Serialization;

namespace TestBuilder.Models;

public sealed class PingMonitorSettings
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("host")] public string Host { get; set; } = "";
    [JsonPropertyName("intervalMs")] public int IntervalMs { get; set; } = 1000;
    [JsonPropertyName("timeoutMs")] public int TimeoutMs { get; set; } = 1000;
    [JsonPropertyName("showInEngineer")] public bool ShowInEngineer { get; set; } = true;
    [JsonPropertyName("showInOperator")] public bool ShowInOperator { get; set; }

    public PingMonitorSettings Copy() => (PingMonitorSettings)MemberwiseClone();

    public void Validate()
    {
        if (Host == null || Host != Host.Trim() || Host.Length > 253 ||
            (Host.Length == 0 ? Enabled : !IPAddress.TryParse(Host, out _) && Uri.CheckHostName(Host) != UriHostNameType.Dns))
            throw new InvalidOperationException("Укажите IP-адрес или имя узла без http://, порта и пути.");
        if (IntervalMs is < 250 or > 60000)
            throw new InvalidOperationException("Период опроса: от 250 до 60000 мс.");
        if (TimeoutMs is < 100 or > 30000)
            throw new InvalidOperationException("Таймаут ответа: от 100 до 30000 мс.");
    }
}
