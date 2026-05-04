using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Globalization;
using TestBuilder.Services.Logging;

namespace TestBuilder.Converters
{
    public class LogLevelToColorConverter : IValueConverter
    {
        public static readonly LogLevelToColorConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var isDark = Avalonia.Application.Current?.RequestedThemeVariant == ThemeVariant.Dark;

            if (value is string msg)
            {
                // Яркие цвета — читаются на любом фоне
                if (msg.Contains("[OK]")) return new SolidColorBrush(Color.Parse("#16A34A")); // зелёный
                if (msg.Contains("[ОШИБКА]")) return new SolidColorBrush(Color.Parse("#DC2626")); // красный
                if (msg.Contains("[ШАГ]")) return new SolidColorBrush(Color.Parse("#2563EB")); // синий
            }

            // Обычный текст — адаптируется под тему
            return isDark
                ? new SolidColorBrush(Color.Parse("#D1D5DB"))
                : new SolidColorBrush(Color.Parse("#1F2937"));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}