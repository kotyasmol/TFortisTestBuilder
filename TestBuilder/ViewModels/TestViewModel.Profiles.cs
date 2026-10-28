using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TestBuilder.Services;

namespace TestBuilder.ViewModels;

public partial class TestViewModel
{
    private EquipmentCatalog _equipmentCatalog = new();
    public ObservableCollection<string> DeviceNameSuggestions { get; } = new();
    public ObservableCollection<string> ProfileNameSuggestions { get; } = new();

    public string ProfileDescription
    {
        get => DescriptionFromTitle(RootGraph.ConfigurationName ?? CurrentProfileName, DeviceName);
        set
        {
            if (!CanEditGraph) return;
            var description = DescriptionFromTitle(value ?? "", DeviceName);
            if (description == ProfileDescription) return;
            var oldTitle = CurrentProfileName; var oldDescription = RootGraph.ConfigurationName;
            var title = ComposeProfileName(DeviceName, description);
            _history.Execute(RootGraph, new ProfileMetadataCommand(
                () => SetProfileIdentity(title, description), () => SetProfileIdentity(oldTitle, oldDescription)));
        }
    }

    private static string DescriptionFromTitle(string title, string? device)
    {
        var text = title.Trim();
        if (text == "Новый профиль" || text.Equals(device, StringComparison.OrdinalIgnoreCase)) return "";
        if (!string.IsNullOrWhiteSpace(device))
            foreach (var separator in new[] { " · ", " — ", " - " })
                if (text.StartsWith(device + separator, StringComparison.OrdinalIgnoreCase)) return text[(device.Length + separator.Length)..].Trim();
        return text;
    }
    private static string ComposeProfileName(string? device, string description) =>
        GraphProfile.ComposeName(device, description);
    private void SetProfileIdentity(string title, string? description)
    {
        RootGraph.ConfigurationName = description;
        SetProfileTitle(title);
    }

    // Free text is stored in the profile; a server/catalog release is not required.
    public string DeviceName
    {
        get => RootGraph.DeviceModel ?? "";
        set
        {
            if (!CanEditGraph) return;
            var name = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (name == RootGraph.DeviceModel) return;
            var known = _equipmentCatalog.Resolve(null, name);
            var saved = name == null ? null : _profileCatalog.FirstOrDefault(p =>
                EquipmentCatalog.Normalize(p.DeviceModel) == EquipmentCatalog.Normalize(name));
            var id = known?.Id ?? saved?.DeviceModelId;
            name = known?.Name ?? saved?.DeviceModel ?? name;
            var oldId = RootGraph.DeviceModelId; var oldName = RootGraph.DeviceModel;
            var oldTitle = CurrentProfileName;
            var oldDescription = RootGraph.ConfigurationName;
            var description = ProfileDescription;
            var newTitle = ComposeProfileName(name, description);
            var oldKind = RootGraph.ProductKind;
            var kind = saved?.ProductKind ?? oldKind;
            void Set(string? modelId, string? modelName, string title, string? detail, string productKind)
            {
                RootGraph.DeviceModelId = modelId; RootGraph.DeviceModel = modelName;
                RootGraph.ProductKind = productKind;
                SetProfileIdentity(title, detail);
                NotifyDeviceNameChanged();
                NotifyProfileMetadataChanged();
            }
            _history.Execute(RootGraph, new ProfileMetadataCommand(
                () => Set(id, name, newTitle, description, kind), () => Set(oldId, oldName, oldTitle, oldDescription, oldKind)));
        }
    }

    internal void NotifyDeviceNameChanged()
    {
        OnPropertyChanged(nameof(CurrentProfileName));
        OnPropertyChanged(nameof(ProfileDescription));
        OnPropertyChanged(nameof(DeviceName));
        OnPropertyChanged(nameof(SelectedEquipmentModel));
        RefreshNameSuggestions();
    }

    private void RefreshNameSuggestions()
    {
        var names = _equipmentCatalog.Models.Select(m => m.Name)
            .Concat(_profileCatalog.Select(p => p.DeviceModel).OfType<string>())
            .Append(DeviceName).Where(n => !string.IsNullOrWhiteSpace(n))
            .GroupBy(EquipmentCatalog.Normalize).Select(g => g.First())
            .OrderBy(EquipmentCatalog.SortKey, StringComparer.Ordinal).ToArray();
        SynchronizeStationItems(DeviceNameSuggestions, names);
        var titles = _profileCatalog.OrderBy(p => p.DeviceModel != DeviceName)
            .Select(p => DescriptionFromTitle(p.ConfigurationName, p.DeviceModel)).Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        SynchronizeStationItems(ProfileNameSuggestions, titles);
    }

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
            var oldTitle = CurrentProfileName; var oldDescription = RootGraph.ConfigurationName;
            var description = ProfileDescription;
            var name = value.Id.Length == 0 ? null : value.Name;
            void Set(string? id, string? model, string title, string? detail)
            {
                RootGraph.DeviceModelId = id; RootGraph.DeviceModel = model;
                SetProfileIdentity(title, detail); NotifyDeviceNameChanged();
            }
            _history.Execute(RootGraph, new ProfileMetadataCommand(
                () => Set(value.Id.Length == 0 ? null : value.Id, name, ComposeProfileName(name, description), description),
                () => Set(oldId, oldName, oldTitle, oldDescription)));
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
        NotifyDeviceNameChanged();
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
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool isExpanded;
    public EngineerProfileGroup(string name) => Name = name;
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}
