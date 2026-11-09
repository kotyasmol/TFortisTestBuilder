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
        private string _settingsPath = SettingsPath;
        [JsonIgnore] public string LoadError { get; private set; } = "";
        [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }

        // Singleton
        private static readonly Lazy<AppSettings> Settings = new(() => Load(SettingsPath));
        public static AppSettings Instance => Settings.Value;

        [JsonPropertyName("standPort")] public string StandPort { get; set; } = "";
        [JsonPropertyName("consolePort")] public string ConsolePort { get; set; } = "";
        [JsonPropertyName("graphsFolder")]
        public string GraphsFolder { get; set; } = string.Empty;
        private string? _defaultGraphsFolder;
        internal string ResolvedGraphsFolder => string.IsNullOrWhiteSpace(GraphsFolder)
            ? _defaultGraphsFolder ??= ProfileDirectoryLocator.Resolve()
            : GraphsFolder;

        [JsonPropertyName("serverBaseUrl")]
        public string ServerBaseUrl { get; set; } = DefaultServerBaseUrl;

        [JsonPropertyName("standId")]
        public string StandId { get; set; } = DefaultStandId;

        [JsonPropertyName("defaultPrinterName")]
        public string DefaultPrinterName { get; set; } = string.Empty;

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

        [JsonPropertyName("appliedDataTestAdapterIds")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? AppliedDataTestAdapterIds { get; set; }


        internal static AppSettings Load(string path)
        {
            AppSettings settings;
            try
            {
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
                    ?? throw new JsonException("Пустой файл настроек.");
            }
            catch (FileNotFoundException)
            {
                var defaultsPath = Path.ChangeExtension(path, ".defaults.json");
                try
                {
                    settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(defaultsPath), JsonOptions)
                        ?? new AppSettings();
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    if (ex is not FileNotFoundException) Logging.DiagnosticLog.WriteException("Чтение исходных настроек", ex);
                    settings = new AppSettings();
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                settings = new AppSettings { LoadError = $"Не удалось прочитать настройки. Проверьте файл {path}." };
                Logging.DiagnosticLog.WriteException("Чтение настроек", ex);
            }
            settings._settingsPath = path;
            return ApplyProfileFolderDefault(settings);
        }

        private static AppSettings ApplyProfileFolderDefault(AppSettings settings)
        {
            settings.LabelTemplates ??= LabelTemplateCatalog.Defaults();
            foreach (var template in settings.LabelTemplates) LabelTemplateCatalog.UpgradeBuiltIn(template);
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
            if (LoadError.Length > 0) throw new IOException(LoadError);
            var temporary = _settingsPath + "." + Guid.NewGuid() + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
                File.Move(temporary, _settingsPath, overwrite: true);
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
