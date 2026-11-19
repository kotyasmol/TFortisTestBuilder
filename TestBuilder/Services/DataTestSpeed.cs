using System;
using System.Globalization;

namespace TestBuilder.Services;

internal static class DataTestSpeed
{
    internal static int Parse(string? text)
    {
        if (!int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value is < 1 or > 1000)
            throw new InvalidOperationException("Скорость теста: введите целое число от 1 до 1000 Мбит/с.");
        return value;
    }
}
