using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace TestBuilder.Converters
{
    public class BoolToBackgroundConverter : IValueConverter
    {
        public static readonly BoolToBackgroundConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // IsReadOnly = true -> белый, IsReadOnly = false (RW) -> голубой
            if (value is bool isReadOnly)
                return isReadOnly ? Brushes.White : new SolidColorBrush(Color.Parse("#E8F4FF"));

            return Brushes.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}