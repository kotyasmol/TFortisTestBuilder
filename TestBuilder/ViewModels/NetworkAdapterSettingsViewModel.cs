using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Services;

namespace TestBuilder.ViewModels;

public sealed class NetworkPortChoice : ObservableObject
{
    public BenchNetworkAddress? Port { get; }
    public string Title => Port == null ? "Карта не назначена тестовому порту" : $"{(Port.Name.Length == 0 ? "Порт" : Port.Name)} · {Port.Ip}";
    public NetworkPortChoice(BenchNetworkAddress? port)
    {
        Port = port;
        if (port != null) port.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Title));
    }
}

public partial class NetworkAdapterSettingsViewModel : ViewModelBase
{
    private readonly Func<bool> allowed;
    private readonly Action<string, BenchNetworkPlan, string?> savePlan;
    private readonly Func<Func<Task<string>>, Task<string>> operate;
    private readonly Func<BenchAdapter?> refresh;
    private readonly NetworkAdapterOperations operations;
    public BenchNetworkPlan Plan { get; }
    public ObservableCollection<NetworkPortChoice> Ports { get; } = new();
    public NetworkSettingMode[] Modes => NetworkSettingMode.All;
    [ObservableProperty] private BenchAdapter current;
    [ObservableProperty] private string adapterName = "";
    [ObservableProperty] private NetworkSettingMode addressMode = NetworkSettingMode.All[0];
    [ObservableProperty] private NetworkSettingMode dnsMode = NetworkSettingMode.All[0];
    [ObservableProperty] private string address = "";
    [ObservableProperty] private string mask = "255.255.255.0";
    [ObservableProperty] private string gateway = "";
    [ObservableProperty] private string dns = "";
    [ObservableProperty] private string testSpeedText = "100";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private NetworkPortChoice? selectedPort;
    public bool CanEdit => !IsBusy && allowed();
    public bool CanClose => !IsBusy;
    public bool CanChangeSystem => CanEdit && operations.Supported;
    public bool ManualAddress => AddressMode?.Key == "static";
    public bool ManualDns => DnsMode?.Key == "static";
    public bool CanAssignPort => CanEdit && Current.CanUseForDataTest;
    public bool HasPort => SelectedPort?.Port != null && Current.CanUseForDataTest;
    public string PlanSummary => Plan.Addresses.Count == 0
        ? "Тестовых портов пока нет. Создайте порт для этой карты или добавьте группу в разделе «Сеть → Тестовые порты и пары»."
        : $"Настроено тестовых портов: {Plan.Addresses.Count}. Выберите существующий или создайте новый. Количество портов можно увеличивать.";
    public string NativeSupport => operations.Supported ? "Изменение настроек Windows требует запуска от имени администратора." :
        "Изменение IPv4 в приложении доступно в Windows. На этом компьютере используйте системные настройки сети; тестовые порты и пары можно настраивать здесь.";
    public string ConnectionDetails => $"{Current.Kind} · {Current.LinkDisplay}\nMAC: {Current.Mac}\nIPv4: {Empty(Current.Ipv4)}\nМаска: {Empty(Current.Mask)}\nШлюз: {Empty(Current.Gateway)}\nDNS: {Empty(Current.Dns)}\nDHCP: {(Current.DhcpEnabled == true ? "включён" : Current.DhcpEnabled == false ? "выключен" : "нет данных")}\n{Current.Description}\nID: {Current.Id}";
    private static string Empty(string value) => string.IsNullOrWhiteSpace(value) ? "не задан" : value;

    internal NetworkAdapterSettingsViewModel(BenchAdapter card, BenchNetworkPlan plan, IReadOnlyDictionary<string, string?> bindings,
        Func<bool> allowed, Action<string, BenchNetworkPlan, string?> savePlan, Func<Func<Task<string>>, Task<string>> operate,
        Func<BenchAdapter?> refresh, NetworkAdapterOperations operations)
    {
        current = card; Plan = plan; this.allowed = allowed; this.savePlan = savePlan; this.operate = operate; this.refresh = refresh; this.operations = operations;
        Ports.Add(new(null)); foreach (var port in Plan.Addresses) Ports.Add(new(port));
        SelectedPort = Ports.FirstOrDefault(p => p.Port != null && bindings.TryGetValue(p.Port.Ip, out var id) && string.Equals(id, card.Id, StringComparison.OrdinalIgnoreCase)) ?? Ports[0];
        LoadCurrent(card);
    }

    partial void OnSelectedPortChanged(NetworkPortChoice? value)
    {
        TestSpeedText = (value?.Port?.TestMbps ?? 100).ToString(System.Globalization.CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(HasPort));
    }
    partial void OnAddressModeChanged(NetworkSettingMode value) => OnPropertyChanged(nameof(ManualAddress));
    partial void OnDnsModeChanged(NetworkSettingMode value) => OnPropertyChanged(nameof(ManualDns));
    partial void OnIsBusyChanged(bool value)
    {
        foreach (var name in new[] { nameof(CanEdit), nameof(CanChangeSystem), nameof(CanAssignPort), nameof(CanClose) }) OnPropertyChanged(name);
    }

    private void LoadCurrent(BenchAdapter card)
    {
        Current = card; AdapterName = card.Name;
        AddressMode = Modes.Single(m => m.Key == (card.DhcpEnabled == true ? "dhcp" : card.DhcpEnabled == false ? "static" : "keep"));
        Address = card.Ipv4.Split(", ", StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        Mask = card.Mask.Length == 0 ? "255.255.255.0" : card.Mask;
        Gateway = card.Gateway.Split(", ", StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        Dns = card.Dns; DnsMode = Modes[0]; OnPropertyChanged(nameof(ConnectionDetails));
    }

    public void Reload()
    {
        if (!CanEdit) return;
        try { LoadCurrent(refresh() ?? throw new InvalidOperationException("Карта больше не найдена.")); Status = "Данные карты обновлены."; }
        catch (Exception ex) { Status = ex.Message; }
    }

    public void AddPort()
    {
        if (!CanAssignPort) return;
        try
        {
            var port = Plan.CreateNextAddress(); port.Name = Current.Name; port.Role = "Ethernet";
            Plan.Addresses.Add(port); var choice = new NetworkPortChoice(port); Ports.Add(choice); SelectedPort = choice;
            OnPropertyChanged(nameof(PlanSummary));
            Status = "Порт добавлен в черновик. Сохраните назначение карты.";
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    public void RemovePort()
    {
        if (!CanAssignPort || SelectedPort?.Port is not { } port) return;
        var choice = SelectedPort;
        Plan.Addresses.Remove(port); Ports.Remove(choice); SelectedPort = Ports[0];
        OnPropertyChanged(nameof(PlanSummary));
        Status = "Тестовый порт удалён из черновика. Сохраните назначение, чтобы применить удаление. IP компьютера не меняется.";
    }

    public void SaveMapping()
    {
        if (!CanAssignPort) return;
        if (SelectedPort?.Port is { } port) port.TestMbps = DataTestSpeed.Parse(TestSpeedText);
        Plan.Validate(); savePlan(Current.Id, Plan, SelectedPort?.Port?.Ip);
        Status = "План и назначение сохранены. Настройки IPv4 компьютера не менялись.";
    }

    public Task ApplyIpv4() => Perform(() => operations.ApplyIpv4(Current.Id,
        new(AddressMode?.Key ?? "", Address, Mask, Gateway, DnsMode?.Key ?? "", Dns)));
    public Task Rename() => Perform(() => operations.Rename(Current.Id, AdapterName));

    private async Task Perform(Func<Task<string>> action)
    {
        if (!CanChangeSystem) return;
        IsBusy = true;
        try
        {
            var result = await operate(action);
            if (refresh() is { } card) LoadCurrent(card);
            Status = result;
        }
        catch (Exception ex)
        {
            // Preserve the entered draft on failure; the operator can explicitly
            // reload actual values after a partially applied native operation.
            Status = ex.Message;
        }
        finally { IsBusy = false; }
    }
}
