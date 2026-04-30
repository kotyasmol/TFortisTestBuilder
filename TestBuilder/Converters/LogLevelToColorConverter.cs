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

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var isDark = Avalonia.Application.Current?.RequestedThemeVariant == ThemeVariant.Dark;

            if (value is LogLevel level)
            {
                return level switch
                {
                    LogLevel.Warning => new SolidColorBrush(Color.Parse("#F59E0B")),
                    LogLevel.Error => new SolidColorBrush(Color.Parse("#EF4444")),
                    _ => isDark
                        ? new SolidColorBrush(Color.Parse("#E5E7EB"))
                        : new SolidColorBrush(Color.Parse("#111827"))
                };
            }

            // Цвет по тексту — [OK] зелёный, [ОШИБКА] красный, [ШАГ] синий
            if (value is string msg)
            {
                if (msg.Contains("[OK]")) return new SolidColorBrush(Color.Parse("#22C55E"));
                if (msg.Contains("[ОШИБКА]")) return new SolidColorBrush(Color.Parse("#EF4444"));
                if (msg.Contains("[ШАГ]")) return new SolidColorBrush(Color.Parse("#60A5FA"));
            }

            return isDark
                ? new SolidColorBrush(Color.Parse("#E5E7EB"))
                : new SolidColorBrush(Color.Parse("#111827"));
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}