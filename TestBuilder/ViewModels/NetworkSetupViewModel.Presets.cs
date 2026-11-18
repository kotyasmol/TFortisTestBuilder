using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Services;

namespace TestBuilder.ViewModels;

public sealed class NetworkPresetItem
{
    public NetworkConfigurationPreset Definition { get; }
    public bool IsCustom { get; }
    public string Name => Definition.Name;
    public string Description => $"Карт: {Definition.Plan.Addresses.Count}; пар: {Definition.Plan.Addresses.Count(a => a.IncludeInDataTest) / 2}";
    public IRelayCommand LoadCommand { get; }
    internal NetworkPresetItem(NetworkConfigurationPreset definition, bool custom, Action<NetworkPresetItem> load, Func<bool> allowed)
    {
        Definition = definition; IsCustom = custom;
        LoadCommand = new RelayCommand(() => load(this), allowed);
    }
}

public partial class NetworkSetupViewModel
{
    private Action _saveNetworkSettings = () => AppSettings.Instance.SaveChecked();
    private Func<IReadOnlyList<NetworkConfigurationPreset>> _loadBundledPresets = NetworkPresetCatalog.LoadBundled;
    public ObservableCollection<NetworkPresetItem> Presets { get; } = new();
    [ObservableProperty] private string presetStatus = "";
    [ObservableProperty] private NetworkPresetItem? loadedPreset;
    public bool CanDeletePreset => CanEdit && LoadedPreset?.IsCustom == true;
    partial void OnLoadedPresetChanged(NetworkPresetItem? value) => DeletePresetCommand.NotifyCanExecuteChanged();

    private void ReloadPresets()
    {
        Presets.Clear();
        try
        {
            foreach (var definition in _loadBundledPresets())
                Presets.Add(new(definition, false, LoadPreset, () => CanEdit));
        }
        catch (Exception ex) { PresetStatus = "Не удалось загрузить поставляемые наборы: " + ex.Message; }
        foreach (var definition in AppSettings.Instance.NetworkPresets ?? new())
        {
            try { definition.Validate(); Presets.Add(new(definition, true, LoadPreset, () => CanEdit)); }
            catch (Exception ex) { PresetStatus = "Ошибка сохранённого набора: " + ex.Message; }
        }
    }

    internal void LoadPreset(NetworkPresetItem item)
    {
        if (!CanEdit) return;
        var settings = AppSettings.Instance;
        var previous = settings.NetworkPlan;
        try
        {
            item.Definition.Validate();
            var plan = item.Definition.CopyPlan();
            var choices = Rows.ToDictionary(r => r.Ip, r => r.SelectedAdapter?.Id);
            string? Choice(string ip)
            {
                if (choices.TryGetValue(ip, out var selected)) return selected;
                if (settings.DataTestAdapterIds.TryGetValue(ip, out var saved)) return saved;
                var owners = Adapters.Where(a => a.Ipv4.Split(", ").Contains(ip)).Take(2).ToArray();
                return owners.Length == 1 ? owners[0].Id : null;
            }
            settings.NetworkPlan = plan;
            try { _saveNetworkSettings(); }
            catch { settings.NetworkPlan = previous; throw; }
            RebuildRows(plan.Addresses.ToDictionary(a => a.Ip, a => Choice(a.Ip)));
            LoadedPreset = item;
            PresetStatus = $"Загружен «{item.Name}». Проверьте выбранные карты; IP Windows меняются кнопкой «Применить адреса».";
            ProbeResults.Clear(); ProbeStatus = "План изменён. Повторите проверку связи.";
        }
        catch (Exception ex) { PresetStatus = "Не удалось загрузить набор: " + ex.Message; }
    }

    private void RebuildRows(Dictionary<string, string?>? choices = null)
    {
        foreach (var row in Rows) row.SelectionChanged -= SelectionChanged;
        Rows.Clear();
        foreach (var (ip, index) in DataTestNetworkConfigurator.BenchIps.Select((ip, index) => (ip, index)))
        {
            var row = CreateRow(ip, index); row.SelectionChanged += SelectionChanged; Rows.Add(row);
        }
        _draft = choices; Refresh();
    }

    private Window? Owner => (Avalonia.Application.Current?.ApplicationLifetime as
        Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SavePreset()
    {
        if (!CanEdit || Owner is not { } owner) return;
        var name = await new Views.NetworkPresetNameWindow().ShowDialog<string?>(owner);
        if (name == null || !CanEdit) return;
        try
        {
            var preset = new NetworkConfigurationPreset(Guid.NewGuid().ToString("N"), name,
                TestViewModel.CopyDefinition(AppSettings.Instance.NetworkPlan));
            AddCustomPresets(new[] { preset });
            PresetStatus = $"Набор «{name}» сохранён на этом компьютере.";
        }
        catch (Exception ex) { PresetStatus = ex.Message; }
    }

    internal void AddCustomPresets(IEnumerable<NetworkConfigurationPreset> definitions)
    {
        if (!CanEdit) return;
        var settings = AppSettings.Instance;
        var previous = settings.NetworkPresets ?? new();
        var list = previous.ToList();
        var names = Presets.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var source in definitions)
        {
            source.Validate();
            var name = source.Name; var suffix = 2;
            while (names.Contains(name)) name = source.Name[..Math.Min(source.Name.Length, 90)] + $" ({suffix++})";
            names.Add(name);
            list.Add(new(Guid.NewGuid().ToString("N"), name, source.CopyPlan()));
        }
        settings.NetworkPresets = list;
        try { _saveNetworkSettings(); }
        catch { settings.NetworkPresets = previous; throw; }
        ReloadPresets();
    }

    [RelayCommand(CanExecute = nameof(CanDeletePreset))]
    private void DeletePreset()
    {
        if (!CanDeletePreset || LoadedPreset is not { } item) return;
        var settings = AppSettings.Instance; var previous = settings.NetworkPresets ?? new();
        settings.NetworkPresets = previous.Where(p => p.Id != item.Definition.Id).ToList();
        try
        {
            _saveNetworkSettings(); LoadedPreset = null; ReloadPresets();
            PresetStatus = "Набор удалён. Текущий план сети сохранён.";
        }
        catch (Exception ex) { settings.NetworkPresets = previous; PresetStatus = ex.Message; }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task ExportNetworkPlan()
    {
        if (!CanEdit || Owner is not { } owner) return;
        try
        {
            var name = LoadedPreset?.Name ?? "План сети";
            var preset = new NetworkConfigurationPreset(Guid.NewGuid().ToString("N"), name,
                TestViewModel.CopyDefinition(AppSettings.Instance.NetworkPlan));
            preset.Validate();
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Экспорт плана сети", DefaultExtension = "json", SuggestedFileName = "План сети.json",
                ShowOverwritePrompt = true,
                FileTypeChoices = new[] { new FilePickerFileType("Наборы сети JSON") { Patterns = new[] { "*.json" } } }
            });
            if (file == null || !CanEdit) return;
            await using var stream = await file.OpenWriteAsync(); if (stream.CanSeek) stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(new NetworkPresetCatalog { Presets = new() { preset } }.ToJson());
            PresetStatus = "План экспортирован. Привязки к физическим картам не переносятся.";
        }
        catch (Exception ex) { PresetStatus = ex.Message; }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task ImportNetworkPresets()
    {
        if (!CanEdit || Owner is not { } owner) return;
        try
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Импорт наборов сети", AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Наборы сети JSON") { Patterns = new[] { "*.json" } } }
            });
            if (files.Count == 0 || !CanEdit) return;
            await using var stream = await files[0].OpenReadAsync();
            if (stream.CanSeek && stream.Length > 1024 * 1024) throw new InvalidDataException("Файл наборов сети слишком большой.");
            using var reader = new StreamReader(stream);
            var catalog = NetworkPresetCatalog.Read(await reader.ReadToEndAsync());
            if (!CanEdit) return;
            AddCustomPresets(catalog.Presets);
            PresetStatus = $"Импортировано наборов: {catalog.Presets.Count}. Выберите набор для загрузки плана.";
        }
        catch (Exception ex) { PresetStatus = ex.Message; }
    }
}
