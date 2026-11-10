using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Services;
using TestBuilder.ViewModels.StepVM;

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
            var oldUseCatalog = RootGraph.UseCatalogSerials;
            var useCatalog = UseCatalogAfterModelChange(known);
            var oldVariant = RootGraph.LabelVariant;
            void Set(string? modelId, string? modelName, string title, string? detail, string productKind, bool useSeries, int variant)
            {
                RootGraph.DeviceModelId = modelId; RootGraph.DeviceModel = modelName;
                RootGraph.ProductKind = productKind;
                RootGraph.UseCatalogSerials = useSeries;
                RootGraph.LabelVariant = variant;
                SetProfileIdentity(title, detail);
                NotifyDeviceNameChanged();
                NotifyProfileMetadataChanged();
            }
            _history.Execute(RootGraph, new ProfileMetadataCommand(
                () => Set(id, name, newTitle, description, kind, useCatalog, 0),
                () => Set(oldId, oldName, oldTitle, oldDescription, oldKind, oldUseCatalog, oldVariant)));
        }
    }

    internal void NotifyDeviceNameChanged()
    {
        OnPropertyChanged(nameof(CurrentProfileName));
        OnPropertyChanged(nameof(ProfileDescription));
        OnPropertyChanged(nameof(DeviceName));
        OnPropertyChanged(nameof(SelectedEquipmentModel));
        NotifyCatalogSerialSettingsChanged();
        RefreshNameSuggestions();
    }

    public bool UseCatalogSerials
    {
        get => RootGraph.UseCatalogSerials;
        set
        {
            var old = RootGraph.UseCatalogSerials;
            if (!CanEditGraph || old == value) return;
            void Set(bool enabled)
            {
                RootGraph.UseCatalogSerials = enabled;
                NotifyCatalogSerialSettingsChanged();
            }
            _history.Execute(RootGraph, new ProfileMetadataCommand(() => Set(value), () => Set(old)));
        }
    }

    public int ProfileLabelVariant
    {
        get => RootGraph.LabelVariant;
        set
        {
            var old = RootGraph.LabelVariant;
            if (!CanEditGraph || value is < 0 or > 9 || old == value) return;
            void Set(int variant)
            {
                RootGraph.LabelVariant = variant;
                NotifyCatalogSerialSettingsChanged();
            }
            _history.Execute(RootGraph, new ProfileMetadataCommand(() => Set(value), () => Set(old)));
        }
    }

    public string CatalogSerialSeriesSummary
    {
        get
        {
            var model = _equipmentCatalog.Resolve(RootGraph.DeviceModelId, RootGraph.DeviceModel);
            if (model?.SerialSeries is { } series)
                return $"{series.ServerDeviceType} · {series.FirstSerialNumber}–{series.LastSerialNumber}";
            return model?.EffectiveServerDeviceType is { Length: > 0 } serverType
                ? $"{serverType} · " + (model.RtlReport?.AssignSerialOnReport == true
                    ? "номер возвращается в ответе на отчёт" : "номер выдаёт сервер")
                : "Для модели не настроен серверный тип в справочнике";
        }
    }

    private void NotifyCatalogSerialSettingsChanged()
    {
        OnPropertyChanged(nameof(UseCatalogSerials));
        OnPropertyChanged(nameof(ProfileLabelVariant));
        OnPropertyChanged(nameof(CatalogSerialSeriesSummary));
    }

    private bool UseCatalogAfterModelChange(EquipmentModel? model) =>
        RootGraph.UseCatalogSerials || !string.IsNullOrWhiteSpace(model?.EffectiveServerDeviceType) ||
        BoardNodes(RootGraph).Any(node => node is GetSerialNumberFromServerNodeViewModel or BuildTestReportNodeViewModel);

    private EquipmentModel? ResolveProductionModel()
    {
        if (!RootGraph.UseCatalogSerials) return null;
        // Read once at the start. All nodes, including failure cleanup, use the same model.
        var model = EquipmentCatalog.Load(ProfilesFolder).Resolve(RootGraph.DeviceModelId, RootGraph.DeviceModel);
        if (model == null || string.IsNullOrWhiteSpace(model.EffectiveServerDeviceType))
            throw new InvalidOperationException("Выберите изделие с настроенным серверным типом в справочнике.");
        if (RootGraph.LabelVariant is < 0 or > 9 || RootGraph.LabelVariant != 0 &&
            model.LabelVariants?.ContainsKey(RootGraph.LabelVariant) != true)
            throw new InvalidOperationException($"Комплектация {RootGraph.LabelVariant} не задана для «{model.Name}» в справочнике.");
        return model;
    }

    internal EquipmentModel ReadCatalogModelForEditing()
    {
        var name = DeviceName.Trim();
        if (name.Length == 0) throw new InvalidOperationException("Сначала укажите изделие в шапке профиля.");
        var catalog = EquipmentCatalog.Load(ProfilesFolder);
        return catalog.Resolve(RootGraph.DeviceModelId, name) ?? catalog.Resolve(null, name)
            ?? new EquipmentModel(Guid.NewGuid().ToString("N"), name);
    }

    internal void SaveCatalogModel(EquipmentModel model)
    {
        if (!CanEditGraph) throw new InvalidOperationException("Остановите проверку перед правкой справочника.");
        EquipmentCatalog.SaveModel(ProfilesFolder, model);
        LoadEquipmentCatalog(ProfilesFolder);
        SelectedEquipmentModel = EquipmentModels.Single(item => item.Id == model.Id);
        UseCatalogSerials = true;
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
            var oldUseCatalog = RootGraph.UseCatalogSerials;
            var useCatalog = UseCatalogAfterModelChange(value);
            var oldVariant = RootGraph.LabelVariant;
            void Set(string? id, string? model, string title, string? detail, bool useSeries, int variant)
            {
                RootGraph.DeviceModelId = id; RootGraph.DeviceModel = model;
                RootGraph.UseCatalogSerials = useSeries;
                RootGraph.LabelVariant = variant;
                SetProfileIdentity(title, detail); NotifyDeviceNameChanged();
            }
            _history.Execute(RootGraph, new ProfileMetadataCommand(
                () => Set(value.Id.Length == 0 ? null : value.Id, name, ComposeProfileName(name, description), description, useCatalog, 0),
                () => Set(oldId, oldName, oldTitle, oldDescription, oldUseCatalog, oldVariant)));
        }
    }
    private void LoadEquipmentCatalog(string folder)
    {
        try
        {
            _equipmentCatalog = EquipmentCatalog.Load(folder);
            SetCatalogStatus(Directory.Exists(folder) ? "" : $"Папка профилей недоступна: {folder}");
        }
        catch (Exception ex) { SetCatalogStatus("Каталог изделий недоступен: " + ex.Message); }
        EquipmentModels.Clear();
        EquipmentModels.Add(new EquipmentModel("", "Без модели"));
        foreach (var model in _equipmentCatalog.Models.OrderBy(m => m.Name)) EquipmentModels.Add(model);
        RefreshSelectedEquipmentModel();
    }

    private void SetCatalogStatus(string status)
    {
        if (CatalogStatus == status) return;
        CatalogStatus = status;
        OnPropertyChanged(nameof(CatalogStatus));
        OnPropertyChanged(nameof(HasCatalogStatus));
    }

    private static readonly object CatalogCacheLock = new();
    private static readonly Dictionary<string, (long Length, DateTime LastWriteUtc, GraphProfile Profile)> CatalogProfileCache = new();

    private string[] ReadProfileCatalogFiles(string folder)
    {
        try { return Directory.GetFiles(folder, "*.json").OrderBy(path => path, StringComparer.Ordinal).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetCatalogStatus("Папка профилей недоступна: " + ex.Message);
            return Array.Empty<string>();
        }
    }

    private GraphProfile ReadCatalogProfile(string path)
    {
        try
        {
            var file = new FileInfo(path);
            var length = file.Length;
            var written = file.LastWriteTimeUtc;
            lock (CatalogCacheLock)
            {
                if (CatalogProfileCache.TryGetValue(path, out var cached) && cached.Length == length && cached.LastWriteUtc == written)
                    return cached.Profile;
                var profile = GraphSerializer.ReadProfile(path, validateContents: true);
                if (CatalogProfileCache.Count >= 128) CatalogProfileCache.Clear();
                CatalogProfileCache[path] = (length, written, profile);
                return profile;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return GraphSerializer.ReadProfile(path, validateContents: true); }
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
        var ordered = Profiles.OrderBy(p => p.DeviceModel == null)
            .ThenBy(p => EquipmentCatalog.SortKey(p.ModelGroup), StringComparer.Ordinal)
            .ThenBy(p => p.ModelGroupKey, StringComparer.Ordinal)
            .ThenByDescending(p => p.IsLatest)
            .ThenBy(p => EquipmentCatalog.SortKey(p.Name), StringComparer.Ordinal);
        foreach (var items in ordered.GroupBy(p => p.ModelGroupKey, StringComparer.OrdinalIgnoreCase))
        {
            if (!_engineerGroups.TryGetValue(items.Key, out var group))
                _engineerGroups.Add(items.Key, group = new EngineerProfileGroup(items.First().ModelGroup, items.Key));
            SynchronizeStationItems(group.Profiles, items.ToArray());
            if (!string.IsNullOrWhiteSpace(ProfileSearch)) group.IsExpanded = true;
            group.RefreshVisibleProfiles();
            groups.Add(group);
        }
        SynchronizeStationItems(EngineerProfileGroups, groups);
    }
}

public partial class EngineerProfileGroup : ViewModelBase
{
    public string Name { get; }
    public string Key { get; }
    public ObservableCollection<GraphProfile> Profiles { get; } = new();
    public IEnumerable<GraphProfile> VisibleProfiles => IsExpanded ? Profiles : Profiles.Where(p => p.IsLatest);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleProfiles))]
    private bool isExpanded;
    public EngineerProfileGroup(string name, string? key = null) { Name = name; Key = key ?? name; }
    internal void RefreshVisibleProfiles() => OnPropertyChanged(nameof(VisibleProfiles));
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}
