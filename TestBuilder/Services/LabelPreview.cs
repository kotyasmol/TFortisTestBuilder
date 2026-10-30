using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestBuilder.Services;

public sealed record LabelPreviewElement(double X, double Y, double Width, double Height,
    string Text = "", string Bars = "", double Stroke = 0);

/// <summary>A bounded, offline approximation of the rendered printer command, never a print job.</summary>
public sealed record LabelPreview(IReadOnlyList<LabelPreviewElement> Elements, double Left, double Top,
    double Width, double Height, string Message, bool IsSample = false)
{
    public bool HasContent => Elements.Count > 0;
    public bool HasMessage => Message.Length > 0;

    public static LabelPreview FromInput(LabelTemplateDefinition template, string serial, string mac)
    {
        try
        {
            template.Validate();
            serial = serial.Trim(); mac = mac.Trim();
            var sample = serial.Length == 0 || (template.RequiresMac && mac.Length == 0);
            if (serial.Length == 0) serial = template.UseShortSerial ? Math.Min(42, template.ShortSerialMax).ToString(CultureInfo.InvariantCulture) : "12345678";
            if (serial.Length > 256 || serial.Any(c => !char.IsAsciiDigit(c)))
                return Empty("Серийный номер должен содержать только цифры 0–9 (не более 256).");
            var shortSerial = 0;
            if (template.UseShortSerial)
            {
                if (!LabelPrintContent.TryResolveSerial(template, serial, out var full, out shortSerial, out var error)) return Empty(error);
                serial = full.ToString(CultureInfo.InvariantCulture);
            }
            if (template.RequiresMac)
            {
                if (mac.Length == 0) mac = "C0:11:A6:00:00:01";
                if (!LabelPrintContent.TryNormalizeMac(mac, out mac)) return Empty("Введите корректный MAC-адрес для предпросмотра.");
            }
            else mac = "";
            var preview = Parse(template, LabelPrintContent.Render(template, serial, shortSerial, mac));
            return preview with { IsSample = sample, Message = (sample ? "Незаполненные поля показаны на примере. " : "") + preview.Message };
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OverflowException)
        { return Empty(ex.Message); }
    }

    private static LabelPreview Empty(string message) => new(Array.Empty<LabelPreviewElement>(), 0, 0, 354, 150, message);

    public static LabelPreview Parse(LabelTemplateDefinition template, string command)
    {
        if (command.Length > 100000) return Empty("Шаблон слишком большой для предпросмотра.");
        var parser = new Parser(template.WidthDots);
        try
        {
            if (template.Language == "ZPL") parser.Zpl(command);
            else if (template.Language == "EPL") parser.Epl(command);
            else return Empty("Предпросмотр поддерживает ZPL и EPL.");
            return parser.Build();
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        { return Empty("Не удалось разобрать команду шаблона для предпросмотра."); }
    }

    private sealed class Parser(int width)
    {
        private readonly List<LabelPreviewElement> _elements = new();
        private readonly HashSet<string> _unsupported = new();
        private double _width = width, _height;
        private bool _explicitWidth;
        private static double Number(string[] args, int i, double fallback = 0)
        {
            if (i >= args.Length || string.IsNullOrWhiteSpace(args[i])) return fallback;
            if (!double.TryParse(args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || Math.Abs(n) > 32000)
                throw new FormatException();
            return n;
        }
        private static string Part(string[] args, int i, string fallback = "") => i < args.Length ? args[i] : fallback;
        private void Add(LabelPreviewElement element)
        {
            if (_elements.Count >= 500) { _unsupported.Add("слишком много элементов"); return; }
            if (element.Width <= 0 || element.Height <= 0 || element.Width > 32000 || element.Height > 32000)
                throw new FormatException();
            _elements.Add(element);
        }
        private void Text(double x, double y, double height, double charWidth, string text)
        {
            if (text.Length == 0) return;
            if (text.Length > 256) { _unsupported.Add("длинный текст"); return; }
            Add(new(x, y, Math.Max(charWidth, text.Length * charWidth), height, text));
        }
        private void Barcode(double x, double y, double height, double module, string text, bool subsetC, bool readable, bool above = false)
        {
            var bars = Code128Preview.Encode(text, subsetC);
            if (bars == null) { _unsupported.Add("содержимое Code128"); return; }
            var w = bars.Sum(c => c - '0') * module;
            Add(new(x, y, w, height, Bars: bars));
            if (readable) Text(x, above ? y - 23 : y + height + 3, 20, Math.Min(12, w / Math.Max(1, text.Length)), text);
        }

        // Syntax: Zebra ZPL Programming Guide, ^FO/^A/^BC/^BY/^GB. Unsupported visual
        // commands are reported; a barcode in another symbology is never drawn as Code128.
        public void Zpl(string command)
        {
            double x = 0, y = 0, homeX = 0, homeY = 0, fontH = 30, fontW = 18, defaultH = 30, defaultW = 18, module = 2, barH = 50;
            var barcode = false; var readable = true; var above = false; var skip = false; var auto = false;
            var matches = Regex.Matches(command, @"[\^~]([A-Za-z0-9@]{2})([^\^~]*)");
            if (matches.Count == 0) _unsupported.Add("формат ZPL");
            foreach (Match match in matches)
            {
                var op = match.Groups[1].Value; var value = match.Groups[2].Value; var a = value.Trim().Split(',');
                switch (op)
                {
                    case "XA": break;
                    case "XZ": return; // Preview one label, regardless of requested copies.
                    case "FO": x = Number(a, 0) + homeX; y = Number(a, 1) + homeY; break;
                    case "LH": homeX = Number(a, 0); homeY = Number(a, 1); break;
                    case "PW": _width = Number(a, 0); _explicitWidth = true; break;
                    case "LL": _height = Number(a, 0); break;
                    case "CF": defaultH = fontH = Number(a, 1, 30); defaultW = fontW = Number(a, 2, fontH * .6); break;
                    case "A0":
                        if (Part(a, 0) is not ("" or "N")) { _unsupported.Add("поворот текста"); skip = true; }
                        fontH = Number(a, 1, defaultH); fontW = Number(a, 2, fontH * .6); break;
                    case "BY": module = Number(a, 0, 2); barH = Number(a, 2, 50); break;
                    case "BC":
                        barcode = true;
                        if (Part(a, 0) is not ("" or "N")) { _unsupported.Add("поворот штрихкода"); skip = true; }
                        if (Part(a, 4) == "Y" || Part(a, 5) is not ("" or "N" or "A")) { _unsupported.Add("режим Code128"); skip = true; }
                        barH = Number(a, 1, barH); readable = Part(a, 2, "Y") != "N"; above = Part(a, 3) == "Y"; auto = Part(a, 5) == "A"; break;
                    case "FD":
                        if (skip) break;
                        if (!barcode) Text(x, y, fontH, fontW * .65, value);
                        else
                        {
                            var subsetC = value.StartsWith(">;", StringComparison.Ordinal);
                            var data = value.StartsWith(">:", StringComparison.Ordinal) || subsetC ? value[2..] : value;
                            if (data.Contains('>')) { _unsupported.Add("переключение Code128"); break; }
                            Barcode(x, y, barH, module, data, subsetC || (auto && Code128Preview.CanUseC(data)), readable, above);
                        }
                        break;
                    case "GB": Add(new(x, y, Math.Max(1, Number(a, 0)), Math.Max(1, Number(a, 1)), Stroke: Math.Max(1, Number(a, 2, 1)))); break;
                    case "FS": barcode = skip = auto = false; fontH = defaultH; fontW = defaultW; break;
                    case "MD": case "PR": case "PQ": case "CI": case "FX": break; // Nonvisual settings/comment.
                    default: _unsupported.Add("^" + op); skip = true; break;
                }
            }
        }

        public void Epl(string command)
        {
            foreach (var line in command.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                var op = line[0]; var a = line[1..].Split(',');
                if (op == 'q') { _width = Number(a, 0); _explicitWidth = true; continue; }
                if (op == 'Q') { _height = Number(a, 0); continue; }
                if (op == 'P') break;
                if (op is 'N' or 'I' or 'S' or 'D') continue;
                if (op is not ('A' or 'B')) { _unsupported.Add(line[..Math.Min(2, line.Length)]); continue; }
                var field = Regex.Match(line, "^[AB]([^\"]*)\"([^\"]*)\"$");
                if (!field.Success) { _unsupported.Add("поле EPL"); continue; }
                a = field.Groups[1].Value.TrimEnd(',').Split(','); var data = field.Groups[2].Value;
                var x = Number(a, 0); var y = Number(a, 1);
                if (Number(a, 2) != 0) { _unsupported.Add("поворот EPL"); continue; }
                if (op == 'A')
                {
                    var font = (int)Number(a, 3, 3);
                    if (font is < 1 or > 5 || Part(a, 6) != "N") { _unsupported.Add("шрифт EPL"); continue; }
                    var widths = new[] { 8, 10, 12, 14, 32 }; var heights = new[] { 12, 16, 20, 24, 48 };
                    Text(x, y, heights[font - 1] * Number(a, 5, 1), widths[font - 1] * Number(a, 4, 1), data);
                }
                else
                {
                    var kind = Part(a, 3);
                    if (kind is not ("1" or "1B" or "1C")) { _unsupported.Add("штрихкод EPL " + kind); continue; }
                    Barcode(x, y, Number(a, 6, 50), Number(a, 4, 2), data,
                        kind == "1C" || (kind == "1" && Code128Preview.CanUseC(data)), Part(a, 7) == "B");
                }
            }
        }

        public LabelPreview Build()
        {
            var message = _unsupported.Count == 0 ? "" : "Не показаны команды: " + string.Join(", ", _unsupported.Take(8)) + ".";
            if (_elements.Count == 0) return Empty("Нет элементов для предпросмотра. " + message);
            // Old templates include a print-head offset (~500 dots). Fit content when the
            // command does not declare a physical page; preserve spacing between fields.
            var left = _explicitWidth ? 0 : _elements.Min(e => e.X) - 12;
            var top = _height > 0 ? 0 : _elements.Min(e => e.Y) - 12;
            var w = _explicitWidth ? _width : Math.Max(_width, _elements.Max(e => e.X + e.Width) - left + 12);
            var h = _height > 0 ? _height : _elements.Max(e => e.Y + e.Height) - top + 12;
            if (w <= 0 || h <= 0) throw new FormatException();
            if (_elements.Any(e => e.X < left || e.Y < top || e.X + e.Width > left + w || e.Y + e.Height > top + h))
                message += " Часть содержимого выходит за размер этикетки.";
            return new(_elements.ToArray(), left, top, w, h, message.Trim());
        }
    }
}

internal static class Code128Preview
{
    // Code128 symbol bar/space widths, indexed by symbol value (0..106, stop).
    private static readonly string[] Patterns = ("212222 222122 222221 121223 121322 131222 122213 122312 132212 221213 221312 231212 " +
        "112232 122132 122231 113222 123122 123221 223211 221132 221231 213212 223112 312131 311222 321122 321221 " +
        "312212 322112 322211 212123 212321 232121 111323 131123 131321 112313 132113 132311 211313 231113 231311 " +
        "112133 112331 132131 113123 113321 133121 313121 211331 231131 213113 213311 213131 311123 311321 331121 " +
        "312113 312311 332111 314111 221411 431111 111224 111422 121124 121421 141122 141221 112214 112412 122114 " +
        "122411 142112 142211 241211 221114 413111 241112 134111 111242 121142 121241 114212 124112 124211 " +
        "411212 421112 421211 212141 214121 412121 111143 111341 131141 114113 114311 411113 411311 113141 " +
        "114131 311141 411131 211412 211214 211232 2331112").Split(' ');
    public static bool CanUseC(string text) => text.Length > 0 && text.Length % 2 == 0 && text.All(char.IsAsciiDigit);
    public static string? Encode(string text, bool subsetC)
    {
        if (text.Length is 0 or > 256 || text.Any(c => c is < ' ' or > '~') || (subsetC && !CanUseC(text))) return null;
        var codes = new List<int> { subsetC ? 105 : 104 };
        if (subsetC)
            for (var i = 0; i < text.Length; i += 2) codes.Add((text[i] - '0') * 10 + text[i + 1] - '0');
        else codes.AddRange(text.Select(c => c - 32));
        var checksum = codes[0];
        for (var i = 1; i < codes.Count; i++) checksum += i * codes[i];
        codes.Add(checksum % 103); codes.Add(106);
        return string.Concat(codes.Select(c => Patterns[c]));
    }
}
