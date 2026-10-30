using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace TestBuilder.Models;

/// <summary>Presentation only. Empty labels never change execution or feedback availability.</summary>
public sealed class ProfileActionLabels
{
    [JsonPropertyName("showStatus")] public bool ShowStatus { get; set; } = true;
    [JsonPropertyName("ready")] public string Ready { get; set; } = "Готово";
    [JsonPropertyName("unknown")] public string Unknown { get; set; } = "Неизвестно";
    [JsonPropertyName("on")] public string On { get; set; } = "Включено";
    [JsonPropertyName("off")] public string Off { get; set; } = "Выключено";
    [JsonPropertyName("running")] public string Running { get; set; } = "{step} · {done}/{total}";
    [JsonPropertyName("success")] public string Success { get; set; } = "Выполнено";
    [JsonPropertyName("error")] public string Error { get; set; } = "Ошибка";
    [JsonPropertyName("cancelled")] public string Cancelled { get; set; } = "Остановлено";
    public ProfileActionLabels Copy() => (ProfileActionLabels)MemberwiseClone();
    public void Validate()
    {
        if (new[] { Ready, Unknown, On, Off, Running, Success, Error, Cancelled }
            .Any(t => t == null || t.Length > 120 || t.Any(char.IsControl)))
            throw new InvalidOperationException("Подписи кнопки: до 120 символов без переносов строк. Пустая подпись разрешена.");
    }
}
