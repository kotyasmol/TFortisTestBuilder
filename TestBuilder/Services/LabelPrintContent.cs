using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestBuilder.Services;

/// <summary>Shared data normalization and command formatting for printing and preview.</summary>
internal static class LabelPrintContent
{
    public static LabelTemplateDefinition ForDevice(LabelTemplateDefinition template, string? deviceModel)
    {
        var copy = template.Copy();
        if (!string.IsNullOrWhiteSpace(deviceModel)) copy.Name = deviceModel.Trim();
        copy.Validate();
        return copy;
    }

    public static bool TryResolveSerial(LabelTemplateDefinition template, string serial,
        out int fullSerial, out int shortSerial, out string error)
    {
        fullSerial = shortSerial = 0;
        error = "";
        if (!int.TryParse(serial, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            error = $"Серийный номер '{serial}' слишком большой или имеет неверный формат.";
            return false;
        }
        if (parsed <= template.ShortSerialMax)
        {
            shortSerial = parsed;
            fullSerial = template.SerialOffset + parsed;
            return true;
        }
        shortSerial = parsed - template.SerialOffset;
        if (shortSerial < 0 || shortSerial > template.ShortSerialMax)
        {
            error = $"Для {template.Name} введите полный серийник " +
                $"{template.SerialOffset}..{template.SerialOffset + template.ShortSerialMax} " +
                $"или короткий номер 0..{template.ShortSerialMax}.";
            return false;
        }
        fullSerial = parsed;
        return true;
    }

    public static bool TryNormalizeMac(string value, out string normalized)
    {
        normalized = "";
        var compact = value.Trim().Replace(":", "").Replace("-", "").Replace(".", "").Replace(" ", "");
        if (compact.Length != 12 || compact.Any(c => !Uri.IsHexDigit(c))) return false;
        normalized = string.Join(":", Enumerable.Range(0, 6).Select(i => compact.Substring(i * 2, 2).ToUpperInvariant()));
        return true;
    }

    public static string Render(LabelTemplateDefinition template, string serial, int shortSerial, string mac)
    {
        if (template.UseShortSerial) return template.Render(serial, shortSerial, mac);
        var kind = Code128Encoding.CanUseC(serial) ? "1C" : "1B";
        var module = 2;
        var font = 3;
        var multiplier = 1;
        // Read the parameters next to the centering placeholders, so editing the
        // module width or font in a custom EPL template keeps centering correct.
        var bar = Regex.Match(template.Command, @"(?m)^B\{\{BarcodeX\}\},\d+,0,([^,]+),(\d+),");
        if (bar.Success)
        {
            if (bar.Groups[1].Value != "{{BarcodeType}}") kind = bar.Groups[1].Value;
            module = int.Parse(bar.Groups[2].Value, CultureInfo.InvariantCulture);
        }
        var text = Regex.Match(template.Command, @"(?m)^A\{\{TextX\}\},\d+,0,([1-5]),(\d+),");
        if (text.Success)
        {
            font = int.Parse(text.Groups[1].Value, CultureInfo.InvariantCulture);
            multiplier = int.Parse(text.Groups[2].Value, CultureInfo.InvariantCulture);
        }
        var bars = kind == "1" ? Code128Encoding.EncodeAuto(serial)
            : Code128Encoding.Encode(serial, kind == "1C");
        var barcodeWidth = (bars?.Sum(c => c - '0') ?? 0) * (long)module;
        var textWidth = (long)EplFontMetrics.Size(font, template.PrinterDpi).Width * multiplier * serial.Length;
        return template.Render(serial, 0, "", (int)Math.Max(0, (template.WidthDots - barcodeWidth) / 2),
            (int)Math.Max(0, (template.WidthDots - textWidth) / 2));
    }
}

internal static class EplFontMetrics
{
    // EPL Programming Guide, A command: fixed-pitch fonts at each resolution.
    public static (int Width, int Height) Size(int font, int dpi) => dpi == 300
        ? new[] { (12, 20), (16, 28), (20, 36), (24, 44), (48, 80) }[font - 1]
        : new[] { (8, 12), (10, 16), (12, 20), (14, 24), (32, 48) }[font - 1];
}
