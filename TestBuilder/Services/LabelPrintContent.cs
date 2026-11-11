using System;
using System.Collections.Generic;
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

    public static LabelTemplateDefinition ForModel(LabelTemplateDefinition template, EquipmentModel model, int variant)
    {
        var copy = ForDevice(template, model.Name);
        copy.CabinetName = model.LabelCabinets?.GetValueOrDefault(variant);
        if (!copy.UseShortSerial) return copy;
        if (model.LabelCode is not { } code || model.SerialSeries is not { } series)
            throw new InvalidOperationException($"Для «{model.Name}» укажите код этикетки и серию в справочнике.");
        copy.DeviceCode = code;
        copy.SerialOffset = series.SerialOffset;
        copy.FirstSerialNumber = series.FirstSerialNumber;
        copy.ShortSerialMax = series.LastSerialNumber - series.SerialOffset;
        copy.ShortSerialDigits = 5;
        copy.BarcodePrefix = variant.ToString(CultureInfo.InvariantCulture) + code.ToString("D3", CultureInfo.InvariantCulture);
        copy.VariantName = model.LabelVariants?.GetValueOrDefault(variant);
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
        if (parsed < (template.FirstSerialNumber ?? template.SerialOffset) && parsed <= template.ShortSerialMax)
            parsed += template.SerialOffset;
        shortSerial = parsed - template.SerialOffset;
        if (parsed < (template.FirstSerialNumber ?? template.SerialOffset) || shortSerial > template.ShortSerialMax)
        {
            error = $"Для {template.Name} введите полный серийный номер " +
                $"{template.FirstSerialNumber ?? template.SerialOffset}..{template.SerialOffset + template.ShortSerialMax} " +
                $"или короткий номер {(template.FirstSerialNumber ?? template.SerialOffset) - template.SerialOffset}..{template.ShortSerialMax}.";
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
        if (template.Language == "ZPL" && template.Command.Contains("{{ZplBarcode}}", StringComparison.Ordinal))
        {
            var barcode = template.UseShortSerial ? template.Barcode(shortSerial) : serial;
            var bars = Code128Encoding.EncodeAuto(barcode)
                ?? throw new InvalidOperationException("Данные штрихкода Code 128 недопустимы.");
            var width = bars.Sum(c => c - '0') * template.BarcodeModuleDots;
            var x = template.OriginXDots + Math.Max(0, (template.WidthDots - width) / 2);
            return template.Render(serial, shortSerial, mac, x, template.OriginXDots + 8);
        }
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
        var eplBars = kind == "1" ? Code128Encoding.EncodeAuto(serial)
            : Code128Encoding.Encode(serial, kind == "1C");
        var barcodeWidth = (eplBars?.Sum(c => c - '0') ?? 0) * (long)module;
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
