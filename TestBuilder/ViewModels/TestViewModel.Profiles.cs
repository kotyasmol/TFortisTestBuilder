using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Services;
using TestBuilder.Services.Graph;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.ViewModels;

public partial class TestViewModel
{
    private EquipmentCatalog _equipmentCatalog = new();
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

    public string DeviceName => RootGraph.DeviceModel ?? "";

    public string EquipmentPlaceholder => DeviceName.Length == 0
        ? "Выберите из справочника" : $"{DeviceName} · вне справочника";

    public bool HasCatalogEquipmentModel => SelectedEquipmentModel != null;

    internal void NotifyDeviceNameChanged()
    {
        OnPropertyChanged(nameof(CurrentProfileName));
        OnPropertyChanged(nameof(ProfileDescription));
        OnPropertyChanged(nameof(DeviceName));
        OnPropertyChanged(nameof(EquipmentPlaceholder));
        OnPropertyChanged(nameof(SelectedEquipmentModel));
        OnPropertyChanged(nameof(HasCatalogEquipmentModel));
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

    public int? SelectedCatalogLabelVariant
    {
        get => ProfileLabelVariant;
        set
        {
            if (value is int code) ProfileLabelVariant = code;
        }
    }

    public IReadOnlyList<CatalogLabelVariant> CatalogLabelVariants
    {
        get
        {
            var variants = _equipmentCatalog.Resolve(RootGraph.DeviceModelId, RootGraph.DeviceModel)?.LabelVariants;
            var names = variants == null ? new Dictionary<int, string>() : new Dictionary<int, string>(variants);
            names.TryAdd(0, "По умолчанию");
            names.TryAdd(ProfileLabelVariant, "Не задана в справочнике");
            return names.OrderBy(pair => pair.Key)
                .Select(pair => new CatalogLabelVariant(pair.Key, $"{pair.Key} · {pair.Value}")).ToArray();
        }
    }

    public bool ShowCatalogLabelVariants => UseCatalogSerials && CatalogLabelVariants.Count > 1;

    public string CatalogSerialSeriesCaption
    {
        get
        {
            var model = SelectedEquipmentModel;
            if (model?.SerialSeries is { } series)
                return $"Серийные номера: {series.FirstSerialNumber}–{series.LastSerialNumber}";
            if (!UseCatalogSerials) return "";
            return string.IsNullOrWhiteSpace(model?.EffectiveServerDeviceType) ? "Серийные номера: не настроены" : "Серийные номера: выдаёт сервер";
        }
    }

    public string CatalogSerialSeriesSummary
    {
        get
        {
            var model = SelectedEquipmentModel;
            if (model?.SerialSeries is { } series)
            {
                var summary = $"Тип на сервере: {series.ServerDeviceType}\nСерийные номера: от {series.FirstSerialNumber} до {series.LastSerialNumber}";
                return UseCatalogSerials ? summary : summary + "\nПрофиль использует настройки нод.";
            }
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
        OnPropertyChanged(nameof(CatalogSerialSeriesCaption));
        OnPropertyChanged(nameof(CatalogSerialSeriesSummary));
        OnPropertyChanged(nameof(CatalogLabelVariants));
        OnPropertyChanged(nameof(SelectedCatalogLabelVariant));
        OnPropertyChanged(nameof(ShowCatalogLabelVariants));
    }

    private bool UseCatalogAfterModelChange(EquipmentModel? model) =>
        RootGraph.UseCatalogSerials || !string.IsNullOrWhiteSpace(model?.EffectiveServerDeviceType) ||
        ProfileModelAdaptation.All(RootGraph).Any(node => node is GetSerialNumberFromServerNodeViewModel or BuildTestReportNodeViewModel
            or PrintLabelNodeViewModel or BuildMacFromSerialNodeViewModel or SetProMacNodeViewModel or SetPswMacNodeViewModel);

    private (EquipmentModel? Production, EquipmentModel? Legacy) ResolveRunSerialModels()
    {
        if (!RootGraph.UseCatalogSerials)
            return (null, _equipmentCatalog.Resolve(RootGraph.DeviceModelId, RootGraph.DeviceModel));
        // Read once at the start. All nodes, including failure cleanup, use the same model.
        var model = EquipmentCatalog.Load(ProfilesFolder).Resolve(RootGraph.DeviceModelId, RootGraph.DeviceModel);
        if (model == null)
            throw new InvalidOperationException("Выберите изделие из справочника над графом.");
        if (string.IsNullOrWhiteSpace(model.EffectiveServerDeviceType))
            throw new InvalidOperationException($"Для «{model.Name}» не заполнен «Тип на сервере». Откройте раздел «Справочник изделий», выберите модель и перейдите в «Редактировать изделие… → Серийные номера».");
        if (RootGraph.LabelVariant is < 0 or > 9 || RootGraph.LabelVariant != 0 &&
            model.LabelVariants?.ContainsKey(RootGraph.LabelVariant) != true)
            throw new InvalidOperationException($"Комплектация {RootGraph.LabelVariant} не задана для «{model.Name}» в справочнике.");
        return (model, null);
    }

    internal EquipmentModel ReadCatalogModelForEditing()
    {
        return EquipmentCatalog.Load(ProfilesFolder).Resolve(RootGraph.DeviceModelId, RootGraph.DeviceModel)
            ?? throw new InvalidOperationException("Выберите изделие из справочника или добавьте новое.");
    }

    internal EquipmentModel NewCatalogModel(bool useProfileName = true) => new(Guid.NewGuid().ToString("N"),
        useProfileName && !HasCatalogEquipmentModel ? DeviceName : "");

    internal void SaveCatalogModel(EquipmentModel model, bool createNew, bool selectForProfile = true, EquipmentModel? original = null)
    {
        if (!CanEditQuickActions) throw new InvalidOperationException("Остановите проверку перед правкой справочника.");
        EquipmentCatalog.SaveModel(ProfilesFolder, model, createNew, original);
        if (!selectForProfile)
        {
            RefreshProfiles();
            return;
        }
        LoadEquipmentCatalog(ProfilesFolder);
        SelectedEquipmentModel = EquipmentModels.Single(item => item.Id == model.Id);
        if (!string.IsNullOrWhiteSpace(model.EffectiveServerDeviceType)) UseCatalogSerials = true;
    }

    private void RefreshNameSuggestions()
    {
        var titles = _profileCatalog.OrderBy(p => p.DeviceModel != DeviceName)
            .Select(p => DescriptionFromTitle(p.ConfigurationName, p.DeviceModel)).Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        SynchronizeStationItems(ProfileNameSuggestions, titles);
    }

    public ObservableCollection<EquipmentModel> EquipmentModels { get; } = new();
    public ObservableCollection<EngineerProfileGroup> EngineerProfileGroups { get; } = new();
    private readonly Dictionary<string, EngineerProfileGroup> _engineerGroups = new(StringComparer.OrdinalIgnoreCase);
    public string CatalogStatus { get; private set; } = "";
    internal string CatalogOverridesPath => EquipmentCatalog.LocalPath(ProfilesFolder);
    internal EquipmentCatalog CurrentEquipmentCatalog => _equipmentCatalog;
    internal void ReloadCatalogForViewing() => LoadEquipmentCatalog(ProfilesFolder);
    public bool HasCatalogStatus => !string.IsNullOrWhiteSpace(CatalogStatus);
    public EquipmentModel? SelectedEquipmentModel
    {
        get => _equipmentCatalog.Resolve(RootGraph.DeviceModelId, RootGraph.DeviceModel);
        set
        {
            if (!CanEditGraph || value == null || !EquipmentModels.Contains(value) ||
                (value.Id == RootGraph.DeviceModelId && value.Name == RootGraph.DeviceModel)) return;
            var oldId = RootGraph.DeviceModelId; var oldName = RootGraph.DeviceModel;
            var sameModel = string.Equals(oldId, value.Id, StringComparison.OrdinalIgnoreCase);
            var oldTitle = CurrentProfileName; var oldDescription = RootGraph.ConfigurationName;
            var description = ProfileDescription;
            var oldKind = RootGraph.ProductKind;
            var kind = value.RtlReport != null ? "Board" : value.SerialSeries != null ? "Switch" :
                _profileCatalog.FirstOrDefault(p => p.DeviceModelId == value.Id)?.ProductKind ?? oldKind;
            var oldUseCatalog = RootGraph.UseCatalogSerials;
            var useCatalog = UseCatalogAfterModelChange(value);
            var oldVariant = RootGraph.LabelVariant;
            var adaptation = sameModel ? null : ProfileModelAdaptation.Create(RootGraph,
                _equipmentCatalog.Resolve(oldId, oldName), value);
            void Set(string? id, string? model, string title, string? detail, string productKind, bool useSeries, int variant)
            {
                RootGraph.DeviceModelId = id; RootGraph.DeviceModel = model;
                RootGraph.ProductKind = productKind;
                RootGraph.UseCatalogSerials = useSeries;
                RootGraph.LabelVariant = variant;
                SetProfileIdentity(title, detail);
                NotifyDeviceNameChanged();
                NotifyProfileMetadataChanged();
            }
            _history.Execute(RootGraph, new ProfileMetadataCommand(() =>
            {
                using var batch = _nodePresentation.Defer();
                Set(value.Id, value.Name, ComposeProfileName(value.Name, description), description, kind, useCatalog, sameModel ? oldVariant : 0);
                adaptation?.Apply();
                _nodePresentation.Refresh();
            }, () =>
            {
                using var batch = _nodePresentation.Defer();
                adaptation?.Undo();
                Set(oldId, oldName, oldTitle, oldDescription, oldKind, oldUseCatalog, oldVariant);
                _nodePresentation.Refresh();
            }));
            if (adaptation != null) StatusMessage = "Данные изделия обновлены из справочника. Проверьте жёлтые ноды перед запуском.";
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
        SynchronizeStationItems(EquipmentModels, _equipmentCatalog.Models
            .OrderBy(m => EquipmentCatalog.SortKey(m.Name), StringComparer.Ordinal).ToArray());
        NotifyDeviceNameChanged();
        EquipmentCatalogChanged?.Invoke();
    }

    internal event Action? EquipmentCatalogChanged;

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

public sealed record CatalogLabelVariant(int Code, string DisplayName);

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
