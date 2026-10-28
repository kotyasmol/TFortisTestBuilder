using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace TestBuilder.Services;

public sealed class BenchNetworkPlan
{
    public string Mask { get; set; } = "255.255.255.0";
    public List<BenchNetworkAddress> Addresses { get; set; } = Enumerable.Range(2, 10)
        .Select(i => new BenchNetworkAddress { Ip = $"192.168.0.{i}", TemporaryIp = $"192.0.2.{i}" }).ToList();
    public string BuildDataTestPairs()
    {
        Validate();
        var included = Addresses.Where(a => a.IncludeInDataTest).ToArray();
        if (included.Length == 0) throw new InvalidOperationException("Отметьте порты для DataTest в плане сети.");
        if (included.Any(a => string.IsNullOrWhiteSpace(a.Pair)))
            throw new InvalidOperationException("У каждого выбранного порта задайте имя пары.");
        return string.Join(Environment.NewLine, included.GroupBy(a => a.Pair.Trim()).Select(group => {
            var ports = group.ToArray();
            if (ports.Length != 2 || ports[0].TestMbps != ports[1].TestMbps)
                throw new InvalidOperationException($"Пара «{group.Key}»: нужны два порта с одинаковой скоростью теста.");
            return $"{group.Key},{ports[0].Ip},{ports[1].Ip},{ports[0].TestMbps}";
        }));
    }

    public void Validate()
    {
        if (!IPAddress.TryParse(Mask, out var mask) || mask.AddressFamily != AddressFamily.InterNetwork)
            throw new InvalidOperationException("Введите маску IPv4.");
        var bits = mask.GetAddressBytes().Aggregate(0u, (a, b) => (a << 8) | b);
        var inverse = ~bits;
        if (bits == 0 || (inverse & (inverse + 1)) != 0)
            throw new InvalidOperationException("Маска должна содержать непрерывные единичные биты.");
        if (Addresses == null || Addresses.Count is < 1 or > 247 || Addresses.Any(a => a == null))
            throw new InvalidOperationException("Добавьте от 1 до 247 адресов стенда.");
        foreach (var row in Addresses)
        {
            if (row.Name == null || row.Role == null || row.Pair == null || row.Name.Length > 100 || row.Role.Length > 100 || row.Pair.Length > 100 ||
                row.Name.Any(char.IsControl) || row.Role.Any(char.IsControl) ||
                row.Pair.Any(c => c == ',' || char.IsControl(c)) || row.TestMbps is < 1 or > 1000)
                throw new InvalidOperationException("Названия: до 100 символов; пара без запятых; скорость теста 1–1000 Мбит/с.");
        }
        var all = Addresses.SelectMany(a => new[] { a.Ip, a.TemporaryIp }).ToArray();
        foreach (var text in all)
        {
            if (!IPAddress.TryParse(text, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork || ip.ToString() != text ||
                ip.GetAddressBytes()[0] is 0 or 127 or >= 224)
                throw new InvalidOperationException($"Некорректный адрес IPv4: {text}.");
            var address = ip.GetAddressBytes().Aggregate(0u, (a, b) => (a << 8) | b);
            if (inverse > 1 && ((address & inverse) == 0 || (address & inverse) == inverse))
                throw new InvalidOperationException($"{text}: нельзя назначать адрес сети или широковещательный адрес.");
        }
        if (all.Distinct().Count() != all.Length)
            throw new InvalidOperationException("Основные и временные адреса должны быть уникальны.");
    }
}
public sealed class BenchNetworkAddress
{
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Pair { get; set; } = "";
    public int TestMbps { get; set; } = 100;
    public bool IncludeInDataTest { get; set; }
    public string Ip { get; set; } = "";
    public string TemporaryIp { get; set; } = "";
}
