using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;

namespace TestBuilder.Services;
public sealed class LabelTemplateDefinition : System.ComponentModel.INotifyPropertyChanged
{
    private static readonly Regex FittedZplName = new(@"\^FO(\d+),\d+\^A0N?,\d+,\{\{FitNameWidth\}\}\^FD([^\^]*)\^FS");
    private static readonly Regex FittedZplSerial = new(@"\^FO(\d+),(\d+)\^A0N?,\d+,\{\{SerialTextWidth\}\}\^FDSN: \{\{(?:Serial|SerialText)\}\}\^FS");

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public string Id { get; set; } = Guid.NewGuid().ToString();
    private string _name = "Новая этикетка";
    public string Name { get => _name; set { _name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); } }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
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
    public string? SerialTextFormat { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BarcodeFormat { get; set; }
    public string ServicePrefix { get; set; } = "0";
    public int VariantCode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VariantName { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CabinetName { get; set; }
    public int CabinetWidthDots { get; set; } = 84;
    public int CabinetFontHeightDots { get; set; } = 20;
    public int WidthDots { get; set; } = 354;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? HeightDots { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int OriginXDots { get; set; }
    public int BarcodeModuleDots { get; set; } = 2;
    public int BarcodeTopDots { get; set; } = 112;
    public int BarcodeHeightDots { get; set; } = 34;
    public int PrinterDpi { get; set; } = 300;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? NameFontMaxWidthDots { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? NameCharacterWidthFactor { get; set; }
    public string Command { get; set; } = "^XA^FO20,20^A0N,30,30^FD{{Serial}}^FS^XZ";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LabelLayoutDefinition? Layout { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LabelDataBindings? DataBindings { get; set; }
    public LabelTemplateDefinition Copy()
    {
        var copy = JsonSerializer.Deserialize<LabelTemplateDefinition>(JsonSerializer.Serialize(this))!;
        if (copy.Layout != null && copy.Command != "{{VisualLayoutV1}}")
        {
            copy.Layout.OriginalCommand ??= copy.Command;
            // Old executables reject an unknown token instead of printing a stale raw layout.
            copy.Command = "{{VisualLayoutV1}}";
        }
        return copy;
    }
    public LabelTemplateDefinition CopyUpgraded()
    {
        var copy = Copy();
        LabelTemplateCatalog.UpgradeBuiltIn(copy);
        return copy;
    }
    public override string ToString() => Name;
    internal void CommitDataFormats()
    {
        // An explicit editor save preserves the rules shown to the user, including legacy full SN.
        SerialTextFormat ??= "{{Serial}}";
        BarcodeFormat ??= UseShortSerial ? "{{BarcodePrefix}}{{ShortSerial}}" : "{{Serial}}";
    }
    public string Barcode(int shortSerial) => RenderBarcode(((long)SerialOffset + shortSerial).ToString(CultureInfo.InvariantCulture), shortSerial, "");
    public string RenderSerialText(string serial, int shortSerial, string mac)
    {
        var value = SerialTextFormat == null ? serial : LabelDataFormat.Resolve(this, SerialTextFormat, serial, shortSerial, mac);
        if (!IsSafeLabelText(value)) throw new InvalidOperationException("Надпись SN не должна содержать команды принтера.");
        return value;
    }
    public string RenderBarcode(string serial, int shortSerial, string mac)
    {
        var value = BarcodeFormat == null
            ? UseShortSerial ? BarcodePrefix + shortSerial.ToString("D" + ShortSerialDigits, CultureInfo.InvariantCulture) : serial
            : LabelDataFormat.Resolve(this, BarcodeFormat, serial, shortSerial, mac);
        if (value.Length is < 1 or > 256 || value.Any(c => c is < ' ' or > '~') || value.IndexOfAny(new[] { '^', '~', '>', '"' }) >= 0)
            throw new InvalidOperationException("Штрихкод: от 1 до 256 цифр или печатных латинских символов без команд принтера.");
        return value;
    }
    internal bool MatchesServerSerial(int serial) =>
        MatchServerSerial && serial >= (FirstSerialNumber ?? SerialOffset) && (long)serial <= (long)SerialOffset + ShortSerialMax;

    public void Validate()
    {
        DataBindings?.Validate();
        Layout?.Validate();
        if (SerialTextFormat != null) LabelDataFormat.Validate(SerialTextFormat);
        if (BarcodeFormat != null) LabelDataFormat.Validate(BarcodeFormat);
        if (ServicePrefix == null || ServicePrefix.Length > 10 || ServicePrefix.Any(c => !char.IsAsciiDigit(c)) || DeviceCode < 0 || VariantCode is < 0 or > 999)
            throw new InvalidOperationException("Служебный префикс — до 10 цифр; код изделия и комплектации не могут быть отрицательными.");
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || Language is not ("ZPL" or "EPL") ||
            WidthDots is < 1 or > 10000 || HeightDots is < 1 or > 32000 || OriginXDots is < 0 or > 22000 ||
            BarcodeModuleDots is < 1 or > 10 || BarcodeTopDots is < 0 or > 32000 || BarcodeHeightDots is < 1 or > 32000 ||
            PrinterDpi is not (203 or 300) || SerialOffset < 0 || ShortSerialMax < 0 ||
            NameFontMaxWidthDots is < 1 or > 1000 || NameCharacterWidthFactor is < .1m or > 2m ||
            CabinetWidthDots is < 1 or > 10000 || CabinetFontHeightDots is < 1 or > 32000 ||
            (long)SerialOffset + ShortSerialMax > int.MaxValue || ShortSerialDigits is < 1 or > 10 ||
            FirstSerialNumber.HasValue && (FirstSerialNumber < SerialOffset || FirstSerialNumber > (long)SerialOffset + ShortSerialMax) ||
            string.IsNullOrWhiteSpace(Command) || Command.Length > 100000 || (RequiresMac && !UseShortSerial) || (MatchServerSerial && !UseShortSerial))
            throw new InvalidOperationException("Проверьте имя, язык ZPL/EPL, размеры, диапазон серийных номеров и команду этикетки. MAC и поиск по диапазону требуют короткого номера.");
        foreach (Match token in Regex.Matches(Command, @"\{\{(.*?)\}\}"))
            if (!(Layout != null && token.Groups[1].Value == "VisualLayoutV1") && !new[] { "Serial", "ShortSerial", "Mac", "Barcode", "Name", "Variant", "WidthDots", "BarcodeX", "TextX", "BarcodeType",
                    "SerialText", "DeviceCode", "VariantCode", "ServicePrefix", "ZplBarcode", "BarcodeModuleDots", "BarcodeTopDots", "BarcodeHeightDots", "FitNameWidth", "Cabinet", "CabinetField", "SerialTextWidth" }.Contains(token.Groups[1].Value))
                throw new InvalidOperationException("Неизвестная подстановка: " + token.Value);
        if (!IsSafeLabelText(Name) || VariantName != null && !IsSafeLabelText(VariantName) ||
            CabinetName != null && !IsSafeLabelText(CabinetName) ||
            BarcodePrefix.Any(c => !char.IsAsciiDigit(c)))
            throw new InvalidOperationException("Название не должно содержать команды принтера; префикс штрихкода — только цифры.");
    }
    internal static bool IsSafeLabelText(string text) => text.IndexOfAny(new[] { '^', '~', '"', '\r', '\n' }) < 0;
    public string Render(string serial, int shortSerial, string mac, int barcodeX = 0, int textX = 0)
    {
        Validate();
        var barcode = RenderBarcode(serial, shortSerial, mac);
        var serialText = RenderSerialText(serial, shortSerial, mac);
        var values = new Dictionary<string, string> {
            ["Serial"] = serial, ["ShortSerial"] = shortSerial.ToString("D" + ShortSerialDigits, CultureInfo.InvariantCulture),
            ["SerialText"] = serialText, ["DeviceCode"] = DeviceCode.ToString("D3", CultureInfo.InvariantCulture),
            ["VariantCode"] = VariantCode.ToString(CultureInfo.InvariantCulture), ["ServicePrefix"] = ServicePrefix,
            ["Mac"] = mac, ["Barcode"] = barcode, ["Name"] = Name,
            ["Variant"] = string.IsNullOrWhiteSpace(VariantName) ? "" : $"TYPE: {VariantName}",
            ["ZplBarcode"] = Code128Encoding.ZplData(barcode), ["BarcodeModuleDots"] = BarcodeModuleDots.ToString(CultureInfo.InvariantCulture),
            ["BarcodeTopDots"] = BarcodeTopDots.ToString(CultureInfo.InvariantCulture), ["BarcodeHeightDots"] = BarcodeHeightDots.ToString(CultureInfo.InvariantCulture),
            ["BarcodeType"] = Code128Encoding.CanUseC(barcode) ? "1C" : "1B",
            ["WidthDots"] = WidthDots.ToString(CultureInfo.InvariantCulture), ["BarcodeX"] = barcodeX.ToString(CultureInfo.InvariantCulture),
            ["TextX"] = textX.ToString(CultureInfo.InvariantCulture) };
        if (Command.Contains("{{FitNameWidth}}", StringComparison.Ordinal))
            values["FitNameWidth"] = FitNameWidth().ToString(CultureInfo.InvariantCulture);
        var (serialWidth, cabinetField) = CabinetLayout(serialText);
        values["Cabinet"] = CabinetName ?? "";
        values["CabinetField"] = cabinetField;
        values["SerialTextWidth"] = serialWidth.ToString(CultureInfo.InvariantCulture);
        return Regex.Replace(Command, @"\{\{(.*?)\}\}", m => values[m.Groups[1].Value]);
    }

    private (int SerialWidth, string CabinetField) CabinetLayout(string serial)
    {
        if (string.IsNullOrWhiteSpace(CabinetName) || !Command.Contains("{{CabinetField}}", StringComparison.Ordinal))
            return (20, "");
        var field = FittedZplSerial.Match(Command);
        if (Language != "ZPL" || !field.Success)
            throw new InvalidOperationException("Для подписи шкафа нужен ZPL: строка SN с шириной {{SerialTextWidth}} и поле {{CabinetField}}.");
        var serialX = int.Parse(field.Groups[1].Value, CultureInfo.InvariantCulture);
        var y = int.Parse(field.Groups[2].Value, CultureInfo.InvariantCulture);
        const int margin = 8;
        var cabinetX = OriginXDots + WidthDots - CabinetWidthDots;
        var serialWidth = Math.Min(20, (cabinetX - margin - serialX) / (serial.Length + 4));
        var cabinetWidth = Math.Min(12, (CabinetWidthDots - margin) / CabinetName.Length);
        if (serialX < OriginXDots || serialWidth < 10 || cabinetWidth < 7 ||
            HeightDots is { } height && y + CabinetFontHeightDots > height || y + CabinetFontHeightDots > BarcodeTopDots)
            throw new InvalidOperationException("Серийный номер и шкаф не помещаются. Сократите подпись шкафа или настройте ширину и высоту его поля в макете.");
        // ^FH UTF-8 bytes keep Cyrillic intact through the workstation's CP1251 transport.
        var hex = string.Concat(Encoding.UTF8.GetBytes(CabinetName).Select(b => "_" + b.ToString("X2", CultureInfo.InvariantCulture)));
        return (serialWidth, $"^CI28^FO{cabinetX},{y}^A0N,{CabinetFontHeightDots},{cabinetWidth}^FH^FD{hex}^FS^CI0");
    }

    internal int FitNameWidth()
    {
        var field = FittedZplName.Match(Command);
        if (Language != "ZPL" || !int.TryParse(field.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var x) || x > 32000)
            throw new InvalidOperationException("Для подгонки названия укажите координату X в ^FO и ширину {{FitNameWidth}} в ^A0.");
        var text = field.Groups[2].Value == "{{Name}}" ? Name : field.Groups[2].Value;
        if (text.Contains("{{", StringComparison.Ordinal))
            throw new InvalidOperationException("Подгонка названия поддерживает {{Name}} или постоянный текст.");

        const int rightMargin = 20;
        var standardWidth = NameFontMaxWidthDots ?? 25;
        const int minimumWidth = 10;
        var available = OriginXDots + WidthDots - Math.Max(x, OriginXDots) - rightMargin;
        // Older snapshots retain the original full-width budget; current stock uses its measured ratio.
        var width = Math.Min(standardWidth, (int)Math.Floor(available / (Math.Max(1, text.Length) * (NameCharacterWidthFactor ?? 1m))));
        if (width < minimumWidth)
            throw new InvalidOperationException($"Название «{text}» не помещается. Проверьте ширину этикетки или выберите другой макет.");
        return width;
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
        var original = template.Layout?.OriginalCommand ?? template.Command;
        var previousTemplate = previous.FirstOrDefault(t => t.Language == template.Language && t.Command == original &&
            (t.Id == template.Id || t.UseShortSerial && template.UseShortSerial));
        if (previousTemplate != null)
        {
            var current = Defaults().Single(t => t.Id == previousTemplate.Id);
            if (template.SerialTextFormat == null && template.BarcodeFormat == null && current.UseShortSerial)
            {
                template.SerialTextFormat = current.SerialTextFormat;
                template.BarcodeFormat = current.BarcodeFormat;
                if (template.Layout != null)
                {
                    foreach (var element in template.Layout.Elements.Where(e => e.Kind == "Text" && e.Value == "SN: {{Serial}}"))
                        element.Value = "SN: {{SerialText}}";
                    template.Layout.OriginalCommand = current.Command;
                }
            }
            if (template.Layout != null) return;
            template.Command = current.Command;
            var missingHeight = template.HeightDots == null;
            // Move only stock dimensions to the new default. Explicit custom sizes survive.
            if (template.Language == "ZPL")
            {
                if (template.WidthDots == previousTemplate.WidthDots || missingHeight && template.WidthDots == 295) template.WidthDots = current.WidthDots;
                if (template.HeightDots == previousTemplate.HeightDots) template.HeightDots = current.HeightDots;
                if (template.BarcodeModuleDots == previousTemplate.BarcodeModuleDots) template.BarcodeModuleDots = current.BarcodeModuleDots;
                if (template.BarcodeHeightDots == previousTemplate.BarcodeHeightDots) template.BarcodeHeightDots = current.BarcodeHeightDots;
                if (template.NameFontMaxWidthDots == previousTemplate.NameFontMaxWidthDots) template.NameFontMaxWidthDots = current.NameFontMaxWidthDots;
                if (template.NameCharacterWidthFactor == previousTemplate.NameCharacterWidthFactor) template.NameCharacterWidthFactor = current.NameCharacterWidthFactor;
            }
            if (template.Language == "ZPL" && missingHeight)
            {
                if (template.WidthDots == 354) template.WidthDots = current.WidthDots;
                template.HeightDots = current.HeightDots;
                if (template.OriginXDots == 0) template.OriginXDots = current.OriginXDots;
            }
            if (previousTemplate.OriginXDots != 0 && template.OriginXDots == previousTemplate.OriginXDots)
                template.OriginXDots = current.OriginXDots;
            if (template.Language == "ZPL" && (template.BarcodeTopDots == previousTemplate.BarcodeTopDots || missingHeight && template.BarcodeTopDots == 117))
                template.BarcodeTopDots = current.BarcodeTopDots;
        }
    }
    public static List<LabelTemplateDefinition> Defaults()
    {
        using var stream = typeof(LabelTemplateCatalog).Assembly.GetManifestResourceStream("TestBuilder.label-templates.json")!;
        return JsonSerializer.Deserialize<List<LabelTemplateDefinition>>(stream)!;
    }

    internal static List<LabelTemplateDefinition> ConfiguredTemplates() =>
        LabelTemplateStore.Load(AppSettings.Instance.ResolvedGraphsFolder, AppSettings.Instance.LabelTemplates);

    internal static List<LabelTemplateDefinition> WorkstationTemplates(out EquipmentCatalog? catalog)
    {
        var templates = ConfiguredTemplates();
        var file = LabelTypeStore.LoadFile(AppSettings.Instance.ResolvedGraphsFolder);
        var types = file.Types;
        try { catalog = EquipmentCatalog.Load(AppSettings.Instance.ResolvedGraphsFolder); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            catalog = null;
            Logging.DiagnosticLog.WriteException("Каталог моделей для этикеток", ex);
            return templates;
        }
        var stock = Legacy("PswUpsBox8x2Pro").CopyUpgraded();
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
        if (types.Count > 0 || file.Assignments.Count > 0)
        {
            for (var i = 0; i < templates.Count; i++)
            {
                var model = catalog.Resolve(null, templates[i].Name);
                var resolved = LabelTypeStore.Resolve(templates[i], model, model?.Name, file: file);
                resolved.Id = templates[i].Id;
                // Lookup identifies an actual product; constant display text is applied at printing.
                if (model != null) resolved.Name = model.Name;
                templates[i] = resolved;
            }
            foreach (var model in catalog.Models.Where(m => m.RtlReport != null && m.LabelCode.HasValue))
            {
                if (LabelTypeStore.AssignedTypeId(file, model.Id) == null && !types.Any(t => string.Equals(t.ProductModelId, model.Id, StringComparison.OrdinalIgnoreCase) || t.Id == LabelTypeStore.BoardTypeId)) continue;
                var resolved = LabelTypeStore.Resolve(LabelTypeStore.FamilyDefault(true), model, model.Name, file: file);
                resolved.Id = "catalog:" + model.Id;
                resolved.Name = model.Name;
                templates.RemoveAll(t => EquipmentCatalog.Normalize(t.Name) == EquipmentCatalog.Normalize(model.Name));
                templates.Add(resolved);
            }
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
    public static LabelTemplateDefinition Legacy(string id) => ConfiguredTemplates().FirstOrDefault(t => t.Id == id)?.Copy()
        ?? Defaults().FirstOrDefault(t => t.Id == id)
        ?? throw new InvalidOperationException($"Не найден шаблон этикетки {id}. Выберите шаблон в ноде.");
}
