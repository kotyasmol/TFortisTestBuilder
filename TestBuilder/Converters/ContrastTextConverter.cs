using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TestBuilder.Converters;

/// <summary>Choose text against the rendered button color, including transparency and the theme surface.</summary>
public sealed class ContrastTextConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not ISolidColorBrush brush || values[1] is not ISolidColorBrush surface)
            return Brushes.Black;
        var alpha = brush.Color.A / 255d * brush.Opacity;
        double Channel(byte front, byte back)
        {
            var value = (front * alpha + back * (1 - alpha)) / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        var luminance = .2126 * Channel(brush.Color.R, surface.Color.R) +
                        .7152 * Channel(brush.Color.G, surface.Color.G) + .0722 * Channel(brush.Color.B, surface.Color.B);
        return (luminance + .05) / .05 >= 1.05 / (luminance + .05) ? Brushes.Black : Brushes.White;
    }
}
