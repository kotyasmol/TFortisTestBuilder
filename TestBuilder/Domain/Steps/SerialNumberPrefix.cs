using System;
using System.Globalization;
using System.Linq;

namespace TestBuilder.Domain.Steps
{
    internal static class SerialNumberPrefix
    {
        public static string? Validate(string prefix) =>
            prefix.All(char.IsAsciiDigit) ? null : "Префикс серийного номера должен состоять из цифр 0–9.";

        public static string? Check(long serial, string prefix)
        {
            var number = serial.ToString(CultureInfo.InvariantCulture);
            if (number == prefix)
                return $"Получен только префикс {prefix}. Требуется полный серийный номер.";

            return number.StartsWith(prefix, StringComparison.Ordinal)
                ? null
                : $"Серийный номер {number} не начинается с ожидаемого префикса {prefix}.";
        }
    }
}
