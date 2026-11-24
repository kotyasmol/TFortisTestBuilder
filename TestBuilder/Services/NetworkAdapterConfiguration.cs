using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Services;

public sealed record NetworkSettingMode(string Key, string Name)
{
    public override string ToString() => Name;
    public static NetworkSettingMode[] All { get; } = { new("keep", "Не менять"), new("dhcp", "Автоматически (DHCP)"), new("static", "Вручную") };
}

public sealed record NetworkAdapterIpv4Configuration(string AddressMode, string Address, string Mask,
    string Gateway, string DnsMode, string Dns)
{
    internal string[] DnsAddresses => Dns.Split(new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public void Validate()
    {
        foreach (var mode in new[] { AddressMode, DnsMode })
            if (!NetworkSettingMode.All.Any(m => m.Key == mode)) throw new InvalidOperationException("Выберите способ настройки IPv4 и DNS.");
        if (AddressMode == "static")
        {
            var address = ReadAddress(Address, "Адрес IPv4");
            var mask = ReadMask(Mask);
            var inverse = ~mask;
            var value = Number(address);
            if (inverse > 1 && ((value & inverse) == 0 || (value & inverse) == inverse))
                throw new InvalidOperationException("Адрес IPv4 не должен быть адресом сети или широковещательным адресом.");
            if (Gateway.Trim().Length > 0)
            {
                var gateway = Number(ReadAddress(Gateway, "Шлюз"));
                if (gateway == value || (gateway & mask) != (value & mask) ||
                    inverse > 1 && ((gateway & inverse) == 0 || (gateway & inverse) == inverse))
                    throw new InvalidOperationException("Шлюз должен быть другим адресом устройства в той же подсети.");
            }
        }
        if (DnsMode == "static")
        {
            if (DnsAddresses.Length > 8) throw new InvalidOperationException("Укажите не больше восьми DNS-серверов.");
            foreach (var address in DnsAddresses) ReadAddress(address, "DNS-сервер", allowLoopback: true);
            if (DnsAddresses.Distinct(StringComparer.Ordinal).Count() != DnsAddresses.Length)
                throw new InvalidOperationException("DNS-серверы не должны повторяться.");
        }
    }

    internal static IPAddress ReadAddress(string text, string field, bool allowLoopback = false)
    {
        text = text.Trim();
        if (!IPAddress.TryParse(text, out var value) || value.AddressFamily != AddressFamily.InterNetwork || value.ToString() != text ||
            value.GetAddressBytes()[0] is 0 or >= 224 || !allowLoopback && IPAddress.IsLoopback(value) || value.Equals(IPAddress.Broadcast))
            throw new InvalidOperationException($"{field}: введите корректный адрес IPv4.");
        return value;
    }

    private static uint Number(IPAddress value) => value.GetAddressBytes().Aggregate(0u, (a, b) => (a << 8) | b);
    private static uint ReadMask(string text)
    {
        if (!IPAddress.TryParse(text.Trim(), out var address) || address.AddressFamily != AddressFamily.InterNetwork || address.ToString() != text.Trim())
            throw new InvalidOperationException("Введите маску IPv4.");
        var value = Number(address); var inverse = ~value;
        if (value == 0 || (inverse & (inverse + 1)) != 0) throw new InvalidOperationException("Маска должна содержать непрерывные единичные биты.");
        return value;
    }

    internal IReadOnlyList<string[]> Commands(string adapterName)
    {
        Validate();
        var commands = new List<string[]>();
        if (AddressMode == "dhcp") commands.Add(new[] { "interface", "ipv4", "set", "address", "name=" + adapterName, "source=dhcp", "store=persistent" });
        if (AddressMode == "static") commands.Add(new[] { "interface", "ipv4", "set", "address", "name=" + adapterName, "source=static",
            "address=" + Address.Trim(), "mask=" + Mask.Trim(), "gateway=" + (Gateway.Trim().Length == 0 ? "none" : Gateway.Trim()), "store=persistent" });
        if (DnsMode == "dhcp") commands.Add(new[] { "interface", "ipv4", "set", "dnsservers", "name=" + adapterName, "source=dhcp" });
        if (DnsMode == "static")
        {
            commands.Add(new[] { "interface", "ipv4", "set", "dnsservers", "name=" + adapterName, "source=static", "address=" + DnsAddresses.FirstOrDefault("none"), "validate=no" });
            for (var index = 1; index < DnsAddresses.Length; index++)
                commands.Add(new[] { "interface", "ipv4", "add", "dnsservers", "name=" + adapterName, "address=" + DnsAddresses[index], "index=" + (index + 1), "validate=no" });
        }
        return commands;
    }
}

internal sealed class NetworkAdapterOperations
{
    private readonly Func<IReadOnlyList<BenchAdapter>> inspect;
    private readonly Func<IEnumerable<string>, CancellationToken, Task<string?>> execute;
    private readonly Func<string?> accessError;
    public bool Supported { get; }

    public NetworkAdapterOperations() : this(DataTestNetworkConfigurator.GetComputerAdapters, DataTestNetworkConfigurator.RunNetshAsync, NativeAccessError, OperatingSystem.IsWindows()) { }
    internal NetworkAdapterOperations(Func<IReadOnlyList<BenchAdapter>> inspect,
        Func<IEnumerable<string>, CancellationToken, Task<string?>> execute, Func<string?> accessError, bool supported = true)
    { this.inspect = inspect; this.execute = execute; this.accessError = accessError; Supported = supported; }

    private static string? NativeAccessError()
    {
        if (!OperatingSystem.IsWindows()) return "Изменение настроек карт в приложении доступно только в Windows. Откройте системные настройки сети.";
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) ? null : "Для изменения карты запустите TestBuilder от имени администратора.";
    }

    public async Task<string> ApplyIpv4(string id, NetworkAdapterIpv4Configuration configuration, CancellationToken token = default)
    {
        configuration.Validate();
        if (accessError() is { } error) throw new InvalidOperationException(error);
        var cards = inspect(); var card = Find(cards, id);
        if (configuration.AddressMode == "static" && cards.Any(c => c.Id != card.Id && c.Ipv4.Split(", ").Contains(configuration.Address.Trim())))
            throw new InvalidOperationException("Такой адрес IPv4 уже назначен другой карте этого компьютера.");
        var commands = configuration.Commands(card.Name);
        if (commands.Count == 0) return "Изменений IPv4 и DNS не задано.";
        var completed = 0;
        try
        {
            foreach (var command in commands)
            {
                token.ThrowIfCancellationRequested();
                // Revalidate identity before every operation: a removed/renamed card must
                // never cause a command to be sent to a different interface with its old name.
                var now = Find(inspect(), id);
                if (now.Name != card.Name) throw new InvalidOperationException("Название карты изменилось. Обновите данные перед применением.");
                error = await execute(command, token);
                if (error != null) throw new InvalidOperationException($"Не удалось применить настройки: {error}");
                completed++;
            }
        }
        catch (Exception ex) when (completed > 0)
        {
            var message = ex.Message + $" Часть настроек уже применена (команд: {completed}). Обновите данные карты.";
            if (ex is OperationCanceledException) throw new OperationCanceledException(message, ex, token);
            throw new InvalidOperationException(message, ex);
        }
        return configuration.AddressMode == "dhcp" ? "DHCP настроен. Получение адреса может занять некоторое время." : "Настройки IPv4 и DNS применены.";
    }

    public async Task<string> Rename(string id, string name, CancellationToken token = default)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 128 || name.Any(char.IsControl)) throw new InvalidOperationException("Введите название карты длиной 1–128 символов.");
        if (accessError() is { } error) throw new InvalidOperationException(error);
        var cards = inspect(); var card = Find(cards, id);
        if (cards.Any(c => !c.Id.Equals(card.Id, StringComparison.OrdinalIgnoreCase) && c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Такое название уже есть у другой карты.");
        if (card.Name == name) return "Название карты не изменилось.";
        if (await execute(new[] { "interface", "set", "interface", "name=" + card.Name, "newname=" + name }, token) is { } failure)
            throw new InvalidOperationException(failure);
        return "Карта переименована.";
    }

    private static BenchAdapter Find(IReadOnlyList<BenchAdapter> cards, string id) =>
        cards.SingleOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("Карта больше не найдена. Обновите список.");
}
