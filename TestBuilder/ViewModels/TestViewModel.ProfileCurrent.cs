using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Services;

namespace TestBuilder.ViewModels;

public partial class TestViewModel
{
    private readonly ProfileCurrentStore _profileCurrentStore;
    private HashSet<string> _currentProfileMarks = new(ProfileCurrentStore.PathComparer);

    private bool CanToggleProfileCurrent(GraphProfile? profile) => profile != null && !IsTraining && !IsActionGraphEditor;

    [RelayCommand(CanExecute = nameof(CanToggleProfileCurrent))]
    private async Task ToggleProfileCurrent(GraphProfile? profile)
    {
        if (!CanToggleProfileCurrent(profile)) return;
        try
        {
            var marked = !profile!.IsMarkedCurrent;
            _currentProfileMarks = await _profileCurrentStore.SetAsync(profile.FilePath, marked);
            ApplyCurrentProfileMarks();
            RefreshStationProfiles();
            RefreshEngineerProfileGroups();
            StatusMessage = marked ? $"Профиль отмечен как актуальный: {profile.DisplayName}" :
                $"Отметка «Актуальный» снята: {profile.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusMessage = "Не удалось сохранить отметку профиля: " + ex.Message;
            LogUiException("Отметка актуальности профиля", ex);
        }
    }

    private void ReadCurrentProfileMarks()
    {
        if (IsTraining) return;
        try { _currentProfileMarks = _profileCurrentStore.Read(); }
        catch (Exception ex) { SetCatalogStatus("Не удалось прочитать локальные отметки профилей: " + ex.Message); }
    }

    private void ApplyCurrentProfileMarks()
    {
        foreach (var item in _profileCatalog)
            item.IsMarkedCurrent = _currentProfileMarks.Contains(ProfileCurrentStore.Normalize(item.FilePath));
    }

    private async Task<string> MoveCurrentProfileMarkAsync(string oldPath, string? newPath)
    {
        if (IsTraining) return "";
        try
        {
            _currentProfileMarks = _profileCurrentStore.Read();
            if (!_currentProfileMarks.Contains(ProfileCurrentStore.Normalize(oldPath))) return "";
            _currentProfileMarks = newPath == null
                ? await _profileCurrentStore.SetAsync(oldPath, false)
                : await _profileCurrentStore.MoveAsync(oldPath, newPath);
            return "";
        }
        catch (Exception ex)
        {
            LogUiException("Перенос локальной отметки профиля", ex);
            return " Локальная отметка актуальности не обновлена: " + ex.Message;
        }
    }
}
