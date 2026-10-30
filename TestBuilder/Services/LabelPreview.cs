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
        var parser = new Parser(template.WidthDots, template.PrinterDpi);
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

    private sealed class Parser(int width, int dpi)
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
        private void Barcode(double x, double y, double height, double module, string text, bool subsetC, bool readable, bool above = false, bool automatic = false)
        {
            var bars = automatic ? Code128Encoding.EncodeAuto(text) : Code128Encoding.Encode(text, subsetC);
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
            double blockWidth = 0;
            var justification = "L";
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
                    case "FB":
                        blockWidth = Number(a, 0); justification = Part(a, 3, "L");
                        if (Number(a, 1, 1) != 1 || Number(a, 4) != 0 || justification is not ("L" or "C" or "R"))
                        { _unsupported.Add("многострочный ^FB"); skip = true; }
                        break;
                    case "BC":
                        barcode = true;
                        if (Part(a, 0) is not ("" or "N")) { _unsupported.Add("поворот штрихкода"); skip = true; }
                        if (Part(a, 4) == "Y" || Part(a, 5) is not ("" or "N" or "A")) { _unsupported.Add("режим Code128"); skip = true; }
                        barH = Number(a, 1, barH); readable = Part(a, 2, "Y") != "N"; above = Part(a, 3) == "Y"; auto = Part(a, 5) == "A"; break;
                    case "FD":
                        if (skip) break;
                        if (!barcode)
                        {
                            var textWidth = value.Length * fontW * .65;
                            if (blockWidth > 0 && (textWidth > blockWidth || value.Contains('\\')))
                            { _unsupported.Add("переносы ^FB"); break; }
                            var offset = blockWidth > 0 ? justification switch { "C" => (blockWidth - textWidth) / 2, "R" => blockWidth - textWidth, _ => 0 } : 0;
                            Text(x + offset, y, fontH, fontW * .65, value);
                        }
                        else
                        {
                            var subsetC = value.StartsWith(">;", StringComparison.Ordinal);
                            var data = value.StartsWith(">:", StringComparison.Ordinal) || subsetC ? value[2..] : value;
                            if (data.Contains('>')) { _unsupported.Add("переключение Code128"); break; }
                            Barcode(x, y, barH, module, data, subsetC || (auto && Code128Encoding.CanUseC(data)), readable, above);
                        }
                        break;
                    case "GB": Add(new(x, y, Math.Max(1, Number(a, 0)), Math.Max(1, Number(a, 1)), Stroke: Math.Max(1, Number(a, 2, 1)))); break;
                    case "FS": barcode = skip = auto = false; blockWidth = 0; justification = "L"; fontH = defaultH; fontW = defaultW; break;
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
                // Orientation relative to the feed does not change the readable layout.
                if (line is "ZT" or "ZB") continue;
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
                    var size = EplFontMetrics.Size(font, dpi);
                    Text(x, y, size.Height * Number(a, 5, 1), size.Width * Number(a, 4, 1), data);
                }
                else
                {
                    var kind = Part(a, 3);
                    if (kind is not ("1" or "1B" or "1C")) { _unsupported.Add("штрихкод EPL " + kind); continue; }
                    Barcode(x, y, Number(a, 6, 50), Number(a, 4, 2), data,
                        kind == "1C", Part(a, 7) == "B", automatic: kind == "1");
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
