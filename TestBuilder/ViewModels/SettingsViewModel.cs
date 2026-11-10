using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;
using TestBuilder.Services;

namespace TestBuilder.ViewModels
{
    public partial class SettingsViewModel : ViewModelBase
    {
        private readonly Func<bool>? canStartTutorial;
        private bool isEngineerMode;
        [ObservableProperty] private string profileContext = "Коммутатор";
        [ObservableProperty] private bool showConsoleSettings;

        public void SetProfileContext(string kind, bool hasConsole)
        {
            ProfileContext = "Тип открытого профиля: " + kind;
            ShowConsoleSettings = hasConsole;
        }

        public bool CanStartTutorial => canStartTutorial?.Invoke() ?? true;

        public bool IsEngineerMode
        {
            get => isEngineerMode;
            set
            {
                if (!SetProperty(ref isEngineerMode, value)) return;
                AppSettings.Instance.EngineerMode = value;
                AppSettings.Instance.Save();
            }
        }

        [RelayCommand(CanExecute = nameof(CanStartTutorial))]
        private async Task ConfigureDiscovery()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
                await new TestBuilder.Views.DiscoverySettingsWindow().ShowDialog(desktop.MainWindow);
        }

        public void RefreshTutorialAvailability() { OnPropertyChanged(nameof(CanStartTutorial)); ConfigureDiscoveryCommand.NotifyCanExecuteChanged(); }

        [ObservableProperty] private string standPort = "";
        [ObservableProperty] private string consolePort = "";
        partial void OnStandPortChanged(string value) { AppSettings.Instance.StandPort = value.Trim(); AppSettings.Instance.Save(); }
        partial void OnConsolePortChanged(string value) { AppSettings.Instance.ConsolePort = value.Trim(); AppSettings.Instance.Save(); }

        [ObservableProperty]
        private string graphsFolder = string.Empty;

        [ObservableProperty]
        private string serverBaseUrl = string.Empty;

        [ObservableProperty]
        private string standId = string.Empty;

        [ObservableProperty]
        private string defaultPrinterName = string.Empty;

        [ObservableProperty]
        private bool enableFileLogging;

        [ObservableProperty]
        private string logFolder = string.Empty;

        [ObservableProperty]
        private bool isDarkTheme;

        public IAsyncRelayCommand SelectFolderCommand { get; }
        public IAsyncRelayCommand SelectLogFolderCommand { get; }

        public SettingsViewModel(Func<bool>? canStartTutorial = null)
        {
            this.canStartTutorial = canStartTutorial;
            var settings = AppSettings.Instance;
            isEngineerMode = settings.EngineerMode;
            standPort = settings.StandPort;
            consolePort = settings.ConsolePort;
            graphsFolder = settings.ResolvedGraphsFolder;
            serverBaseUrl = settings.ServerBaseUrl;
            standId = settings.StandId;
            defaultPrinterName = settings.DefaultPrinterName ?? string.Empty;
            enableFileLogging = settings.EnableFileLogging;
            logFolder = settings.LogFolder;
            isDarkTheme = settings.Theme == "Dark";

            SelectFolderCommand = new AsyncRelayCommand(SelectFolderAsync);
            SelectLogFolderCommand = new AsyncRelayCommand(SelectLogFolderAsync);

            ApplyTheme(IsDarkTheme);
        }

        public string LoadError => AppSettings.Instance.LoadError;
        public bool HasLoadError => LoadError.Length > 0;

        partial void OnIsDarkThemeChanged(bool value)
        {
            ApplyTheme(value);
            AppSettings.Instance.Theme = value ? "Dark" : "Light";
            AppSettings.Instance.Save();
        }

        private static void ApplyTheme(bool dark)
        {
            if (Avalonia.Application.Current != null)
                Avalonia.Application.Current.RequestedThemeVariant =
                    dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        private async Task SelectFolderAsync()
        {
            var topLevel = Avalonia.Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Выберите папку с профилями графов",
                    AllowMultiple = false
                });

            if (folders.Count == 0) return;

            GraphsFolder = folders[0].Path.LocalPath;
        }

        private async Task SelectLogFolderAsync()
        {
            var topLevel = Avalonia.Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Выберите папку для текстовых логов",
                    AllowMultiple = false
                });

            if (folders.Count == 0) return;

            LogFolder = folders[0].Path.LocalPath;
        }

        partial void OnGraphsFolderChanged(string value)
        {
            AppSettings.Instance.GraphsFolder = value;
            AppSettings.Instance.Save();
        }

        partial void OnServerBaseUrlChanged(string value)
        {
            AppSettings.Instance.ServerBaseUrl = value?.Trim() ?? string.Empty;
            AppSettings.Instance.Save();
        }

        partial void OnStandIdChanged(string value)
        {
            AppSettings.Instance.StandId = value?.Trim() ?? string.Empty;
            AppSettings.Instance.Save();
        }

        partial void OnDefaultPrinterNameChanged(string value)
        {
            AppSettings.Instance.DefaultPrinterName = value?.Trim() ?? string.Empty;
            AppSettings.Instance.Save();
        }

        partial void OnEnableFileLoggingChanged(bool value)
        {
            AppSettings.Instance.EnableFileLogging = value;
            AppSettings.Instance.Save();
        }

        partial void OnLogFolderChanged(string value)
        {
            AppSettings.Instance.LogFolder = value?.Trim() ?? string.Empty;
            AppSettings.Instance.Save();
        }
    }
}
