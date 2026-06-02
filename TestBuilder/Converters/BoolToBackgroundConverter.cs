using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Globalization;

namespace TestBuilder.Converters
{
    public class BoolToBackgroundConverter : IValueConverter
    {
        public static readonly BoolToBackgroundConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var isDark = Avalonia.Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

            if (value is bool isReadOnly)
            {
                if (isDark)
                    return isReadOnly
                        ? new SolidColorBrush(Color.Parse("#182235"))   // тёмный фон — ReadOnly
                        : new SolidColorBrush(Color.Parse("#1E3A5F"));  // тёмно-синий — RW
                else
                    return isReadOnly
                        ? Brushes.White                                  // белый — ReadOnly
                        : new SolidColorBrush(Color.Parse("#E8F4FF"));  // голубой — RW
            }

            return isDark ? new SolidColorBrush(Color.Parse("#182235")) : Brushes.White;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
