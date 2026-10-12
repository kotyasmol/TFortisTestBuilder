using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TestBuilder.Services;

namespace TestBuilder.ViewModels;

public partial class TestViewModel
{
    private EquipmentCatalog _equipmentCatalog = new();
    public ObservableCollection<EquipmentModel> EquipmentModels { get; } = new();
    public ObservableCollection<EngineerProfileGroup> EngineerProfileGroups { get; } = new();
    private readonly Dictionary<string, EngineerProfileGroup> _engineerGroups = new(StringComparer.OrdinalIgnoreCase);
    public string CatalogStatus { get; private set; } = "";
    public bool HasCatalogStatus => !string.IsNullOrWhiteSpace(CatalogStatus);
    public EquipmentModel? SelectedEquipmentModel
    {
        get => EquipmentModels.FirstOrDefault(m => RootGraph.DeviceModelId != null ? m.Id == RootGraph.DeviceModelId : RootGraph.DeviceModel == null ? m.Id.Length == 0 : m.Name == RootGraph.DeviceModel);
        set
        {
            if (!CanEditGraph || value == null || Equals(value, SelectedEquipmentModel)) return;
            var oldId = RootGraph.DeviceModelId; var oldName = RootGraph.DeviceModel;
            void Set(string? id, string? name)
            { RootGraph.DeviceModelId = id; RootGraph.DeviceModel = name; OnPropertyChanged(nameof(SelectedEquipmentModel)); }
            _history.Execute(RootGraph, new ProfileMetadataCommand(() => Set(value.Id.Length == 0 ? null : value.Id, value.Id.Length == 0 ? null : value.Name), () => Set(oldId, oldName)));
        }
    }
    private void LoadEquipmentCatalog(string folder)
    {
        try { _equipmentCatalog = EquipmentCatalog.Load(folder); CatalogStatus = ""; }
        catch (Exception ex) { CatalogStatus = "Каталог изделий недоступен: " + ex.Message; }
        EquipmentModels.Clear();
        EquipmentModels.Add(new EquipmentModel("", "Без модели"));
        foreach (var model in _equipmentCatalog.Models.OrderBy(m => m.Name)) EquipmentModels.Add(model);
        RefreshSelectedEquipmentModel();
        OnPropertyChanged(nameof(CatalogStatus));
        OnPropertyChanged(nameof(HasCatalogStatus));
    }
    private void RefreshSelectedEquipmentModel()
    {
        var known = _equipmentCatalog.Resolve(RootGraph.DeviceModelId, RootGraph.DeviceModel, CurrentProfileName + " " + System.IO.Path.GetFileNameWithoutExtension(_currentProfilePath));
        if (known != null) { RootGraph.DeviceModelId = known.Id; RootGraph.DeviceModel = known.Name; }
        if (SelectedEquipmentModel == null && RootGraph.DeviceModel != null)
            EquipmentModels.Add(new EquipmentModel(RootGraph.DeviceModelId ?? "legacy:" + RootGraph.DeviceModel, RootGraph.DeviceModel));
        OnPropertyChanged(nameof(SelectedEquipmentModel));
    }
    private void RefreshEngineerProfileGroups()
    {
        var groups = new List<EngineerProfileGroup>();
        foreach (var items in Profiles.OrderBy(p => p.DeviceModel == null).ThenBy(p => EquipmentCatalog.SortKey(p.ModelGroup), StringComparer.Ordinal).ThenBy(p => EquipmentCatalog.SortKey(p.Name), StringComparer.Ordinal).GroupBy(p => p.ModelGroupKey))
        {
            if (!_engineerGroups.TryGetValue(items.Key, out var group))
                _engineerGroups.Add(items.Key, group = new EngineerProfileGroup(items.First().ModelGroup));
            SynchronizeStationItems(group.Profiles, items.ToArray());
            if (!string.IsNullOrWhiteSpace(ProfileSearch)) group.IsExpanded = true;
            groups.Add(group);
        }
        SynchronizeStationItems(EngineerProfileGroups, groups);
    }
}

public partial class EngineerProfileGroup : ViewModelBase
{
    public string Name { get; }
    public ObservableCollection<GraphProfile> Profiles { get; } = new();
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool isExpanded = true;
    public EngineerProfileGroup(string name) => Name = name;
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}
