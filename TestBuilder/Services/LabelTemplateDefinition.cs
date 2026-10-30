using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TestBuilder.Services;
public sealed class LabelTemplateDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Новая этикетка";
    public string Language { get; set; } = "ZPL";
    public bool UseShortSerial { get; set; }
    public bool RequiresMac { get; set; }
    public bool MatchServerSerial { get; set; }
    public int SerialOffset { get; set; }
    public int ShortSerialMax { get; set; } = 65535;
    public int ShortSerialDigits { get; set; } = 5;
    public int DeviceCode { get; set; }
    public string BarcodePrefix { get; set; } = "";
    public int WidthDots { get; set; } = 354;
    public int PrinterDpi { get; set; } = 300;
    public string Command { get; set; } = "^XA^FO20,20^A0N,30,30^FD{{Serial}}^FS^XZ";
    public LabelTemplateDefinition Copy() => JsonSerializer.Deserialize<LabelTemplateDefinition>(JsonSerializer.Serialize(this))!;
    public LabelTemplateDefinition CopyUpgraded()
    {
        var copy = Copy();
        LabelTemplateCatalog.UpgradeBuiltIn(copy);
        return copy;
    }
    public override string ToString() => Name;
    public string Barcode(int shortSerial) => BarcodePrefix + shortSerial.ToString("D" + ShortSerialDigits, CultureInfo.InvariantCulture);
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || Language is not ("ZPL" or "EPL") ||
            WidthDots is < 1 or > 10000 || PrinterDpi is not (203 or 300) || SerialOffset < 0 || ShortSerialMax < 0 ||
            (long)SerialOffset + ShortSerialMax > int.MaxValue || ShortSerialDigits is < 1 or > 10 ||
            string.IsNullOrWhiteSpace(Command) || Command.Length > 100000 || (RequiresMac && !UseShortSerial) || (MatchServerSerial && !UseShortSerial))
            throw new InvalidOperationException("Проверьте имя, язык ZPL/EPL, размеры, диапазон серийников и команду этикетки. MAC и поиск по диапазону требуют короткого номера.");
        foreach (Match token in Regex.Matches(Command, @"\{\{(.*?)\}\}"))
            if (!new[] { "Serial", "ShortSerial", "Mac", "Barcode", "Name", "WidthDots", "BarcodeX", "TextX", "BarcodeType" }.Contains(token.Groups[1].Value))
                throw new InvalidOperationException("Неизвестная подстановка: " + token.Value);
        if (Name.IndexOfAny(new[] { '^', '~', '"', '\r', '\n' }) >= 0 || BarcodePrefix.Any(c => !char.IsAsciiDigit(c)))
            throw new InvalidOperationException("Название не должно содержать команды принтера; префикс штрихкода — только цифры.");
    }
    public string Render(string serial, int shortSerial, string mac, int barcodeX = 0, int textX = 0)
    {
        Validate();
        var values = new Dictionary<string, string> {
            ["Serial"] = serial, ["ShortSerial"] = shortSerial.ToString("D" + ShortSerialDigits, CultureInfo.InvariantCulture),
            ["Mac"] = mac, ["Barcode"] = UseShortSerial ? Barcode(shortSerial) : serial, ["Name"] = Name,
            ["BarcodeType"] = Code128Encoding.CanUseC(serial) ? "1C" : "1B",
            ["WidthDots"] = WidthDots.ToString(CultureInfo.InvariantCulture), ["BarcodeX"] = barcodeX.ToString(CultureInfo.InvariantCulture),
            ["TextX"] = textX.ToString(CultureInfo.InvariantCulture) };
        return Regex.Replace(Command, @"\{\{(.*?)\}\}", m => values[m.Groups[1].Value]);
    }
}
public static class LabelTemplateCatalog
{
    // Only exact shipped commands are upgraded; user-authored commands and metadata survive.
    public static void UpgradeBuiltIn(LabelTemplateDefinition? template)
    {
        if (template == null) return;
        using var stream = typeof(LabelTemplateCatalog).Assembly.GetManifestResourceStream("TestBuilder.label-template-upgrades.json")!;
        var previous = JsonSerializer.Deserialize<List<LabelTemplateDefinition>>(stream)!;
        if (previous.Any(t => t.Id == template.Id && t.Language == template.Language && t.Command == template.Command))
            template.Command = Defaults().Single(t => t.Id == template.Id).Command;
    }
    public static List<LabelTemplateDefinition> Defaults()
    {
        using var stream = typeof(LabelTemplateCatalog).Assembly.GetManifestResourceStream("TestBuilder.label-templates.json")!;
        return JsonSerializer.Deserialize<List<LabelTemplateDefinition>>(stream)!;
    }
    public static void Validate(IReadOnlyList<LabelTemplateDefinition> templates)
    {
        if (templates.Count == 0 || templates.Any(t => t == null) || templates.Select(t => t.Id).Distinct().Count() != templates.Count)
            throw new InvalidOperationException("Нужен непустой список шаблонов с уникальными идентификаторами.");
        foreach (var template in templates) template.Validate();
        var ranges = templates.Where(t => t.MatchServerSerial).OrderBy(t => t.SerialOffset).ToArray();
        for (var i = 1; i < ranges.Length; i++)
            if (ranges[i].SerialOffset <= (long)ranges[i - 1].SerialOffset + ranges[i - 1].ShortSerialMax)
                throw new InvalidOperationException($"Диапазоны поиска шаблонов «{ranges[i - 1].Name}» и «{ranges[i].Name}» пересекаются.");
    }
    public static LabelTemplateDefinition Legacy(string id) => AppSettings.Instance.LabelTemplates.FirstOrDefault(t => t.Id == id)?.Copy()
        ?? throw new InvalidOperationException($"Не найден шаблон этикетки {id}. Выберите шаблон в ноде.");
}
