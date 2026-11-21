using System;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public sealed class LabelLayoutCanvas : LabelPreviewControl
{
    public LabelLayoutEditorViewModel Editor { get; }
    private bool _dragging, _resizing;
    private Point _start;
    private Rect _original;
    private int _originalModule;
    public LabelLayoutCanvas(LabelLayoutEditorViewModel editor)
    {
        Editor = editor; Focusable = true; Name = "LabelLayoutCanvas";
        editor.Changed += UpdatePreview;
        DetachedFromVisualTree += (_, _) => editor.Changed -= UpdatePreview;
        UpdatePreview();
    }
    private void UpdatePreview()
    {
        Preview = (Editor.Preview ?? new LabelPreview(Array.Empty<LabelPreviewElement>(), 0, 0, 1, 1, "")) with
        { Left = Editor.Template.OriginXDots, Width = Math.Max(1, Editor.Template.WidthDots), Height = Math.Max(1, Editor.Template.HeightDots ?? 150) };
        InvalidateVisual();
    }
    private double Scale => Preview == null ? 1 : PaperBounds(Preview).Width / Preview.Width;
    private Point ToDots(Point p)
    {
        var paper = PaperBounds(Preview!); return new Point((p.X - paper.X) / Scale, (p.Y - paper.Y) / Scale);
    }
    private Rect ToScreen(Rect r)
    {
        var paper = PaperBounds(Preview!); return new Rect(paper.X + r.X * Scale, paper.Y + r.Y * Scale, r.Width * Scale, r.Height * Scale);
    }
    internal Rect ScreenBounds(LabelLayoutElement element) => ToScreen(Editor.BoundsOf(element));
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Preview == null || Bounds.Width <= 4) return;
        foreach (var element in Editor.Elements.Where(e => e.Visible))
        {
            Rect rect;
            try { rect = ToScreen(Editor.BoundsOf(element)); } catch { continue; }
            var selected = element == Editor.Selected;
            context.DrawRectangle(null, new Pen(selected ? Brushes.DodgerBlue : Brushes.LightSlateGray, selected ? 2 : 1, DashStyle.Dash), rect);
            if (selected) context.FillRectangle(Brushes.DodgerBlue, new Rect(rect.Right - 5, rect.Bottom - 5, 10, 10));
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Preview == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus(); var p = e.GetPosition(this);
        if (Editor.Selected is { } current)
        {
            var r = ToScreen(Editor.BoundsOf(current));
            _resizing = new Rect(r.Right - 7, r.Bottom - 7, 14, 14).Contains(p);
        }
        if (!_resizing) Editor.Selected = Editor.Elements.Reverse().FirstOrDefault(element => element.Visible && Editor.BoundsOf(element).Contains(ToDots(p)));
        if (Editor.Selected == null) return;
        _start = ToDots(p); _original = Editor.BoundsOf(Editor.Selected); _originalModule = Editor.Selected.Module; _dragging = true; e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || Preview == null) return;
        var delta = ToDots(e.GetPosition(this)) - _start;
        if (_resizing && Editor.Selected is { } element)
        {
            element.Width = Math.Max(1, (int)Math.Round(_original.Width + delta.X));
            element.Height = Math.Max(1, (int)Math.Round(_original.Height + delta.Y));
            if (element.Kind == "Barcode")
            {
                var modules = _original.Width / _originalModule;
                element.Module = Math.Clamp((int)Math.Round(element.Width / Math.Max(1, modules)), 1, 10);
            }
            Editor.Selected = null; Editor.Selected = element; Editor.Refresh();
        }
        else Editor.Move(_original.X + delta.X, _original.Y + delta.Y);
        e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); _dragging = _resizing = false; e.Pointer.Capture(null); }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); _dragging = _resizing = false; }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Editor.Selected is not { } selected) return;
        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        switch (e.Key)
        {
            case Key.Left: Editor.Move(selected.X - step, selected.Y); break;
            case Key.Right: Editor.Move(selected.X + step, selected.Y); break;
            case Key.Up: Editor.Move(selected.X, selected.Y - step); break;
            case Key.Down: Editor.Move(selected.X, selected.Y + step); break;
            case Key.Delete: Editor.Remove(); break;
            default: return;
        }
        e.Handled = true;
    }
}
