using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
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

        public IAsyncRelayCommand SelectFolderCommand { get; }

        public SettingsViewModel()
        {
            // Загружаем сохранённый путь
            GraphsFolder = AppSettings.Instance.GraphsFolder;

            SelectFolderCommand = new AsyncRelayCommand(SelectFolderAsync);
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

            // Сохраняем в .settings
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