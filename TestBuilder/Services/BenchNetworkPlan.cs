using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;

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
        var bits = ReadMask();
        var inverse = ~bits;
        if (Addresses == null || Addresses.Count < 1 || Addresses.Any(a => a == null))
            throw new InvalidOperationException("Добавьте хотя бы один адрес стенда.");
        var used = new Dictionary<string, (int Row, string Field)>();
        for (var index = 0; index < Addresses.Count; index++)
        {
            var row = Addresses[index];
            foreach (var (field, text) in new[] { ("Название порта", row.Name), ("Назначение / разъём", row.Role), ("Пара DataTest", row.Pair) })
                if (text == null || text.Length > 100 || text.Any(char.IsControl) || field == "Пара DataTest" && text.Contains(','))
                    throw new InvalidOperationException($"Строка {index + 1}, поле «{field}»: до 100 символов без переводов строки; имя пары не должно содержать запятых.");
            if (row.TestMbps is < 1 or > 1000)
                throw new InvalidOperationException($"Строка {index + 1}, поле «Мбит/с теста»: введите целое число от 1 до 1000.");
            foreach (var (field, text) in new[] { ("Адрес карты", row.Ip), ("Временный адрес", row.TemporaryIp) })
            {
                var location = $"Строка {index + 1}, поле «{field}»";
                if (string.IsNullOrWhiteSpace(text))
                    throw new InvalidOperationException($"{location}: заполните адрес IPv4.");
                if (!TryAddress(text, out var address) || IPAddress.Parse(text).ToString() != text)
                    throw new InvalidOperationException($"{location}: некорректный адрес IPv4 «{text}».");
                if (inverse > 1 && ((address & inverse) == 0 || (address & inverse) == inverse))
                    throw new InvalidOperationException($"{location}: {text} — адрес сети или широковещательный адрес.");
                if (used.TryGetValue(text, out var owner))
                    throw new InvalidOperationException($"{location}: {text} уже указан в строке {owner.Row} (поле «{owner.Field}»). Основные и временные адреса должны быть уникальны.");
                used.Add(text, (index + 1, field));
            }
        }
    }

    // Draft suggestions only: never changes Windows or existing plan rows.
    public BenchNetworkAddress CreateNextAddress()
    {
        var mask = ReadMask();
        var occupied = Addresses.SelectMany(a => new[] { a.Ip, a.TemporaryIp })
            .Where(ip => TryAddress(ip, out _)).Select(ip => ToNumber(IPAddress.Parse(ip))).ToHashSet();
        var defaults = new BenchNetworkPlan().Addresses[0];
        var ip = Next(Addresses.Select(a => a.Ip), defaults.Ip, mask, occupied);
        if (TryAddress(ip, out var number)) occupied.Add(number);
        return new BenchNetworkAddress { Ip = ip, TemporaryIp = Next(Addresses.Select(a => a.TemporaryIp), defaults.TemporaryIp, mask, occupied) };
    }

    private uint ReadMask()
    {
        if (!IPAddress.TryParse(Mask, out var mask) || mask.AddressFamily != AddressFamily.InterNetwork)
            throw new InvalidOperationException("Введите маску IPv4.");
        var bits = ToNumber(mask);
        var inverse = ~bits;
        if (bits == 0 || (inverse & (inverse + 1)) != 0)
            throw new InvalidOperationException("Маска должна содержать непрерывные единичные биты.");
        return bits;
    }

    private static bool TryAddress(string? text, out uint value)
    {
        value = 0;
        if (!IPAddress.TryParse(text, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork ||
            ip.GetAddressBytes()[0] is 0 or 127 or >= 224) return false;
        value = ToNumber(ip); return true;
    }

    private static uint ToNumber(IPAddress ip) => ip.GetAddressBytes().Aggregate(0u, (a, b) => (a << 8) | b);
    private static string Next(IEnumerable<string> addresses, string fallback, uint mask, HashSet<uint> occupied)
    {
        var existing = addresses.Where(ip => TryAddress(ip, out _)).ToArray();
        var anchor = ToNumber(IPAddress.Parse(existing.LastOrDefault() ?? fallback));
        var inverse = ~mask;
        var first = (ulong)(anchor & mask) + (inverse > 1 ? 1u : 0u);
        var last = (ulong)(anchor & mask) + inverse - (inverse > 1 ? 1u : 0u);
        first = Math.Max(first, 1UL << 24); // Exclude 0/8.
        last = Math.Min(last, 0xDFFFFFFFUL); // Exclude multicast/reserved addresses.
        if ((last >> 24) == 127) last = (127UL << 24) - 1;
        if (first > last) return "";
        var candidate = existing.Length == 0 ? Math.Clamp((ulong)anchor, first, last) : anchor < last ? anchor + 1UL : first;
        for (ulong i = 0; i < Math.Min(last - first + 1, (ulong)occupied.Count + 1); i++)
        {
            if (!occupied.Contains((uint)candidate))
                return new IPAddress(new[] { (byte)(candidate >> 24), (byte)(candidate >> 16), (byte)(candidate >> 8), (byte)candidate }).ToString();
            candidate = candidate == last ? first : candidate + 1;
        }
        return ""; // A full subnet needs an explicit address in another subnet.
    }
}
public sealed partial class BenchNetworkAddress : ObservableObject
{
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string role = "";
    [ObservableProperty] private string pair = "";
    [ObservableProperty] private int testMbps = 100;
    [ObservableProperty] private bool includeInDataTest;
    [ObservableProperty] private string ip = "";
    [ObservableProperty] private string temporaryIp = "";
}
