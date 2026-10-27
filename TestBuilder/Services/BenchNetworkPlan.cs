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
    public string Ip { get; set; } = "";
    public string TemporaryIp { get; set; } = "";
}
