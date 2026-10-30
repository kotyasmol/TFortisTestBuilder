using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace TestBuilder.Services
{
    /// <summary>
    /// Настройки приложения — сохраняются в testbuilder.settings рядом с exe.
    /// Аналог Properties.Settings из WPF, но для Avalonia.
    /// </summary>
    public class AppSettings
    {
        public const string DefaultServerBaseUrl = "https://iccid.fort-telecom.ru";
        public const string DefaultStandId = "тест";
        public const string DefaultLogFolder = "logs";

        private static readonly string SettingsPath = Path.Combine(
            AppContext.BaseDirectory, "testbuilder.settings");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        // Singleton
        private static AppSettings? _instance;
        public static AppSettings Instance => _instance ??= Load();

        [JsonPropertyName("standPort")] public string StandPort { get; set; } = "";
        [JsonPropertyName("consolePort")] public string ConsolePort { get; set; } = "";
        [JsonPropertyName("graphsFolder")]
        public string GraphsFolder { get; set; } = string.Empty;

        [JsonPropertyName("serverBaseUrl")]
        public string ServerBaseUrl { get; set; } = DefaultServerBaseUrl;

        [JsonPropertyName("standId")]
        public string StandId { get; set; } = DefaultStandId;

        [JsonPropertyName("theme")]
        public string Theme { get; set; } = "Light";

        [JsonPropertyName("engineerMode")]
        public bool EngineerMode { get; set; }

        [JsonPropertyName("workspace")]
        public WorkspacePreferences Workspace { get; set; } = new();

        [JsonPropertyName("tutorialOfferedVersion")]
        public int TutorialOfferedVersion { get; set; }

        [JsonPropertyName("tutorialCompletedVersion")]
        public int TutorialCompletedVersion { get; set; }

        [JsonPropertyName("enableFileLogging")]
        public bool EnableFileLogging { get; set; } = false;

        [JsonPropertyName("logFolder")]
        public string LogFolder { get; set; } = string.Empty;

        public TestBuilder.Domain.Modbus.DiscoverySettings Discovery { get; set; } = new();

        public BenchNetworkPlan NetworkPlan { get; set; } = new();

        public List<LabelTemplateDefinition> LabelTemplates { get; set; } = LabelTemplateCatalog.Defaults();

        // IP -> stable Windows network interface ID for the DataTest bench.
        [JsonPropertyName("dataTestAdapterIds")]
        public Dictionary<string, string> DataTestAdapterIds { get; set; } = new();


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
            foreach (var template in settings.LabelTemplates) LabelTemplateCatalog.UpgradeBuiltIn(template);
            if (string.IsNullOrWhiteSpace(settings.GraphsFolder) ||
                !Directory.Exists(settings.GraphsFolder))
            {
                settings.GraphsFolder = ProfileDirectoryLocator.Resolve();
            }

            if (string.IsNullOrWhiteSpace(settings.ServerBaseUrl))
            {
                settings.ServerBaseUrl = DefaultServerBaseUrl;
            }

            if (string.IsNullOrWhiteSpace(settings.StandId))
            {
                settings.StandId = DefaultStandId;
            }

            if (string.IsNullOrWhiteSpace(settings.LogFolder))
            {
                settings.LogFolder = Path.Combine(AppContext.BaseDirectory, DefaultLogFolder);
            }

            return settings;
        }

        public void SaveChecked()
        {
            var temporary = SettingsPath + "." + Guid.NewGuid() + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
                File.Move(temporary, SettingsPath, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public void Save()
        {
            try { SaveChecked(); }
            catch { }
        }
    }
}
