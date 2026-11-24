using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public sealed class EquipmentCatalogView : UserControl
{
    private TestViewModel? _profile;
    private Action? _refresh;
    private Action? _updateAvailability;
    private bool _attached;

    public EquipmentCatalogView()
    {
        DataContextChanged += (_, _) => BindProfile();
        AttachedToVisualTree += (_, _) => { _attached = true; Subscribe(); _refresh?.Invoke(); };
        DetachedFromVisualTree += (_, _) => { Unsubscribe(); _attached = false; };
    }

    private void BindProfile()
    {
        Unsubscribe();
        _profile = DataContext as TestViewModel;
        _refresh = null;
        _updateAvailability = null;
        Content = null;
        if (_profile != null) Build(_profile);
        Subscribe();
    }

    private void Subscribe()
    {
        if (!_attached || _profile == null) return;
        _profile.EquipmentCatalogChanged -= OnCatalogChanged;
        _profile.EquipmentCatalogChanged += OnCatalogChanged;
        _profile.PropertyChanged -= OnProfileChanged;
        _profile.PropertyChanged += OnProfileChanged;
    }

    private void Unsubscribe()
    {
        if (_profile == null) return;
        _profile.EquipmentCatalogChanged -= OnCatalogChanged;
        _profile.PropertyChanged -= OnProfileChanged;
    }

    private void OnCatalogChanged() => _refresh?.Invoke();
    private void OnProfileChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TestViewModel.CanEditQuickActions)) _updateAvailability?.Invoke();
    }

    private void Build(TestViewModel profile)
    {
        var editing = false;
        var data = new EquipmentCatalogDataView(profile.CatalogOverridesPath, () => profile.CanEditQuickActions && !editing);
        data.ReloadRequested += (_, _) => profile.ReloadCatalogForViewing();
        data.AddRequested += async () => await OpenCardAsync(profile.NewCatalogModel(useProfileName: false), createNew: true);
        data.EditRequested += async model => await OpenCardAsync(model, createNew: false);
        Content = data;
        _updateAvailability = data.RefreshAvailability;
        _refresh = () => data.Refresh(profile.CurrentEquipmentCatalog, profile.CatalogStatus, profile.CatalogOverridesPath);
        _refresh();
        if (profile.SelectedEquipmentModel is { } selected) data.RevealModel(selected.Id);

        async Task OpenCardAsync(EquipmentModel model, bool createNew)
        {
            if (editing || !profile.CanEditQuickActions || TopLevel.GetTopLevel(this) is not Window owner) return;
            editing = true;
            data.RefreshAvailability();
            try
            {
                var saved = await new CatalogSerialsWindow(profile, model, createNew, selectForProfile: false)
                    .ShowDialog<EquipmentModel?>(owner);
                if (saved == null) return;
                data.Refresh(profile.CurrentEquipmentCatalog, profile.CatalogStatus, profile.CatalogOverridesPath);
                data.RevealModel(saved.Id);
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or
                UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
            {
                data.Refresh(profile.CurrentEquipmentCatalog, ex.Message, profile.CatalogOverridesPath);
            }
            finally
            {
                editing = false;
                data.RefreshAvailability();
            }
        }
    }
}
