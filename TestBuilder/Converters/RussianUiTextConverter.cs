using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using TestBuilder.Presentation;

namespace TestBuilder.Converters;
public sealed class RussianUiTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => RussianUiText.Display(value);
    // Display-only, without exceptions or opt-out parameters. TextBox defaults to
    // TwoWay even when read-only; presentation must never write protocol values.
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
