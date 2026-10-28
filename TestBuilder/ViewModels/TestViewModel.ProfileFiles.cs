using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Services;

namespace TestBuilder.ViewModels;

public partial class TestViewModel
{
    private bool CanManageProfile(GraphProfile? profile) => profile != null && CanEditGraph && CanChooseProfile &&
        !IsActionGraphEditor && !IsTraining && !IsNetworkSetupBusy && !IsLabelToolBusy;

    internal Func<GraphProfile, bool, string, Func<string, string?>, Task<string?>> PromptProfileNameAsync { get; set; } =
        async (profile, copy, suggested, validate) =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is { } owner)
                return await new Views.ProfileNameDialog(profile, copy, suggested, validate).ShowDialog<string?>(owner);
            return null;
        };

    private bool IsCurrentProfile(GraphProfile profile) =>
        string.Equals(_currentProfilePath, profile.FilePath, StringComparison.OrdinalIgnoreCase);

    private string? ValidateProfileDescription(GraphProfile source, string name, bool copy)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Введите название профиля.";
        if (name.Any(char.IsControl)) return "Название не должно содержать переносы строк и управляющие символы.";
        var display = GraphProfile.ComposeName(source.DeviceModel, name);
        return _profileCatalog.Any(p => (copy || !string.Equals(p.FilePath, source.FilePath, StringComparison.OrdinalIgnoreCase)) &&
            p.ProductKind == source.ProductKind && p.DisplayName.Equals(display, StringComparison.OrdinalIgnoreCase))
            ? "Профиль с таким названием уже есть." : null;
    }

    private string SuggestedCopyName(GraphProfile source)
    {
        var stem = DescriptionFromTitle(source.ConfigurationName, source.DeviceModel);
        stem = new string(stem.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        if (stem.Length == 0) stem = "Профиль";
        var name = stem + " — копия";
        for (var index = 2; ValidateProfileDescription(source, name, true) != null; index++) name = $"{stem} — копия ({index})";
        return name;
    }

    [RelayCommand(CanExecute = nameof(CanManageProfile))]
    private async Task CopyProfile(GraphProfile? profile)
    {
        if (!CanManageProfile(profile)) return;
        try
        {
            var source = IsCurrentProfile(profile!) && _profileLoadError.Length == 0
                ? new GraphProfile(profile!.FilePath, CurrentProfileName, RootGraph.DeviceModel, ProfileDescription, RootGraph.DeviceModelId, RootGraph.ProductKind)
                : profile!;
            var description = await PromptProfileNameAsync(source, true, SuggestedCopyName(source), name => ValidateProfileDescription(source, name, true));
            if (description == null || !CanManageProfile(profile)) return;
            description = DescriptionFromTitle(description, source.DeviceModel);
            if (ValidateProfileDescription(source, description, true) is { } error) { StatusMessage = error; return; }
            var currentPath = _currentProfilePath;
            var copyCurrent = IsCurrentProfile(profile!) && _profileLoadError.Length == 0;
            var json = copyCurrent ? GraphSerializer.Serialize(this, CurrentProfileName) : await File.ReadAllTextAsync(profile!.FilePath);
            var copyPath = ProfileFileStorage.NewCopyPath(Path.GetDirectoryName(profile!.FilePath)!, GraphProfile.ComposeName(source.DeviceModel, description));
            await ProfileFileStorage.WriteAsync(copyPath, ProfileFileStorage.WithName(json, source, description), createNew: true);
            RevealProfileFile(copyPath);
            var copy = _profileCatalog.FirstOrDefault(p => p.FilePath == copyPath);
            // Keep another edited document and any connected stand untouched.
            var canOpen = !IsConnected && CanManageProfile(profile) && _currentProfilePath == currentPath &&
                (copyCurrent || !_history.Manager.CanUndo);
            if (copy != null && canOpen) await SelectProfileAsync(copy);
            StatusMessage = canOpen ? $"Создана копия профиля: {copy?.DisplayName ?? description}" :
                $"Создана копия профиля: {copy?.DisplayName ?? description}. Выберите её в списке для редактирования.";
        }
        catch (Exception ex) { StatusMessage = $"Не удалось скопировать профиль: {ex.Message}"; LogUiException("Копирование профиля", ex); }
    }

    [RelayCommand(CanExecute = nameof(CanManageProfile))]
    private async Task RenameProfile(GraphProfile? profile)
    {
        if (!CanManageProfile(profile)) return;
        try
        {
            var description = await PromptProfileNameAsync(profile!, false, DescriptionFromTitle(profile!.ConfigurationName, profile.DeviceModel), name => ValidateProfileDescription(profile, name, false));
            if (description == null || !CanManageProfile(profile)) return;
            description = DescriptionFromTitle(description, profile.DeviceModel);
            if (ValidateProfileDescription(profile, description, false) is { } error) { StatusMessage = error; return; }
            var json = await File.ReadAllTextAsync(profile.FilePath);
            await ProfileFileStorage.WriteAsync(profile.FilePath, ProfileFileStorage.WithName(json, profile, description), createNew: false);
            var renamed = GraphSerializer.ReadProfile(profile.FilePath);
            if (IsCurrentProfile(profile))
            {
                SetProfileIdentity(renamed.Name, renamed.ConfigurationName);
                _savedProfileName = renamed.Name;
                RootGraph.UpdatedAt = renamed.UpdatedAt;
            }
            RevealProfileFile(profile.FilePath);
            if (IsCurrentProfile(profile)) SelectProfileByPath(profile.FilePath);
            StatusMessage = $"Профиль переименован: {renamed.DisplayName}";
        }
        catch (Exception ex) { StatusMessage = $"Не удалось переименовать профиль: {ex.Message}"; LogUiException("Переименование профиля", ex); }
    }

    private void RevealProfileFile(string path)
    {
        _profileSearch = string.Empty;
        OnPropertyChanged(nameof(ProfileSearch));
        RefreshProfiles();
        foreach (var group in EngineerProfileGroups.Where(g => g.Profiles.Any(p => p.FilePath == path))) group.IsExpanded = true;
    }
}
