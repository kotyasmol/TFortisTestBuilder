using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public enum VariableComparisonType
    {
        Number,
        String,
        Boolean,
        HexString,
        Version,
        MacAddress
    }

    public sealed class CheckVariableEqualityStep : ITestStep
    {
        private readonly ILogger _logger;
        private readonly string _variableName;
        private readonly string _expectedValue;
        private readonly VariableComparisonType _comparisonType;
        private readonly string _failMessage;

        public CheckVariableEqualityStep(
            ILogger logger,
            string variableName,
            string expectedValue,
            VariableComparisonType comparisonType,
            string failMessage)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _variableName = variableName?.Trim() ?? string.Empty;
            _expectedValue = expectedValue ?? string.Empty;
            _comparisonType = comparisonType;
            _failMessage = failMessage ?? string.Empty;
        }

        public Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(_variableName))
            {
                return Task.FromResult(Fail(context, string.Empty, "Имя переменной не задано."));
            }

            if (!context.Variables.TryGetValue(_variableName, out var actual))
            {
                return Task.FromResult(Fail(context, string.Empty, $"VariableNotFound: переменная '{_variableName}' не найдена."));
            }

            var actualText = ToInvariantString(actual);
            string error;
            bool passed;

            try
            {
                passed = Compare(actual, _expectedValue, _comparisonType);
                error = passed
                    ? string.Empty
                    : BuildMismatchMessage(actualText);
            }
            catch (FormatException ex)
            {
                passed = false;
                error = ex.Message;
            }

            SaveLastCheck(context, actualText, _expectedValue, passed);

            _logger.Info(
                $"[ШАГ] Проверка переменной → {_variableName}, факт '{actualText}', ожидалось '{_expectedValue}', тип {_comparisonType}.");

            if (!passed)
            {
                _logger.Warning($"[ОШИБКА] {error}");
                return Task.FromResult(StepResult.False);
            }

            _logger.Info($"[OK] Переменная '{_variableName}' равна ожидаемому значению.");
            return Task.FromResult(StepResult.True);
        }

        private string BuildMismatchMessage(string actual)
        {
            if (!string.IsNullOrWhiteSpace(_failMessage))
            {
                return $"{_failMessage}. Факт: '{actual}', ожидалось: '{_expectedValue}'.";
            }

            return $"Переменная '{_variableName}' имеет значение '{actual}', ожидалось '{_expectedValue}'.";
        }

        private void SaveLastCheck(
            TestContext context,
            string actual,
            string expected,
            bool passed)
        {
            context.SetVariable("LastCheck.VariableName", _variableName);
            context.SetVariable("LastCheck.ActualValue", actual);
            context.SetVariable("LastCheck.ExpectedValue", expected);
            context.SetVariable("LastCheck.Passed", passed);
        }

        private StepResult Fail(
            TestContext context,
            string actual,
            string error)
        {
            SaveLastCheck(context, actual, _expectedValue, false);
            _logger.Warning($"[ОШИБКА] {error}");
            return StepResult.False;
        }

        private static bool Compare(
            object actual,
            string expected,
            VariableComparisonType comparisonType)
        {
            return comparisonType switch
            {
                VariableComparisonType.Number => ParseDouble(actual) == ParseDouble(expected),
                VariableComparisonType.String => string.Equals(ToInvariantString(actual), expected, StringComparison.Ordinal),
                VariableComparisonType.Boolean => ParseBoolean(actual) == ParseBoolean(expected),
                VariableComparisonType.HexString => string.Equals(NormalizeHex(actual), NormalizeHex(expected), StringComparison.OrdinalIgnoreCase),
                VariableComparisonType.Version => CompareVersions(actual, expected) == 0,
                VariableComparisonType.MacAddress => string.Equals(NormalizeMac(actual), NormalizeMac(expected), StringComparison.OrdinalIgnoreCase),
                _ => throw new FormatException($"Неизвестный тип сравнения: {comparisonType}.")
            };
        }

        private static int CompareVersions(object actual, string expected)
        {
            var actualVersion = ParseVersion(ToInvariantString(actual));
            var expectedVersion = ParseVersion(expected);
            return actualVersion.CompareTo(expectedVersion);
        }

        private static Version ParseVersion(string value)
        {
            var normalized = value.Trim();

            if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
            {
                normalized = numeric.ToString(CultureInfo.InvariantCulture);
            }

            var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0 || parts.Length > 4)
            {
                throw new FormatException($"Некорректная версия: '{value}'.");
            }

            while (parts.Length < 4)
            {
                parts = parts.Append("0").ToArray();
            }

            normalized = string.Join(".", parts);

            return Version.TryParse(normalized, out var version)
                ? version
                : throw new FormatException($"Некорректная версия: '{value}'.");
        }

        private static double ParseDouble(object value)
        {
            if (value is double d)
            {
                return d;
            }

            if (value is float f)
            {
                return f;
            }

            if (value is int or long or short or byte or decimal)
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }

            var text = ToInvariantString(value).Trim();

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariant))
            {
                return invariant;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var current))
            {
                return current;
            }

            throw new FormatException($"Значение '{text}' не является числом.");
        }

        private static bool ParseBoolean(object value)
        {
            if (value is bool boolean)
            {
                return boolean;
            }

            var text = ToInvariantString(value).Trim();

            if (bool.TryParse(text, out var parsed))
            {
                return parsed;
            }

            return text.ToLowerInvariant() switch
            {
                "1" or "yes" or "y" or "on" or "да" => true,
                "0" or "no" or "n" or "off" or "нет" => false,
                _ => throw new FormatException($"Значение '{text}' не является Boolean.")
            };
        }

        private static string NormalizeHex(object value)
        {
            var text = ToInvariantString(value).Trim();

            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(2);
            }

            var chars = text
                .Where(Uri.IsHexDigit)
                .Select(char.ToUpperInvariant)
                .ToArray();

            if (chars.Length == 0)
            {
                throw new FormatException($"Значение '{text}' не является HexString.");
            }

            return new string(chars).TrimStart('0');
        }

        private static string NormalizeMac(object value)
        {
            var text = ToInvariantString(value).Trim();
            var hex = new string(text.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());

            if (hex.Length != 12)
            {
                throw new FormatException($"Значение '{text}' не является MAC-адресом.");
            }

            var builder = new StringBuilder();

            for (var i = 0; i < hex.Length; i += 2)
            {
                if (builder.Length > 0)
                {
                    builder.Append(':');
                }

                builder.Append(hex.Substring(i, 2));
            }

            return builder.ToString();
        }

        private static string ToInvariantString(object? value)
        {
            return value switch
            {
                null => string.Empty,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty
            };
        }
    }
}
