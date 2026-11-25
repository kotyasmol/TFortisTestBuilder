using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace TestBuilder.Views;

/// <summary>Equal-width fields with responsive columns and natural row heights.</summary>
internal sealed class LabelFieldGrid : Panel
{
    public int MaxColumns { get; init; } = 4;
    public double MinColumnWidth { get; init; } = 150;
    private const double Gap = 10;
    private int Columns(double width) => Math.Clamp((int)Math.Floor((width + Gap) / (MinColumnWidth + Gap)), 1, MaxColumns);
    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? MinColumnWidth * MaxColumns + Gap * (MaxColumns - 1) : available.Width;
        var columns = Columns(width); var cellWidth = Math.Max(0, (width - Gap * (columns - 1)) / columns);
        var children = Children.Where(c => c.IsVisible).ToArray();
        foreach (var child in children) child.Measure(new Size(cellWidth, double.PositiveInfinity));
        var height = 0d;
        for (var row = 0; row < children.Length; row += columns)
            height += children.Skip(row).Take(columns).Max(c => c.DesiredSize.Height) + (row == 0 ? 0 : Gap);
        return new Size(width, height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = Children.Where(c => c.IsVisible).ToArray(); var columns = Columns(finalSize.Width);
        var cellWidth = Math.Max(0, (finalSize.Width - Gap * (columns - 1)) / columns); var y = 0d;
        for (var row = 0; row < children.Length; row += columns)
        {
            var height = children.Skip(row).Take(columns).Max(c => c.DesiredSize.Height);
            for (var col = 0; col < columns && row + col < children.Length; col++)
                children[row + col].Arrange(new Rect(col * (cellWidth + Gap), y, cellWidth, height));
            y += height + Gap;
        }
        return finalSize;
    }
}
