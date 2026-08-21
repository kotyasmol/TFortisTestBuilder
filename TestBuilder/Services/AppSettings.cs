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

        [JsonPropertyName("graphsFolder")]
        public string GraphsFolder { get; set; } = string.Empty;

        [JsonPropertyName("serverBaseUrl")]
        public string ServerBaseUrl { get; set; } = string.Empty;

        [JsonPropertyName("theme")]
        public string Theme { get; set; } = "Light";


        private static AppSettings Load()
        {
            AppSettings settings;

            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                               ?? new AppSettings();
                    return ApplyProfileFolderDefault(settings);
                }
            }
            catch { }

            settings = new AppSettings();
            return ApplyProfileFolderDefault(settings);
        }

        private static AppSettings ApplyProfileFolderDefault(AppSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.GraphsFolder) ||
                !Directory.Exists(settings.GraphsFolder))
            {
                settings.GraphsFolder = ProfileDirectoryLocator.Resolve();
            }

            return settings;
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
