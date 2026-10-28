using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace TestBuilder.Views;

/// <summary>Activity ring: graph loops and branches do not have a reliable percentage.</summary>
public sealed class ActionActivityIndicator : Control
{
    public static readonly StyledProperty<bool> IsActiveProperty = AvaloniaProperty.Register<ActionActivityIndicator, bool>(nameof(IsActive));
    public static readonly StyledProperty<IBrush> StrokeProperty = AvaloniaProperty.Register<ActionActivityIndicator, IBrush>(nameof(Stroke), Brushes.White);
    public static readonly StyledProperty<double?> ProgressProperty = AvaloniaProperty.Register<ActionActivityIndicator, double?>(nameof(Progress));
    public double? Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public IBrush Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    private readonly DispatcherTimer _timer;
    private double _angle;
    private bool _attached;
    public ActionActivityIndicator()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _timer.Tick += (_, _) => { _angle += .22; InvalidateVisual(); };
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _attached = true; UpdateTimer(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _attached = false; _timer.Stop(); base.OnDetachedFromVisualTree(e); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty || change.Property == IsVisibleProperty || change.Property == ProgressProperty) UpdateTimer();
        if (change.Property == IsActiveProperty || change.Property == StrokeProperty || change.Property == ProgressProperty) InvalidateVisual();
    }
    private void UpdateTimer() { if (_attached && IsActive && IsVisible && Progress == null) _timer.Start(); else _timer.Stop(); }
    public override void Render(DrawingContext context)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Math.Max(0, Math.Min(Bounds.Width, Bounds.Height) / 2 - 3);
        using (context.PushOpacity(.3)) context.DrawEllipse(null, new Pen(Stroke, 2), center, radius, radius);
        if (!IsActive && Progress is not > 0) return;
        var startAngle = Progress.HasValue ? -Math.PI / 2 : _angle;
        var sweep = Progress.HasValue ? Math.Clamp(Progress.Value, 0, 1) * 2 * Math.PI : Math.PI * 4 / 3;
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(new Point(center.X + radius * Math.Cos(startAngle), center.Y + radius * Math.Sin(startAngle)), false);
            for (var i = 1; i <= 40; i++) { var a = startAngle + i * sweep / 40; path.LineTo(new Point(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a))); }
            path.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(Stroke, 3, lineCap: PenLineCap.Round), geometry);
    }
}
