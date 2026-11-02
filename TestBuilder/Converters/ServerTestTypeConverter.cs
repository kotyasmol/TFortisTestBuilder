using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TestBuilder.Converters;

// A field-specific, reversible mapping. Custom server codes must pass unchanged,
// even when they happen to match a key from the general UI translation catalogue.
public sealed class ServerTestTypeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() switch { "production" => "Производственная", "repair" => "Ремонтная", var text => text ?? "" };
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() switch { "Производственная" => "production", "Ремонтная" => "repair", var text => text ?? "" };
}
