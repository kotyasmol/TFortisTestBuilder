using Avalonia.Data.Converters;
using System;
using System.Collections;
using System.Globalization;

namespace TestBuilder.Converters;

public sealed class CollectionEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isEmpty = value switch
        {
            int count => count == 0,
            ICollection collection => collection.Count == 0,
            _ => true
        };
        return string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase)
            ? !isEmpty
            : isEmpty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
