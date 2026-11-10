using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TestBuilder.Services;
public sealed class LabelTemplateDefinition
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Новая этикетка";
    public string Language { get; set; } = "ZPL";
    public bool UseShortSerial { get; set; }
    public bool RequiresMac { get; set; }
    public bool MatchServerSerial { get; set; }
    public int SerialOffset { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FirstSerialNumber { get; set; }
    public int ShortSerialMax { get; set; } = 65535;
    public int ShortSerialDigits { get; set; } = 5;
    public int DeviceCode { get; set; }
    public string BarcodePrefix { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VariantName { get; set; }
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
    internal bool MatchesServerSerial(int serial) =>
        MatchServerSerial && serial >= (FirstSerialNumber ?? SerialOffset) && (long)serial <= (long)SerialOffset + ShortSerialMax;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || Language is not ("ZPL" or "EPL") ||
            WidthDots is < 1 or > 10000 || PrinterDpi is not (203 or 300) || SerialOffset < 0 || ShortSerialMax < 0 ||
            (long)SerialOffset + ShortSerialMax > int.MaxValue || ShortSerialDigits is < 1 or > 10 ||
            FirstSerialNumber.HasValue && (FirstSerialNumber < SerialOffset || FirstSerialNumber > (long)SerialOffset + ShortSerialMax) ||
            string.IsNullOrWhiteSpace(Command) || Command.Length > 100000 || (RequiresMac && !UseShortSerial) || (MatchServerSerial && !UseShortSerial))
            throw new InvalidOperationException("Проверьте имя, язык ZPL/EPL, размеры, диапазон серийных номеров и команду этикетки. MAC и поиск по диапазону требуют короткого номера.");
        foreach (Match token in Regex.Matches(Command, @"\{\{(.*?)\}\}"))
            if (!new[] { "Serial", "ShortSerial", "Mac", "Barcode", "Name", "Variant", "WidthDots", "BarcodeX", "TextX", "BarcodeType" }.Contains(token.Groups[1].Value))
                throw new InvalidOperationException("Неизвестная подстановка: " + token.Value);
        if (!IsSafeLabelText(Name) || VariantName != null && !IsSafeLabelText(VariantName) ||
            BarcodePrefix.Any(c => !char.IsAsciiDigit(c)))
            throw new InvalidOperationException("Название не должно содержать команды принтера; префикс штрихкода — только цифры.");
    }
    internal static bool IsSafeLabelText(string text) => text.IndexOfAny(new[] { '^', '~', '"', '\r', '\n' }) < 0;
    public string Render(string serial, int shortSerial, string mac, int barcodeX = 0, int textX = 0)
    {
        Validate();
        var values = new Dictionary<string, string> {
            ["Serial"] = serial, ["ShortSerial"] = shortSerial.ToString("D" + ShortSerialDigits, CultureInfo.InvariantCulture),
            ["Mac"] = mac, ["Barcode"] = UseShortSerial ? Barcode(shortSerial) : serial, ["Name"] = Name,
            ["Variant"] = string.IsNullOrWhiteSpace(VariantName) ? "" : $"TYPE: {VariantName}",
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
        {
            var current = Defaults().Single(t => t.Id == template.Id);
            template.Command = current.Command;
            if (current.UseShortSerial && template.BarcodePrefix.Length == 3 &&
                current.BarcodePrefix == "0" + template.BarcodePrefix)
                template.BarcodePrefix = current.BarcodePrefix;
        }
    }
    public static List<LabelTemplateDefinition> Defaults()
    {
        using var stream = typeof(LabelTemplateCatalog).Assembly.GetManifestResourceStream("TestBuilder.label-templates.json")!;
        return JsonSerializer.Deserialize<List<LabelTemplateDefinition>>(stream)!;
    }

    internal static List<LabelTemplateDefinition> WorkstationTemplates()
    {
        var templates = AppSettings.Instance.LabelTemplates.Select(template => template.CopyUpgraded()).ToList();
        EquipmentCatalog catalog;
        try { catalog = EquipmentCatalog.Load(AppSettings.Instance.ResolvedGraphsFolder); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            Logging.DiagnosticLog.WriteException("Каталог моделей для этикеток", ex);
            return templates;
        }
        var stock = Defaults().First(template => template.Language == "ZPL" && template.UseShortSerial);
        foreach (var model in catalog.Models)
        {
            if (model is not { SerialSeries: not null, LabelCode: not null } ||
                templates.Any(template => template.MatchServerSerial &&
                    EquipmentCatalog.Normalize(template.Name) == EquipmentCatalog.Normalize(model.Name)))
                continue;
            var template = LabelPrintContent.ForModel(stock, model, 0);
            template.Id = "catalog:" + model.Id;
            templates.Add(template);
        }
        return templates;
    }
    public static void Validate(IReadOnlyList<LabelTemplateDefinition> templates)
    {
        if (templates.Count == 0 || templates.Any(t => t == null) || templates.Select(t => t.Id).Distinct().Count() != templates.Count)
            throw new InvalidOperationException("Нужен непустой список шаблонов с уникальными идентификаторами.");
        foreach (var template in templates) template.Validate();
        var ranges = templates.Where(t => t.MatchServerSerial).OrderBy(t => t.FirstSerialNumber ?? t.SerialOffset).ToArray();
        for (var i = 1; i < ranges.Length; i++)
            if ((ranges[i].FirstSerialNumber ?? ranges[i].SerialOffset) <= (long)ranges[i - 1].SerialOffset + ranges[i - 1].ShortSerialMax)
                throw new InvalidOperationException($"Диапазоны поиска шаблонов «{ranges[i - 1].Name}» и «{ranges[i].Name}» пересекаются.");
    }
    public static LabelTemplateDefinition Legacy(string id) => AppSettings.Instance.LabelTemplates.FirstOrDefault(t => t.Id == id)?.Copy()
        ?? throw new InvalidOperationException($"Не найден шаблон этикетки {id}. Выберите шаблон в ноде.");
}
