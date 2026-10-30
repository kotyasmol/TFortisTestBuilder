using System;
using System.Globalization;
using System.Linq;

namespace TestBuilder.Services;

/// <summary>Shared data normalization and command formatting for printing and preview.</summary>
internal static class LabelPrintContent
{
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
        => template.UseShortSerial ? template.Render(serial, shortSerial, mac)
            : template.Render(serial, 0, "", Math.Max(0, template.WidthDots / 2 - CalculateBarcodeWidth(serial) / 2),
                Math.Max(0, template.WidthDots / 2 - 20 * serial.Length / 2));

    private static int CalculateBarcodeWidth(string serial)
    {
        var baseWidth = serial.Length switch { 12 or 14 => 17.0, 19 => 16.3, 15 => 17.7, 20 => 15.0, _ => 20.0 };
        var nonDigits = serial.Count(c => !char.IsDigit(c));
        return serial.Length == 14 && nonDigits == 8 ? 370 : (int)(serial.Length * baseWidth + nonDigits * 1.7 * baseWidth);
    }
}
