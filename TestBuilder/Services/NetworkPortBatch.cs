using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace TestBuilder.Services;

// Builds a detached draft. Neither Windows addresses nor the saved plan change here.
public sealed record NetworkPortBatch(int Count, int FirstNumber, string NamePrefix, string Role,
    int TestMbps, string FirstIp, string FirstTemporaryIp, bool CreatePairs)
{
    public const int MaximumBatchSize = 1024; // Per operation, not a limit on the entire stand.

    public BenchNetworkPlan AppendTo(BenchNetworkPlan source)
    {
        if (Count is < 1 or > MaximumBatchSize)
            throw new InvalidOperationException($"За одно добавление укажите от 1 до {MaximumBatchSize} портов.");
        if (FirstNumber < 1 || (long)FirstNumber + Count - 1 > int.MaxValue)
            throw new InvalidOperationException("Проверьте начальный номер порта.");
        var prefix = NamePrefix?.Trim() ?? "";
        if (prefix.Length is < 1 or > 70 || prefix.Any(c => c == ',' || char.IsControl(c)))
            throw new InvalidOperationException("Название портов: от 1 до 70 символов без запятых и переводов строки.");
        if (CreatePairs && Count % 2 != 0)
            throw new InvalidOperationException("Для пар DataTest укажите чётное число портов. Чтобы внести отдельный порт, отключите создание пар.");
        if (TestMbps is < 1 or > 1000) throw new InvalidOperationException("Скорость теста: от 1 до 1000 Мбит/с.");
        if (source.Addresses.Count > 0) source.Validate();
        var result = JsonSerializer.Deserialize<BenchNetworkPlan>(JsonSerializer.Serialize(source))!;
        var mask = Parse(result.Mask, "Маска IPv4");
        var firstIp = Parse(FirstIp, "Первый адрес карты");
        var firstTemporary = Parse(FirstTemporaryIp, "Первый временный адрес");
        var names = result.Addresses.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pairs = result.Addresses.Where(a => a.Pair.Length > 0).Select(a => a.Pair).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Count; i++)
        {
            var number = FirstNumber + i;
            var name = $"{prefix} {number.ToString(CultureInfo.InvariantCulture)}";
            if (!names.Add(name)) throw new InvalidOperationException($"Название «{name}» уже есть в плане. Измените начальный номер или название портов.");
            var pairFirst = FirstNumber + i / 2 * 2;
            var pair = CreatePairs ? $"{prefix} {pairFirst}–{pairFirst + 1}" : "";
            if (CreatePairs && i % 2 == 0 && !pairs.Add(pair))
                throw new InvalidOperationException($"Пара «{pair}» уже есть в плане. Измените начальный номер или название портов.");
            result.Addresses.Add(new BenchNetworkAddress
            {
                Name = name, Role = Role?.Trim() ?? "", Pair = pair, TestMbps = TestMbps, IncludeInDataTest = CreatePairs,
                Ip = Offset(firstIp, i, mask, "Основные адреса"),
                TemporaryIp = Offset(firstTemporary, i, mask, "Временные адреса")
            });
        }
        result.Validate();
        return result;
    }

    private static uint Parse(string text, string field)
    {
        text = text?.Trim() ?? "";
        if (!IPAddress.TryParse(text, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork || ip.ToString() != text)
            throw new InvalidOperationException($"{field}: введите адрес IPv4.");
        return ip.GetAddressBytes().Aggregate(0u, (a, b) => (a << 8) | b);
    }

    private static string Offset(uint first, int offset, uint mask, string field)
    {
        var address = (ulong)first + (uint)offset;
        if (address > uint.MaxValue || ((uint)address & mask) != (first & mask))
            throw new InvalidOperationException($"{field}: диапазон не помещается в подсеть. Уменьшите число портов или измените адреса и маску плана.");
        return new IPAddress(new[] { (byte)(address >> 24), (byte)(address >> 16), (byte)(address >> 8), (byte)address }).ToString();
    }
}
