using System;
using System.Globalization;
using Avalonia.Data.Converters;
using TestBuilder.Presentation;

namespace TestBuilder.Converters;
public sealed class RussianUiTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => RussianUiText.Display(value);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Equals(parameter, "testType") ? RussianUiText.TestTypeKey(value?.ToString() ?? "") : value?.ToString() ?? "";
}
