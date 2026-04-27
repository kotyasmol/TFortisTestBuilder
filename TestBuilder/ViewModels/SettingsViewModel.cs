using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using TestBuilder.Services;

namespace TestBuilder.ViewModels
{
    public partial class SettingsViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string graphsFolder = string.Empty;

        [ObservableProperty]
        private bool isDarkTheme;

        public IAsyncRelayCommand SelectFolderCommand { get; }

        public SettingsViewModel()
        {
            GraphsFolder = AppSettings.Instance.GraphsFolder;
            IsDarkTheme = AppSettings.Instance.Theme == "Dark";

            SelectFolderCommand = new AsyncRelayCommand(SelectFolderAsync);

            // Применяем сохранённую тему при старте
            ApplyTheme(IsDarkTheme);
        }

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
            AppSettings.Instance.GraphsFolder = GraphsFolder;
            AppSettings.Instance.Save();
        }

        partial void OnGraphsFolderChanged(string value)
        {
            AppSettings.Instance.GraphsFolder = value;
            AppSettings.Instance.Save();
        }
    }
}