using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TestBuilder.Services;

public sealed class LabelLayoutElement : System.ComponentModel.INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    private string _name = "Текст";
    public string Name { get => _name; set { _name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); } }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public string Kind { get; set; } = "Text";
    public bool Visible { get; set; } = true;
    public int X { get; set; } = 10;
    public int Y { get; set; } = 10;
    public int Width { get; set; } = 200;
    public int Height { get; set; } = 25;
    public int FontWidth { get; set; } = 20;
    public bool FitText { get; set; } = true;
    public string Alignment { get; set; } = "L";
    public int Module { get; set; } = 2;
    public int Stroke { get; set; } = 1;
    public string Value { get; set; } = "{{Serial}}";
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    public override string ToString() => Name;
}

public sealed class LabelLayoutDefinition
{
    public int Version { get; set; } = 1;
    public int Darkness { get; set; } = 10;
    public string? OriginalCommand { get; set; }
    public List<LabelLayoutElement> Elements { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    private static readonly string[] Tokens = { "Name", "Serial", "ShortSerial", "Mac", "Barcode", "Variant", "Cabinet" };

    public void Validate()
    {
        if (Version != 1 || Darkness is < -30 or > 30 || Elements == null || Elements.Count is < 1 or > 100 ||
            Elements.Any(e => e == null) || Elements.Select(e => e.Id).Distinct().Count() != Elements.Count)
            throw new InvalidOperationException("Некорректная версия макета, яркость или список элементов (1–100).");
        foreach (var e in Elements)
        {
            if (string.IsNullOrWhiteSpace(e.Id) || string.IsNullOrWhiteSpace(e.Name) || e.Kind is not ("Text" or "Barcode" or "Box") ||
                e.Alignment is not ("L" or "C" or "R") || e.X is < 0 or > 10000 || e.Y is < 0 or > 32000 ||
                e.Width is < 1 or > 10000 || e.Height is < 1 or > 32000 || e.FontWidth is < 1 or > 1000 ||
                e.Module is < 1 or > 10 || e.Stroke is < 1 or > 100 || e.Value == null || e.Value.Length > 1000 ||
                e.Value.Any(char.IsControl))
                throw new InvalidOperationException($"Элемент «{e.Name}»: проверьте координаты, размеры и текст.");
            foreach (Match token in Regex.Matches(e.Value, @"\{\{(.*?)\}\}"))
                if (!Tokens.Contains(token.Groups[1].Value))
                    throw new InvalidOperationException($"Элемент «{e.Name}»: неизвестное поле {token.Value}.");
        }
    }

    internal static string Resolve(LabelTemplateDefinition t, LabelLayoutElement e, string serial, int shortSerial, string mac)
    {
        var values = new Dictionary<string, string> { ["Name"] = t.Name, ["Serial"] = serial,
            ["ShortSerial"] = shortSerial.ToString("D" + t.ShortSerialDigits, CultureInfo.InvariantCulture),
            ["Mac"] = mac, ["Barcode"] = t.UseShortSerial ? t.Barcode(shortSerial) : serial,
            ["Variant"] = t.VariantName ?? "", ["Cabinet"] = t.CabinetName ?? "" };
        return Regex.Replace(e.Value, @"\{\{(.*?)\}\}", m => values.TryGetValue(m.Groups[1].Value, out var value) ? value : throw new InvalidOperationException("Неизвестное поле макета."));
    }

    internal string Render(LabelTemplateDefinition t, string serial, int shortSerial, string mac, bool validateBounds = true)
    {
        Validate();
        var zpl = t.Language == "ZPL";
        var b = new StringBuilder(zpl ? $"^XA^LH0,0^PW{t.OriginXDots + t.WidthDots}^LL{t.HeightDots ?? 150}^MD{Darkness}" : $"\r\nN\r\nZB\r\nq{t.OriginXDots + t.WidthDots}\r\nI8,C,001\r\n");
        foreach (var e in Elements.Where(e => e.Visible))
        {
            var text = Resolve(t, e, serial, shortSerial, mac);
            if (e.Kind == "Text" && text.Length == 0) continue;
            var x = t.OriginXDots + e.X;
            if (validateBounds && (e.X + e.Width > t.WidthDots && e.Kind != "Barcode" || t.HeightDots is { } h && e.Y + e.Height > h))
                throw new InvalidOperationException($"Элемент «{e.Name}» выходит за размер этикетки.");
            if (e.Kind == "Box")
            {
                b.Append(zpl ? $"^FO{x},{e.Y}^GB{e.Width},{e.Height},{e.Stroke}^FS" : $"X{x},{e.Y},{e.Stroke},{x + e.Width},{e.Y + e.Height}\r\n");
                continue;
            }
            if (text.Length == 0)
            {
                if (e.Kind == "Barcode") throw new InvalidOperationException($"Штрихкод «{e.Name}»: нет данных. Проверьте источник значения.");
                continue;
            }
            if (e.Kind == "Barcode")
            {
                // Invocation characters in user data must not be interpreted as ZPL subset commands.
                if (text.Length > 256 || text.Any(c => c is < ' ' or > '~') || text.IndexOfAny(new[] { '^', '~', '>', '"' }) >= 0)
                    throw new InvalidOperationException($"Штрихкод «{e.Name}»: используйте цифры или печатные латинские символы без команд принтера.");
                var bars = Code128Encoding.EncodeAuto(text) ?? throw new InvalidOperationException("Некорректные данные Code 128.");
                var width = bars.Sum(c => c - '0') * e.Module;
                if (validateBounds && (e.X < 10 * e.Module || e.X + width + 10 * e.Module > t.WidthDots))
                    throw new InvalidOperationException($"Штрихкод «{e.Name}» с полями не помещается. Ширина: {width + 20 * e.Module} точек. Уменьшите модуль или увеличьте этикетку.");
                b.Append(zpl ? $"^FO{x},{e.Y}^BY{e.Module}^BCN,{e.Height},N,N,N^FD{Code128Encoding.ZplData(text)}^FS"
                    : $"B{x},{e.Y},0,1,{e.Module},{e.Module},{e.Height},N,\"{text}\"\r\n");
                continue;
            }
            if (text.Length > 256) throw new InvalidOperationException($"Текст «{e.Name}» слишком длинный.");
            var fontWidth = e.FitText ? Math.Min(e.FontWidth, Math.Max(1, (int)(e.Width / (text.Length * .65)))) : e.FontWidth;
            if (text.Length * fontWidth * .65 > e.Width + 1)
                throw new InvalidOperationException($"Текст «{e.Name}» не помещается. Включите подгонку или увеличьте поле.");
            if (zpl)
            {
                var hex = string.Concat(Encoding.UTF8.GetBytes(text).Select(value => "_" + value.ToString("X2", CultureInfo.InvariantCulture)));
                b.Append($"^CI28^FO{x},{e.Y}^A0N,{e.Height},{fontWidth}^FB{e.Width},1,0,{e.Alignment},0^FH_^FD{hex}^FS^CI0");
            }
            else
            {
                if (text.Any(c => c is < ' ' or > '~') || text.Contains('"'))
                    throw new InvalidOperationException("Для кириллицы в тексте выберите ZPL.");
                // EPL uses discrete bitmap fonts. The selected size is explicit in the resulting command/preview.
                var fonts = Enumerable.Range(1, 5).Select(font => (Font: font, Size: EplFontMetrics.Size(font, t.PrinterDpi)))
                    .Where(f => f.Size.Height <= e.Height && f.Size.Width * text.Length <= e.Width).ToArray();
                if (fonts.Length == 0) throw new InvalidOperationException($"Текст «{e.Name}» не помещается в шрифт EPL. Увеличьте поле.");
                var chosen = fonts[^1];
                var offset = e.Alignment == "C" ? (e.Width - chosen.Size.Width * text.Length) / 2 : e.Alignment == "R" ? e.Width - chosen.Size.Width * text.Length : 0;
                b.Append($"A{x + offset},{e.Y},0,{chosen.Font},1,1,N,\"{text}\"\r\n");
            }
        }
        return b.Append(zpl ? "^XZ" : "P1,1\r\n").ToString();
    }

    public static LabelLayoutDefinition FromTemplate(LabelTemplateDefinition t)
    {
        if (t.Layout != null) return t.Copy().Layout!;
        var layout = new LabelLayoutDefinition { OriginalCommand = t.Command };
        // Read every supported field from the original command, preserving its data bindings.
        if (t.Language == "ZPL")
        {
            var darkness = Regex.Match(t.Command, @"\^MD(-?\d+)");
            if (darkness.Success) layout.Darkness = int.Parse(darkness.Groups[1].Value, CultureInfo.InvariantCulture);
            var matches = Regex.Matches(t.Command, @"\^FO([^,\^]+),([^\^]+)(.*?)\^FS", RegexOptions.Singleline);
            foreach (Match m in matches)
            {
                var body = m.Groups[3].Value;
                if (Regex.Matches(body.Split("^FD", 2)[0], @"\^([A-Za-z0-9]{2})").Cast<Match>()
                    .Any(op => op.Groups[1].Value is not ("A0" or "BY" or "BC" or "FB" or "GB")))
                    throw new InvalidOperationException("В поле есть неподдерживаемые команды. Используйте исходную команду принтера.");
                var barcode = body.Contains("^BC", StringComparison.Ordinal);
                var box = Regex.Match(body, @"\^GB(\d+),(\d+),(\d+)");
                var data = Regex.Match(body, @"\^FD(.*)$", RegexOptions.Singleline);
                var font = Regex.Match(body, @"\^A0N?,(\d+),([^\^]+)");
                if (!barcode && !box.Success && (!data.Success || !font.Success))
                    throw new InvalidOperationException("В команде есть неподдерживаемый элемент. Его можно изменить в разделе «Команда принтера».");
                var x = m.Groups[1].Value == "{{BarcodeX}}" ? 10 * t.BarcodeModuleDots + t.OriginXDots : int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var y = m.Groups[2].Value == "{{BarcodeTopDots}}" ? t.BarcodeTopDots : int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                var value = data.Value.Length == 0 ? "" : data.Groups[1].Value.Replace("{{ZplBarcode}}", "{{Barcode}}");
                if (barcode && (value.StartsWith(">;", StringComparison.Ordinal) || value.StartsWith(">:", StringComparison.Ordinal))) value = value[2..];
                var e = new LabelLayoutElement { X = Math.Max(0, x - t.OriginXDots), Y = y, Value = value,
                    Kind = barcode ? "Barcode" : box.Success ? "Box" : "Text", Name = barcode ? "Штрихкод" : box.Success ? "Рамка" : value.Contains("{{Name}}") ? "Название изделия" : value.Contains("{{Mac}}") ? "MAC" : value.Contains("{{Serial}}") ? "Серийный номер" : value,
                    Width = Math.Max(1, t.WidthDots - Math.Max(0, x - t.OriginXDots) - 8), Height = 25, Module = t.BarcodeModuleDots };
                if (barcode)
                {
                    var height = Regex.Match(body, @"\^BCN,([^,]+),");
                    e.Height = height.Groups[1].Value == "{{BarcodeHeightDots}}" ? t.BarcodeHeightDots : int.Parse(height.Groups[1].Value, CultureInfo.InvariantCulture);
                    var module = Regex.Match(body, @"\^BY(\d+)"); if (module.Success) e.Module = int.Parse(module.Groups[1].Value, CultureInfo.InvariantCulture);
                    var sample = t.UseShortSerial ? t.Barcode(42) : "12345678";
                    var width = Code128Encoding.EncodeAuto(sample)!.Sum(c => c - '0') * e.Module;
                    if (m.Groups[1].Value == "{{BarcodeX}}") e.X = Math.Max(10 * e.Module, (t.WidthDots - width) / 2);
                }
                else if (box.Success) { e.Width = int.Parse(box.Groups[1].Value); e.Height = int.Parse(box.Groups[2].Value); e.Stroke = int.Parse(box.Groups[3].Value); }
                else { e.Height = int.Parse(font.Groups[1].Value); e.FontWidth = int.TryParse(font.Groups[2].Value, out var fw) ? fw : font.Groups[2].Value == "{{SerialTextWidth}}" ? 20 : Math.Max(1, Math.Min(25, (e.Width - 12) / Math.Max(1, t.Name.Length))); }
                if (!barcode && !box.Success && body.Contains("^FB", StringComparison.Ordinal))
                {
                    var block = Regex.Match(body, @"\^FB(\d+),1,0,([LCR]),0");
                    if (!block.Success) throw new InvalidOperationException("Визуальный редактор поддерживает только однострочный текст ^FB.");
                    e.Width = int.Parse(block.Groups[1].Value); e.Alignment = block.Groups[2].Value;
                }
                layout.Elements.Add(e);
            }
            if (t.Command.Contains("{{CabinetField}}", StringComparison.Ordinal))
                layout.Elements.Add(new() { Name = "Шкаф", Value = "{{Cabinet}}", X = Math.Max(0, t.WidthDots - t.CabinetWidthDots),
                    Y = layout.Elements.FirstOrDefault(e => e.Value.Contains("{{Serial}}"))?.Y ?? 95,
                    Width = t.CabinetWidthDots, Height = t.CabinetFontHeightDots, FontWidth = 12 });
            var leftovers = Regex.Replace(t.Command, @"\^FO[^\^]+.*?\^FS", "", RegexOptions.Singleline).Replace("{{CabinetField}}", "");
            if (Regex.Matches(leftovers, @"[\^~]([A-Za-z0-9]{2})").Cast<Match>().Any(m => m.Groups[1].Value is not ("XA" or "XZ" or "MD" or "PW" or "LL" or "PQ")))
                throw new InvalidOperationException("Команда содержит неподдерживаемые настройки. Сохраните её в режиме команд или создайте отдельный визуальный макет.");
        }
        else
        {
            foreach (var line in t.Command.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var m = Regex.Match(line, "^([AB])([^,]+),(\\d+),0,(.*),\"(.*)\"$");
                if (!m.Success)
                {
                    if (Regex.IsMatch(line, @"^(N|Z[TB]|q[^,]+|I8,C,001|P1,1)$")) continue;
                    throw new InvalidOperationException("Для этой команды EPL визуальное редактирование не поддерживается. Используйте режим команд.");
                }
                var args = m.Groups[4].Value.Split(','); var barcode = m.Groups[1].Value == "B";
                var e = new LabelLayoutElement { Name = barcode ? "Штрихкод" : "Серийный номер", Kind = barcode ? "Barcode" : "Text",
                    X = int.TryParse(m.Groups[2].Value, out var x) ? Math.Max(0, x - t.OriginXDots) : 20,
                    Y = int.Parse(m.Groups[3].Value), Value = m.Groups[5].Value, Width = t.WidthDots - 40 };
                if (barcode) { e.Module = int.Parse(args[1]); e.Height = int.Parse(args[3]); }
                else { var size = EplFontMetrics.Size(int.Parse(args[0]), t.PrinterDpi); e.FontWidth = size.Width; e.Height = size.Height * int.Parse(args[2]); }
                if (m.Groups[2].Value == "{{BarcodeX}}") e.X = Math.Max(10 * e.Module, (t.WidthDots - Code128Encoding.EncodeAuto("12345678")!.Sum(c => c - '0') * e.Module) / 2);
                if (m.Groups[2].Value == "{{TextX}}") e.X = Math.Max(0, (t.WidthDots - e.FontWidth * 8) / 2);
                e.Width = Math.Max(1, t.WidthDots - e.X - 8);
                layout.Elements.Add(e);
            }
        }
        layout.Validate();
        return layout;
    }
}
