using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Services;

namespace TestBuilder.ViewModels;

public partial class ComputerNetworkAdapterViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Mac), nameof(Link), nameof(Ipv4), nameof(Description), nameof(Kind))]
    private BenchAdapter adapter;
    [ObservableProperty] private string assignment = "Тестовый порт не назначен";
    public string Name => Adapter.Name;
    public string Mac => Adapter.Mac.Length == 12 && Adapter.Mac.All(Uri.IsHexDigit)
        ? string.Join(":", Enumerable.Range(0, 6).Select(i => Adapter.Mac.Substring(i * 2, 2))) : Adapter.Mac;
    public string Link => Adapter.LinkDisplay;
    public string Ipv4 => string.IsNullOrEmpty(Adapter.Ipv4) ? "Нет IPv4" : Adapter.Ipv4;
    public string Description => Adapter.Description;
    public string Kind => Adapter.Kind;
    public ComputerNetworkAdapterViewModel(BenchAdapter adapter) { this.adapter = adapter; }
}

public partial class NetworkSetupViewModel
{
    public ObservableCollection<ComputerNetworkAdapterViewModel> ComputerAdapters { get; } = new();
    public ObservableCollection<ComputerNetworkAdapterViewModel> VisibleComputerAdapters { get; } = new();
    [ObservableProperty] private string adapterSearch = "";
    [ObservableProperty] private int adapterFilterIndex;
    public string[] AdapterFilters { get; } = { "Все карты", "Подключённые", "Без линка", "Не используются в плане" };
    public string ComputerSummary => $"Найдено сетевых карт: {ComputerAdapters.Count}";
    public string VisibleComputerSummary => $"Показано карт: {VisibleComputerAdapters.Count} из {ComputerAdapters.Count}";
    public bool HasNoComputerAdapters => ComputerAdapters.Count == 0;
    partial void OnAdapterSearchChanged(string value) => FilterComputerAdapters();
    partial void OnAdapterFilterIndexChanged(int value) => FilterComputerAdapters();

    private void RefreshComputerAdapters()
    {
        var existing = ComputerAdapters.ToDictionary(c => c.Adapter.Id, StringComparer.OrdinalIgnoreCase);
        var desired = Adapters.Select(card =>
        {
            var row = existing.GetValueOrDefault(card.Id) ?? new ComputerNetworkAdapterViewModel(card);
            row.Adapter = card;
            var assignments = Rows.Where(r => r.SelectedAdapter?.Id == card.Id).Select(r => $"{r.Port} · {r.Ip}").ToArray();
            row.Assignment = assignments.Length == 0 ? "Тестовый порт не назначен" : string.Join(Environment.NewLine, assignments);
            return row;
        }).ToArray();
        Synchronize(ComputerAdapters, desired);
        FilterComputerAdapters();
        OnPropertyChanged(nameof(ComputerSummary)); OnPropertyChanged(nameof(HasNoComputerAdapters));
    }

    private void FilterComputerAdapters()
    {
        var query = AdapterSearch.Trim();
        var desired = ComputerAdapters.Where(row =>
            (AdapterFilterIndex != 1 || row.Adapter.Status.Equals("Up", StringComparison.OrdinalIgnoreCase)) &&
            (AdapterFilterIndex != 2 || !row.Adapter.Status.Equals("Up", StringComparison.OrdinalIgnoreCase)) &&
            (AdapterFilterIndex != 3 || !Rows.Any(r => r.SelectedAdapter?.Id == row.Adapter.Id)) &&
            (query.Length == 0 || string.Join(" ", row.Name, row.Mac, row.Adapter.Mac, row.Ipv4, row.Description, row.Assignment, row.Kind)
                .Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        Synchronize(VisibleComputerAdapters, desired);
        OnPropertyChanged(nameof(VisibleComputerSummary));
    }

    private static void Synchronize<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
    {
        foreach (var item in target.Where(c => !desired.Contains(c)).ToArray()) target.Remove(item);
        for (var i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && Equals(target[i], desired[i])) continue;
            var index = target.IndexOf(desired[i]);
            if (index >= 0) target.Move(index, i); else target.Insert(i, desired[i]);
        }
    }

    internal NetworkAdapterSettingsViewModel CreateAdapterSettings(string id, NetworkAdapterOperations? operations = null)
    {
        if (!CanEdit) throw new InvalidOperationException("Дождитесь завершения текущей операции.");
        var card = Adapters.SingleOrDefault(c => c.Id == id) ?? throw new InvalidOperationException("Карта больше не найдена. Обновите список.");
        var choices = Rows.ToDictionary(r => r.Ip, r => _draft?.GetValueOrDefault(r.Ip) ?? r.SelectedAdapter?.Id);
        return new NetworkAdapterSettingsViewModel(card, TestViewModel.CopyDefinition(AppSettings.Instance.NetworkPlan), choices,
            () => CanEdit, SaveAdapterPlan, RunAdapterOperation, () => { Refresh(); return Adapters.FirstOrDefault(c => c.Id == id); },
            operations ?? new NetworkAdapterOperations());
    }

    internal void SaveAdapterPlan(string id, BenchNetworkPlan plan, string? assignedIp)
    {
        if (!CanEdit) throw new InvalidOperationException("Дождитесь завершения текущей операции.");
        plan = TestViewModel.CopyDefinition(plan); plan.Validate();
        var card = _getAdapters().SingleOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("Карта больше не найдена.");
        if (assignedIp != null && !card.CanUseForDataTest) throw new InvalidOperationException("Для плана DataTest нужен Ethernet-адаптер.");
        if (assignedIp != null && !plan.Addresses.Any(a => a.Ip == assignedIp)) throw new InvalidOperationException("Адрес назначения отсутствует в плане.");
        var choices = ChoicesForPlan(plan);
        var oldIp = choices.FirstOrDefault(p => string.Equals(p.Value, id, StringComparison.OrdinalIgnoreCase)).Key;
        var occupied = assignedIp == null ? null : choices.GetValueOrDefault(assignedIp);
        foreach (var key in choices.Keys.ToArray()) if (string.Equals(choices[key], id, StringComparison.OrdinalIgnoreCase)) choices[key] = null;
        if (assignedIp != null)
        {
            if (occupied != null && !occupied.Equals(id, StringComparison.OrdinalIgnoreCase) && oldIp != null && oldIp != assignedIp && choices.ContainsKey(oldIp)) choices[oldIp] = occupied;
            choices[assignedIp] = id;
        }
        var settings = AppSettings.Instance;
        var beforePlan = settings.NetworkPlan; var beforeBindings = settings.DataTestAdapterIds;
        var beforeApplied = settings.AppliedDataTestAdapterIds;
        settings.AppliedDataTestAdapterIds ??= beforeBindings;
        settings.NetworkPlan = plan;
        settings.DataTestAdapterIds = choices.Where(p => p.Value != null).ToDictionary(p => p.Key, p => p.Value!);
        try { _saveNetworkSettings(); }
        catch { settings.NetworkPlan = beforePlan; settings.DataTestAdapterIds = beforeBindings; settings.AppliedDataTestAdapterIds = beforeApplied; throw; }
        RebuildRows(choices); LoadedPreset = null; InvalidateProbeResults();
        Status = "План и назначение карты сохранены. Настройки IPv4 компьютера не менялись.";
    }

    private async Task<string> RunAdapterOperation(Func<Task<string>> operation)
    {
        if (!CanEdit) throw new InvalidOperationException("Дождитесь завершения текущей операции.");
        IsBusy = true;
        try { return await operation(); }
        finally { IsBusy = false; Refresh(); }
    }

    internal static void OpenSystemNetworkSettings()
    {
        ProcessStartInfo start;
        if (OperatingSystem.IsWindows()) { start = new("control.exe") { UseShellExecute = true }; start.ArgumentList.Add("ncpa.cpl"); }
        else if (OperatingSystem.IsMacOS()) { start = new("open"); start.ArgumentList.Add("-a"); start.ArgumentList.Add("System Settings"); start.ArgumentList.Add("x-apple.systempreferences:com.apple.Network-Settings.extension"); }
        else throw new InvalidOperationException("Откройте настройки сети вашей операционной системы.");
        if (Process.Start(start) == null) throw new InvalidOperationException("Не удалось открыть системные настройки сети.");
    }
}
