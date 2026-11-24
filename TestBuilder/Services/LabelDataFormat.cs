using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestBuilder.Services;

// Data formatting only; printer commands and server serial numbers are kept separate.
internal static class LabelDataFormat
{
    private static readonly Regex Field = new(@"\{\{([A-Za-z]+)(?::([0-9]{1,2}))?\}\}");
    private static readonly string[] BaseFields = { "Name", "Serial", "SerialTail", "ShortSerial", "Mac", "DeviceCode", "VariantCode", "BarcodePrefix", "ServicePrefix", "Variant", "Cabinet" };
    private static readonly string[] NumericFields = { "Serial", "SerialTail", "ShortSerial", "DeviceCode", "VariantCode" };

    public static void Validate(string format, bool computed = false)
    {
        if (string.IsNullOrWhiteSpace(format) || format.Length > 1000 || format.Any(char.IsControl))
            throw new InvalidOperationException("Укажите формат данных этикетки: до 1000 символов без переводов строки.");
        foreach (Match match in Field.Matches(format))
        {
            var name = match.Groups[1].Value;
            if (!BaseFields.Contains(name) && !(computed && name is "Barcode" or "SerialText"))
                throw new InvalidOperationException("Неизвестное поле данных: " + match.Value);
            if (match.Groups[2].Success && (!NumericFields.Contains(name) || !int.TryParse(match.Groups[2].Value, out var digits) || digits is < 1 or > 10))
                throw new InvalidOperationException("Число цифр задаётся от 1 до 10 только для серийного номера, типа и комплектации.");
            if (name == "SerialTail" && !match.Groups[2].Success)
                throw new InvalidOperationException("Для последних цифр номера укажите их количество, например {{SerialTail:5}}.");
        }
        var rest = Field.Replace(format, "");
        if (rest.Contains("{{", StringComparison.Ordinal) || rest.Contains("}}", StringComparison.Ordinal))
            throw new InvalidOperationException("Проверьте скобки и поля формата этикетки.");
    }

    public static string Resolve(LabelTemplateDefinition template, string format, string serial, int shortSerial, string mac, bool computed = false)
    {
        Validate(format, computed);
        var values = new Dictionary<string, string>
        {
            ["Name"] = template.Name, ["Serial"] = serial,
            ["ShortSerial"] = shortSerial.ToString(CultureInfo.InvariantCulture), ["Mac"] = mac,
            ["DeviceCode"] = template.DeviceCode.ToString(CultureInfo.InvariantCulture),
            ["VariantCode"] = template.VariantCode.ToString(CultureInfo.InvariantCulture),
            ["BarcodePrefix"] = template.BarcodePrefix, ["ServicePrefix"] = template.ServicePrefix,
            ["Variant"] = template.VariantName ?? "", ["Cabinet"] = template.CabinetName ?? ""
        };
        return Field.Replace(format, match =>
        {
            var name = match.Groups[1].Value;
            if (name == "Barcode") return template.RenderBarcode(serial, shortSerial, mac);
            if (name == "SerialText") return template.RenderSerialText(serial, shortSerial, mac);
            if (name == "SerialTail")
            {
                var count = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                return serial.Length > count ? serial[^count..] : serial.PadLeft(count, '0');
            }
            if (name == "ShortSerial" && !template.UseShortSerial)
                throw new InvalidOperationException("Для короткого номера включите разбор полного и короткого SN и задайте смещение. Чтобы взять последние цифры полного номера, используйте {{SerialTail:5}}.");
            var value = values[name];
            var digits = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)
                : name == "ShortSerial" ? template.ShortSerialDigits : name == "DeviceCode" ? 3 : 0;
            if (digits == 0) return value;
            if (value.Length > digits)
                throw new InvalidOperationException($"Поле {name}: число {value} не помещается в {digits} цифр. Увеличьте разрядность; номер не обрезается.");
            return value.PadLeft(digits, '0');
        });
    }
}
