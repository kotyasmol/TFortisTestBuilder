using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Domain.Execution;
using TestBuilder.Services;

namespace TestBuilder.ViewModels;

public partial class TestViewModel
{
    private string? _savedProfileSnapshot;
    private string? _savedProfileRevision;
    private bool _confirmingProfileDiscard;
    public bool CanReloadProfile => CanChooseProfile && !IsActionGraphEditor && !IsNetworkSetupBusy &&
        !IsLabelToolBusy && !SaveGraphCommand.IsRunning && _currentProfilePath != null;

    internal Func<Task<bool>> ConfirmReloadProfileAsync { get; set; } = () => ShowProfileConfirmationAsync(() => new());
    internal Func<Task<bool>> ConfirmDiscardProfileAsync { get; set; } = () => ShowProfileConfirmationAsync(() => new(
        "Закрыть профиль?", "Закрыть без сохранения",
        "В профиле есть несохранённые изменения. Закрыть его без сохранения?"));
    internal Func<Func<Views.ReloadProfileDialog>, Task<bool>> ShowDeleteProfileDialogAsync { get; set; } = ShowProfileConfirmationAsync;

    private static async Task<bool> ShowProfileConfirmationAsync(Func<Views.ReloadProfileDialog> createDialog)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is { } owner)
            return await createDialog().ShowDialog<bool>(owner);
        return false;
    }

    private bool HasUnsavedProfileChanges()
    {
        if (_savedProfileSnapshot == null) return _history.Manager.CanUndo;
        return !MatchesCurrentProfile(_savedProfileSnapshot);
    }

    private bool MatchesCurrentProfile(string snapshot)
    {
        try { return snapshot == GraphSerializer.Serialize(this, CurrentProfileName); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        { return false; }
    }

    private async Task<bool> CanDiscardProfileChangesAsync(Func<Task<bool>> confirm)
    {
        if (_confirmingProfileDiscard) return false;
        if (!HasUnsavedProfileChanges()) return true;
        _confirmingProfileDiscard = true;
        try { return await confirm(); }
        catch (InvalidOperationException error)
        {
            LogUiException("Подтверждение закрытия профиля", error);
            return false;
        }
        finally { _confirmingProfileDiscard = false; }
    }

    [RelayCommand(CanExecute = nameof(CanReloadProfile))]
    private async Task ReloadProfileAsync()
    {
        if (!CanReloadProfile) return;
        var path = _currentProfilePath!;
        try
        {
            if (!await CanDiscardProfileChangesAsync(ConfirmReloadProfileAsync)) return;
            if (!CanReloadProfile || _currentProfilePath != path) return;
            await SelectProfileAsync(GraphSerializer.ReadProfile(path), reload: true);
            RefreshProfiles();
            if (_currentProfilePath == path) SelectProfileByPath(path);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не удалось обновить профиль: {ex.Message}";
            LogUiException("Обновление профиля", ex, TestErrorCode.ProfileLoad);
        }
    }

    private bool CanManageProfile(GraphProfile? profile) => profile != null && CanEditGraph && CanChooseProfile &&
        !IsActionGraphEditor && !IsTraining && !IsNetworkSetupBusy && !IsLabelToolBusy && !SaveGraphCommand.IsRunning;

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

    private bool IsOpenDeleteTarget(GraphProfile profile) => IsCurrentProfile(profile) ||
        string.Equals(_selectedProfile?.FilePath, profile.FilePath, StringComparison.OrdinalIgnoreCase);

    private bool CanDeleteProfile(GraphProfile? profile) => profile != null && CanManageProfile(profile) &&
        !_confirmingProfileDiscard && _profileCatalog.Contains(profile) &&
        (!IsConnected || !IsOpenDeleteTarget(profile)) &&
        Path.GetRelativePath(ProfilesFolder, profile.FilePath) == Path.GetFileName(profile.FilePath);

    [RelayCommand(CanExecute = nameof(CanDeleteProfile))]
    private async Task DeleteProfile(GraphProfile? profile)
    {
        if (profile == null || !CanDeleteProfile(profile)) return;
        var isOpen = IsOpenDeleteTarget(profile);
        var unsaved = isOpen && HasUnsavedProfileChanges();
        try
        {
            var revision = (IsCurrentProfile(profile) ? _savedProfileRevision : null) ??
                ProfileFileStorage.Read(profile.FilePath).Revision;
            var question = $"Удалить профиль «{profile.DisplayName}»? Файл будет перемещён в папку deleted.";
            if (unsaved) question += " Несохранённые изменения будут потеряны.";
            if (!await ShowDeleteProfileDialogAsync(() => new("Удалить профиль?", "Удалить", question)) ||
                !CanDeleteProfile(profile) || isOpen != IsOpenDeleteTarget(profile) ||
                (isOpen && !unsaved && HasUnsavedProfileChanges())) return;
            ProfileFileStorage.MoveToDeleted(profile.FilePath, revision);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не удалось удалить профиль: {ex.Message}";
            LogUiException("Удаление профиля", ex);
            return;
        }

        if (isOpen) ResetToEmptyProfile();
        RefreshProfiles();
        StatusMessage = $"Профиль «{profile.DisplayName}» перемещён в папку deleted.";
    }

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
                (copyCurrent ? MatchesCurrentProfile(json) : !HasUnsavedProfileChanges());
            if (copy != null && canOpen) await SelectProfileAsync(copy, discardChangesConfirmed: copyCurrent);
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
            var target = ProfileFileStorage.DisplayPath(Path.GetDirectoryName(profile.FilePath)!, GraphProfile.ComposeName(profile.DeviceModel, description));
            var moving = !string.Equals(profile.FilePath, target, StringComparison.Ordinal);
            if (!moving && description.Equals(profile.ConfigurationName, StringComparison.Ordinal)) return;
            var (json, revision) = ProfileFileStorage.Read(profile.FilePath);
            if (IsCurrentProfile(profile) && _savedProfileRevision != null && revision != _savedProfileRevision)
                ProfileFileStorage.CheckRevision(profile.FilePath, _savedProfileRevision);
            var updated = ProfileFileStorage.WithName(json, profile, description);
            var newRevision = moving ? await ProfileFileStorage.RenameAsync(profile.FilePath, target, updated, revision)
                : await ProfileFileStorage.WriteAsync(profile.FilePath, updated, createNew: false, expectedRevision: revision);
            if (moving) _reportVariableHistory.Remember(target, _reportVariableHistory.Read(profile.FilePath));
            var renamed = GraphSerializer.ReadProfile(moving ? target : profile.FilePath);
            var isCurrent = IsCurrentProfile(profile);
            if (isCurrent)
            {
                _currentProfilePath = renamed.FilePath;
                SetProfileIdentity(renamed.Name, renamed.ConfigurationName);
                _savedProfileName = renamed.Name;
                RootGraph.UpdatedAt = renamed.UpdatedAt;
                _savedProfileRevision = newRevision;
                if (_savedProfileSnapshot != null)
                    _savedProfileSnapshot = GraphSerializer.UpdateSnapshotIdentity(_savedProfileSnapshot, renamed);
            }
            RevealProfileFile(renamed.FilePath);
            if (isCurrent) SelectProfileByPath(renamed.FilePath);
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
