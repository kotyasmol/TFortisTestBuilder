using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace TestBuilder.Services;

public sealed record ModelSerialSeries
{
    public string ServerDeviceType { get; init; } = "";
    public int FirstSerialNumber { get; init; }
    public int LastSerialNumber { get; init; }
    public int SerialOffset { get; init; }
    public string? MacPrefix { get; init; }
    public string? BoardVersion { get; init; }
    [System.Text.Json.Serialization.JsonExtensionData]
    public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; init; }

    public void Validate(string modelName)
    {
        if (string.IsNullOrWhiteSpace(ServerDeviceType) || FirstSerialNumber <= 0 ||
            LastSerialNumber < FirstSerialNumber || SerialOffset < 0 ||
            SerialOffset > FirstSerialNumber || LastSerialNumber - SerialOffset > 99999)
            throw new InvalidDataException($"Проверьте серверный тип, диапазон и смещение серийных номеров изделия «{modelName}».");
        if (MacPrefix != null && (!LabelPrintContent.TryNormalizeMac(MacPrefix + ":00:00", out var normalized) ||
            !string.Equals(normalized, MacPrefix + ":00:00", StringComparison.OrdinalIgnoreCase) ||
            LastSerialNumber - SerialOffset > ushort.MaxValue))
            throw new InvalidDataException($"Проверьте префикс MAC и диапазон изделия «{modelName}».");
    }

    public string? Check(long serial, int? issuedSerial = null)
    {
        if (serial < FirstSerialNumber || serial > LastSerialNumber)
            return $"Серийный номер {serial} не входит в диапазон {FirstSerialNumber}–{LastSerialNumber} для «{ServerDeviceType}».";
        return CheckAssigned(serial, issuedSerial);
    }

    internal static string? CheckAssigned(long serial, int? issuedSerial) =>
        issuedSerial.HasValue && serial != issuedSerial.Value
            ? $"Серийный номер {serial} отличается от номера {issuedSerial} текущего прогона."
            : null;

    internal string? CheckMac(string mac, int? serial)
    {
        if (MacPrefix == null || !mac.StartsWith(MacPrefix + ":", StringComparison.OrdinalIgnoreCase))
            return $"MAC {mac} не соответствует префиксу изделия «{ServerDeviceType}» из справочника.";
        if (!serial.HasValue) return null;
        if (Check(serial.Value) is { } error) return error;
        var shortSerial = serial.Value - SerialOffset;
        var expected = $"{MacPrefix}:{shortSerial >> 8:X2}:{shortSerial & 0xFF:X2}";
        return string.Equals(mac, expected, StringComparison.OrdinalIgnoreCase)
            ? null : $"MAC {mac} не соответствует серийному номеру {serial}. Ожидается {expected}.";
    }

    internal static string? CheckReport(string report, ModelSerialSeries? series, int? issuedSerial)
    {
        var serialLines = report.Split('\n')
            .Where(line =>
            {
                var separator = line.IndexOf('=');
                return separator > 0 && line.AsSpan(0, separator).Trim().Equals("serial_num", StringComparison.OrdinalIgnoreCase);
            })
            .Take(2).ToArray();
        const string header = "serial_num=true=";
        if (serialLines.Length != 1 || !serialLines[0].StartsWith(header, StringComparison.Ordinal) ||
            !long.TryParse(serialLines[0].AsSpan(header.Length).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var serial) ||
            serial <= 0)
            return "В отчёте нужна одна служебная строка с полным серийным номером.";
        return series == null ? CheckAssigned(serial, issuedSerial) : series.Check(serial, issuedSerial);
    }
}
