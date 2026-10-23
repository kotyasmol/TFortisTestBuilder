using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TestBuilder.Views.TestViews;

public sealed class GraphDotBackground : Control
{
    public static readonly StyledProperty<Point> ViewportLocationProperty =
        AvaloniaProperty.Register<GraphDotBackground, Point>(nameof(ViewportLocation));
    public static readonly StyledProperty<double> ViewportZoomProperty =
        AvaloniaProperty.Register<GraphDotBackground, double>(nameof(ViewportZoom), 1);
    public static readonly StyledProperty<IBrush?> DotBrushProperty =
        AvaloniaProperty.Register<GraphDotBackground, IBrush?>(nameof(DotBrush));

    static GraphDotBackground() =>
        AffectsRender<GraphDotBackground>(ViewportLocationProperty, ViewportZoomProperty, DotBrushProperty);

    public Point ViewportLocation
    {
        get => GetValue(ViewportLocationProperty);
        set => SetValue(ViewportLocationProperty, value);
    }

    public double ViewportZoom
    {
        get => GetValue(ViewportZoomProperty);
        set => SetValue(ViewportZoomProperty, value);
    }

    public IBrush? DotBrush
    {
        get => GetValue(DotBrushProperty);
        set => SetValue(DotBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var zoom = ViewportZoom;
        if (!double.IsFinite(zoom) || zoom <= 0 || DotBrush is null ||
            !double.IsFinite(ViewportLocation.X) || !double.IsFinite(ViewportLocation.Y))
            return;

        var spacing = 16 * zoom;
        // Thin out the same grid when zoomed out, keeping dots legible and rendering bounded.
        while (spacing < 12)
            spacing *= 2;
        var radius = Math.Clamp(zoom, 0.7, 1.5);
        var startX = ((-ViewportLocation.X * zoom % spacing) + spacing) % spacing;
        var startY = ((-ViewportLocation.Y * zoom % spacing) + spacing) % spacing;
        using (context.PushClip(new Rect(Bounds.Size)))
            for (var x = startX; x < Bounds.Width; x += spacing)
                for (var y = startY; y < Bounds.Height; y += spacing)
                    context.DrawEllipse(DotBrush, null, new Point(x, y), radius, radius);
    }
}
