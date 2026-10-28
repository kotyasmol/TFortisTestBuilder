using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace TestBuilder.Converters
{
    public class BoolToBackgroundConverter : IMultiValueConverter
    {
        public static readonly BoolToBackgroundConverter Instance = new();

        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var isDark = values.Count > 1 && Equals(values[1], ThemeVariant.Dark);

            if (values.Count > 0 && values[0] is bool isReadOnly)
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

    }
}
