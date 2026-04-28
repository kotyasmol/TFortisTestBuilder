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

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isDark = Avalonia.Application.Current?.RequestedThemeVariant == ThemeVariant.Dark;

            if (value is bool isReadOnly)
            {
                if (isDark)
                    return isReadOnly
                        ? new SolidColorBrush(Color.Parse("#1E1E1E"))   // тёмный фон — ReadOnly
                        : new SolidColorBrush(Color.Parse("#1A2A3A"));  // тёмно-синий — RW
                else
                    return isReadOnly
                        ? Brushes.White                                  // белый — ReadOnly
                        : new SolidColorBrush(Color.Parse("#E8F4FF"));  // голубой — RW
            }

            return isDark ? new SolidColorBrush(Color.Parse("#1E1E1E")) : Brushes.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}