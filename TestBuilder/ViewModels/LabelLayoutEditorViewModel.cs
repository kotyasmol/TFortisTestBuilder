using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Services;

namespace TestBuilder.ViewModels;

public sealed record LabelElementKind(string Key, string Title) { public override string ToString() => Title; }
public sealed record LabelContentField(string Key, string Title) { public override string ToString() => Title; }

public partial class LabelLayoutEditorViewModel : ObservableObject
{
    public LabelTemplateDefinition Template { get; }
    public ObservableCollection<LabelLayoutElement> Elements { get; }
    public static LabelElementKind[] Kinds { get; } = { new("Text", "Текст"), new("Barcode", "Штрихкод Code 128"), new("Box", "Рамка / линия") };
    public static LabelElementKind[] Alignments { get; } = { new("L", "Слева"), new("C", "По центру"), new("R", "Справа") };
    public static LabelContentField[] ContentFields { get; } = {
        new("Name", "Название устройства"), new("Mac", "MAC"), new("SerialText", "SN по настройкам"),
        new("Barcode", "Штрихкод по настройкам"), new("Cabinet", "Шкаф"), new("Variant", "Комплектация"),
        new("Serial", "Полный серийный номер"), new("ShortSerial", "Короткий номер"), new("Custom", "Свой текст / формат") };
    private bool _customContentRequested;
    public bool IsCustomContent => HasValue && (_customContentRequested || ContentField?.Key == "Custom");
    public LabelContentField? ContentField
    {
        get => _customContentRequested ? ContentFields.Last() : ContentFields.FirstOrDefault(f => f.Key != "Custom" && Value == FieldValue(f.Key)) ?? ContentFields.Last();
        set
        {
            if (value == null || ContentField?.Key == value.Key) return;
            _customContentRequested = value.Key == "Custom";
            if (!_customContentRequested) Value = FieldValue(value.Key);
            OnPropertyChanged(); OnPropertyChanged(nameof(IsCustomContent));
        }
    }
    private string FieldValue(string field) => Selected?.Kind == "Barcode" ? "{{" + field + "}}" : field switch
    { "Mac" => "MAC: {{Mac}}", "SerialText" => "SN: {{SerialText}}", _ => "{{" + field + "}}" };
    public decimal? TopMargin
    {
        get => Elements.Count > 0 ? Mm(Elements.Min(e => e.Y)) : null;
        set { if (Elements.Count > 0 && value.HasValue && TopMargin != value) { var delta = Dots(value) - Elements.Min(e => e.Y); foreach (var e in Elements) e.Y += delta; RefreshFields(); } }
    }
    private LabelLayoutElement? FirstBarcode => Elements.FirstOrDefault(e => e.Kind == "Barcode" && e.Visible);
    public bool HasBarcode => FirstBarcode != null;
    public decimal? BarcodeHeight { get => FirstBarcode is { } bar ? Mm(bar.Height) : null; set { if (FirstBarcode is { } bar && value.HasValue && BarcodeHeight != value) { bar.Height = Dots(value); RefreshFields(); } } }
    public decimal? BarcodeModule { get => FirstBarcode?.Module; set { if (FirstBarcode is { } bar && value.HasValue && value == decimal.Truncate(value.Value) && BarcodeModule != value) { bar.Module = checked((int)value.Value); RefreshFields(); } } }
    public bool HasSelection => Selected != null;
    public bool HasTextOptions => Selected?.Kind == "Text";
    public bool HasBarcodeOptions => Selected?.Kind == "Barcode";
    public bool HasBoxOptions => Selected?.Kind == "Box";
    public bool HasValue => Selected != null && !HasBoxOptions;
    public LabelElementKind? SelectedAlignment { get => Alignments.FirstOrDefault(a => a.Key == Alignment); set { if (value != null) Alignment = value.Key; } }
    [ObservableProperty] private LabelLayoutElement? selected;
    [ObservableProperty] private string serial;
    [ObservableProperty] private string mac = "C0:11:A6:00:00:01";
    [ObservableProperty] private string cabinet;
    [ObservableProperty] private string scannedCode = "";
    public LabelPreview? Preview { get; private set; }
    public string Diagnostics { get; private set; } = "";
    public string Command { get; private set; } = "";
    public event Action? Changed;

    public LabelLayoutEditorViewModel(LabelTemplateDefinition source)
    {
        Template = source.CopyUpgraded();
        Template.Layout = LabelLayoutDefinition.FromTemplate(Template);
        Template.HeightDots ??= Math.Max(150, Template.Layout.Elements.Max(e => e.Y + e.Height) + 10);
        Elements = new(Template.Layout.Elements);
        serial = (Template.UseShortSerial ? Math.Max(Template.FirstSerialNumber ?? Template.SerialOffset, Template.SerialOffset + Math.Min(42, Template.ShortSerialMax)) : 12345678).ToString(CultureInfo.InvariantCulture);
        cabinet = Template.CabinetName ?? "";
        Selected = Elements.FirstOrDefault(); Refresh();
    }
    [ObservableProperty] private bool allowCanvasEditing;

    private decimal Mm(int dots) => Math.Round(dots * 25.4m / Template.PrinterDpi, 3);
    private int Dots(decimal? mm) => mm.HasValue ? checked((int)Math.Round(mm.Value * Template.PrinterDpi / 25.4m, MidpointRounding.AwayFromZero)) : throw new InvalidOperationException("Введите размер.");
    public decimal? PaperWidth { get => Mm(Template.WidthDots); set { Template.WidthDots = Dots(value); Refresh(); } }
    public decimal? PaperHeight { get => Mm(Template.HeightDots ?? 150); set { Template.HeightDots = Dots(value); Refresh(); } }
    public decimal? Origin { get => Mm(Template.OriginXDots); set { Template.OriginXDots = Dots(value); Refresh(); } }
    public decimal? PrintAreaWidth { get => Mm(Template.PrintAreaWidthDots ?? 0); set { var dots = Dots(value); Template.PrintAreaWidthDots = dots == 0 ? null : dots; Refresh(); } }
    public int Dpi
    {
        get => Template.PrinterDpi;
        set
        {
            if (value == Template.PrinterDpi || value is not (203 or 300)) return;
            var ratio = value / (double)Template.PrinterDpi;
            int Scale(int n) => Math.Max(1, (int)Math.Round(n * ratio));
            Template.WidthDots = Scale(Template.WidthDots); Template.HeightDots = Scale(Template.HeightDots ?? 150);
            Template.OriginXDots = (int)Math.Round(Template.OriginXDots * ratio);
            if (Template.PrintAreaWidthDots is { } area) Template.PrintAreaWidthDots = (int)Math.Round(area * ratio);
            foreach (var e in Elements) { e.X = (int)Math.Round(e.X * ratio); e.Y = (int)Math.Round(e.Y * ratio); e.Width = Scale(e.Width); e.Height = Scale(e.Height); e.FontWidth = Scale(e.FontWidth); e.Module = Math.Clamp(Scale(e.Module), 1, 10); }
            Template.PrinterDpi = value; NotifyFields(); Refresh();
        }
    }
    public string Language { get => Template.Language; set { Template.Language = value; Refresh(); } }
    public decimal? Darkness { get => Template.Layout!.Darkness; set { Template.Layout!.Darkness = checked((int)(value ?? throw new InvalidOperationException("Введите яркость."))); Refresh(); } }
    public string ElementName { get => Selected?.Name ?? ""; set { if (Selected != null) Selected.Name = value; Refresh(); } }
    public string Value { get => Selected?.Value ?? ""; set { if (Selected != null) Selected.Value = value; OnPropertyChanged(); OnPropertyChanged(nameof(ContentField)); OnPropertyChanged(nameof(IsCustomContent)); Refresh(); } }
    public LabelElementKind? Kind { get => Kinds.FirstOrDefault(k => k.Key == Selected?.Kind); set { if (Selected != null && value != null) { Selected.Kind = value.Key; if (value.Key == "Box") Selected.Value = ""; } NotifyFields(); Refresh(); } }
    public bool Visible { get => Selected?.Visible == true; set { if (Selected != null) Selected.Visible = value; Refresh(); } }
    public bool FitText { get => Selected?.FitText == true; set { if (Selected != null) Selected.FitText = value; Refresh(); } }
    public string Alignment { get => Selected?.Alignment ?? "L"; set { if (Selected != null) Selected.Alignment = value; Refresh(); } }
    private decimal? Read(Func<LabelLayoutElement, int> read) => Selected == null ? null : Mm(read(Selected));
    private void Write(decimal? value, Action<LabelLayoutElement, int> write) { if (Selected != null) write(Selected, Dots(value)); Refresh(); }
    public decimal? X { get => Read(e => e.X); set => Write(value, (e, n) => e.X = n); }
    public decimal? Y { get => Read(e => e.Y); set => Write(value, (e, n) => e.Y = n); }
    public decimal? Width
    {
        get => Selected?.Kind == "Barcode" ? Mm((int)BoundsOf(Selected).Width) : Read(e => e.Width);
        set
        {
            if (Selected is { Kind: "Barcode" } e)
            {
                var modules = BoundsOf(e).Width / e.Module;
                e.Module = Math.Clamp((int)Math.Round(Dots(value) / Math.Max(1, modules)), 1, 10);
                OnPropertyChanged(nameof(Module)); OnPropertyChanged(); Refresh();
            }
            else Write(value, (e, n) => e.Width = n);
        }
    }
    public decimal? Height { get => Read(e => e.Height); set => Write(value, (e, n) => e.Height = n); }
    public decimal? FontWidth { get => Read(e => e.FontWidth); set => Write(value, (e, n) => e.FontWidth = n); }
    public decimal? Module { get => Selected?.Module; set { if (value != decimal.Truncate(value ?? 0)) throw new InvalidOperationException("Модуль — целое число точек."); if (Selected != null) Selected.Module = checked((int)(value ?? throw new InvalidOperationException("Введите модуль."))); OnPropertyChanged(nameof(Width)); Refresh(); } }
    public decimal? Stroke { get => Selected?.Stroke; set { if (Selected != null) Selected.Stroke = checked((int)(value ?? throw new InvalidOperationException("Введите толщину."))); Refresh(); } }
    partial void OnSelectedChanged(LabelLayoutElement? value) { _customContentRequested = false; NotifyFields(); OnPropertyChanged(nameof(IsCustomContent)); Changed?.Invoke(); }
    partial void OnSerialChanged(string value) => Refresh();
    partial void OnMacChanged(string value) => Refresh();
    partial void OnCabinetChanged(string value) => Refresh();
    partial void OnScannedCodeChanged(string value) => Refresh();
    private void NotifyFields()
    {
        foreach (var property in new[] { nameof(ElementName), nameof(Value), nameof(ContentField), nameof(IsCustomContent), nameof(Kind), nameof(Visible), nameof(FitText), nameof(Alignment), nameof(SelectedAlignment), nameof(X), nameof(Y), nameof(Width), nameof(Height), nameof(FontWidth), nameof(Module), nameof(Stroke), nameof(PaperWidth), nameof(PaperHeight), nameof(Origin), nameof(PrintAreaWidth), nameof(Dpi), nameof(HasSelection), nameof(HasTextOptions), nameof(HasBarcodeOptions), nameof(HasBoxOptions), nameof(HasValue) }) OnPropertyChanged(property);
    }
    public void Add(string kind)
    {
        var e = new LabelLayoutElement { Kind = kind, Name = kind == "Barcode" ? "Штрихкод" : kind == "Box" ? "Рамка" : "Текст",
            X = 20, Y = 10, Width = Math.Max(1, Math.Min(200, Template.WidthDots - 40)), Height = kind == "Barcode" ? 40 : 25,
            Value = kind == "Barcode" ? "{{Barcode}}" : "{{Serial}}" };
        Elements.Add(e); Selected = e; Refresh();
    }
    public void Remove() { if (Selected == null) return; var index = Elements.IndexOf(Selected); Elements.Remove(Selected); Selected = Elements.ElementAtOrDefault(Math.Min(index, Elements.Count - 1)); Refresh(); }
    public void CopySelected()
    {
        if (Selected == null) return;
        var copy = JsonSerializer.Deserialize<LabelLayoutElement>(JsonSerializer.Serialize(Selected))!;
        copy.Id = Guid.NewGuid().ToString("N"); copy.Name += " (копия)"; copy.Y += 5; Elements.Add(copy); Selected = copy; Refresh();
    }
    public void Move(double x, double y)
    {
        if (Selected == null) return;
        var bounds = BoundsOf(Selected);
        var quiet = Selected.Kind == "Barcode" ? 10 * Selected.Module : 0;
        Selected.X = Math.Clamp((int)Math.Round(x), quiet, Math.Max(quiet, Template.WidthDots - (int)Math.Ceiling(bounds.Width) - quiet));
        Selected.Y = Math.Clamp((int)Math.Round(y), 0, Math.Max(0, (Template.HeightDots ?? 150) - Selected.Height)); NotifyFields(); Refresh();
    }
    public void Center()
    {
        if (Selected == null) return;
        var bounds = BoundsOf(Selected); Selected.X = Math.Max(0, (int)((Template.WidthDots - bounds.Width) / 2)); NotifyFields(); Refresh();
    }
    public void RefreshFields() { NotifyFields(); Refresh(); }
    public Rect BoundsOf(LabelLayoutElement e)
    {
        double width = e.Width;
        if (e.Kind == "Barcode")
        {
            try
            {
                var input = ResolveSerial();
                var copy = Template.Copy(); copy.CabinetName = Cabinet;
                var value = LabelLayoutDefinition.Resolve(copy, e, input.Full, input.Short, NormalizedMac());
                width = (Code128Encoding.EncodeAuto(value)?.Sum(c => c - '0') ?? 50) * e.Module;
            }
            catch (InvalidOperationException) { /* Keep invalid fields movable while the diagnostic explains the error. */ }
        }
        return new Rect(e.X, e.Y, Math.Max(1, width), e.Height);
    }
    private (string Full, int Short) ResolveSerial()
    {
        var raw = Serial.Trim();
        if (!Template.UseShortSerial) return (raw, 0);
        return LabelPrintContent.TryResolveSerial(Template, raw, out var full, out var shortSerial, out _) ? (full.ToString(CultureInfo.InvariantCulture), shortSerial) : (raw, 0);
    }
    private string NormalizedMac() => Template.RequiresMac && LabelPrintContent.TryNormalizeMac(Mac, out var value) ? value : "";
    public void Refresh()
    {
        if (Elements == null) return;
        Template.Layout!.Elements = Elements.ToList();
        var sample = Template.Copy(); sample.CabinetName = string.IsNullOrWhiteSpace(Cabinet) ? null : Cabinet;
        Preview = LabelPreview.FromInput(sample, Serial, Mac);
        Command = "";
        var input = ResolveSerial();
        var diagnostics = $"Полный серийный номер: {input.Full}\nКороткий номер: {(Template.UseShortSerial ? input.Short.ToString("D" + Template.ShortSerialDigits) : "—")}\n";
        try
        {
            if (Preview.HasContent && !Preview.IsSample)
            {
                Command = LabelPrintContent.Render(sample, input.Full, input.Short, NormalizedMac());
            }
            foreach (var e in Elements.Where(e => e.Visible && e.Kind == "Barcode"))
            {
                var data = LabelLayoutDefinition.Resolve(sample, e, input.Full, input.Short, NormalizedMac());
                var bounds = BoundsOf(e); var quiet = 10 * e.Module;
                diagnostics += $"{e.Name}: «{data}» · {data.Length} символов\nПолоса: {e.Module} точ. ({Mm(e.Module):0.###} мм); ширина: {Mm((int)bounds.Width):0.###} мм; высота: {Mm(e.Height):0.###} мм; поля с каждой стороны: {Mm(quiet):0.###} мм.\n";
                if (!string.IsNullOrWhiteSpace(ScannedCode)) diagnostics += ScannedCode.Trim() == data ? "Считанный сканером код совпадает.\n" : $"Считанный код «{ScannedCode.Trim()}» не совпадает с данными этого штрихкода.\n";
                if (e.Module == 1) diagnostics += "Узкая полоса в одну точку: проверьте считывание на реальном принтере.\n";
                if (Mm(e.Height) < 5) diagnostics += "Небольшая высота штрихкода: при нестабильном считывании увеличьте её.\n";
                var protectedArea = new Rect(bounds.X - quiet, bounds.Y, bounds.Width + 2 * quiet, bounds.Height);
                if (Elements.Any(other => other != e && other.Visible && LabelLayoutDefinition.Resolve(sample, other, input.Full, input.Short, Mac).Length > 0 && protectedArea.Intersects(BoundsOf(other))))
                    diagnostics += "Проверьте пересечение полей элементов со штрихкодом: шрифты в предпросмотре приблизительные.\n";
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OverflowException) { diagnostics += ex.Message + "\n"; }
        Diagnostics = diagnostics + Preview.Message;
        foreach (var property in new[] { nameof(TopMargin), nameof(BarcodeHeight), nameof(BarcodeModule), nameof(HasBarcode) }) OnPropertyChanged(property);
        OnPropertyChanged(nameof(Preview)); OnPropertyChanged(nameof(Diagnostics)); OnPropertyChanged(nameof(Command)); Changed?.Invoke();
    }
    public LabelTemplateDefinition Apply()
    {
        Template.CommitDataFormats();
        Refresh(); Template.Validate();
        if (Preview is not { HasContent: true } || Preview.BarcodeError != null || string.IsNullOrEmpty(Command))
            throw new InvalidOperationException(Preview?.Message ?? "Проверьте данные макета.");
        return Template.Copy();
    }
}
