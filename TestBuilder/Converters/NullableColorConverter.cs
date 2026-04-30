using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace TestBuilder.Converters
{
    /// <summary>
    /// Конвертирует hex-строку цвета в IBrush.
    /// null → возвращает UnsetValue чтобы сработал TargetNullValue в XAML.
    /// </summary>
    public class NullableColorConverter : IValueConverter
    {
        public static readonly NullableColorConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string hex && !string.IsNullOrEmpty(hex))
                return new SolidColorBrush(Color.Parse(hex));

            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}