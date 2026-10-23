using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TestBuilder.Views;

/// <summary>Clips panel contents to the inside of a uniform rounded frame.</summary>
public sealed class RoundedPanel : Border
{
    private readonly RectangleGeometry _contentClip = new();

    protected override Type StyleKeyOverride => typeof(Border);

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        if (Child is { } child)
        {
            _contentClip.Rect = new Rect(child.Bounds.Size);
            _contentClip.RadiusX = Math.Max(0, CornerRadius.TopLeft - BorderThickness.Left - Padding.Left);
            _contentClip.RadiusY = Math.Max(0, CornerRadius.TopLeft - BorderThickness.Top - Padding.Top);
            child.Clip = _contentClip;
        }
        return size;
    }
}
