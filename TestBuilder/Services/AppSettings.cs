using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestBuilder.Services
{
    /// <summary>
    /// Настройки приложения — сохраняются в testbuilder.settings рядом с exe.
    /// Аналог Properties.Settings из WPF, но для Avalonia.
    /// </summary>
    public class AppSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            AppContext.BaseDirectory, "testbuilder.settings");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        // Singleton
        private static AppSettings? _instance;
        public static AppSettings Instance => _instance ??= Load();

        // ─── Поля настроек ────────────────────────────────────────────────────

        /// <summary>Папка где хранятся JSON-профили графов.</summary>
        [JsonPropertyName("graphsFolder")]
        public string GraphsFolder { get; set; } = string.Empty;

        // ─── Load / Save ──────────────────────────────────────────────────────

        private static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                           ?? new AppSettings();
                }
            }
            catch { }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, JsonOptions);
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }
    }
}