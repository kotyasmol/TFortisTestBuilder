using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using TestBuilder.Presentation;

namespace TestBuilder.Converters;
public sealed class RussianUiTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => RussianUiText.Display(value);
    // Display labels are not protocol/configuration values. TextBox defaults to
    // TwoWay even when read-only, so never feed a translated label back by default.
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => parameter switch
    {
        "testType" => RussianUiText.TestTypeKey(value?.ToString() ?? ""),
        "editableText" => value?.ToString() ?? "",
        _ => BindingOperations.DoNothing
    };
}
