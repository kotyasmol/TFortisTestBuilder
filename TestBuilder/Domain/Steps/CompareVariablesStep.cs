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
    public sealed class CompareVariablesStep : ITestStep
    {
        private readonly ILogger _logger;
        private readonly string _leftVariableName;
        private readonly string _rightVariableName;
        private readonly VariableComparisonType _comparisonType;
        private readonly string _failMessage;

        public CompareVariablesStep(
            ILogger logger,
            string leftVariableName,
            string rightVariableName,
            VariableComparisonType comparisonType,
            string failMessage)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _leftVariableName = leftVariableName?.Trim() ?? string.Empty;
            _rightVariableName = rightVariableName?.Trim() ?? string.Empty;
            _comparisonType = comparisonType;
            _failMessage = failMessage ?? string.Empty;
        }

        public Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(_leftVariableName) || string.IsNullOrWhiteSpace(_rightVariableName))
            {
                return Task.FromResult(Fail(context, string.Empty, string.Empty, "Имена переменных для сравнения не заданы."));
            }

            if (!context.Variables.TryGetValue(_leftVariableName, out var left))
            {
                return Task.FromResult(Fail(context, string.Empty, string.Empty, $"Переменная '{_leftVariableName}' не найдена."));
            }

            if (!context.Variables.TryGetValue(_rightVariableName, out var right))
            {
                return Task.FromResult(Fail(context, ToInvariantString(left), string.Empty, $"Переменная '{_rightVariableName}' не найдена."));
            }

            var leftText = ToInvariantString(left);
            var rightText = ToInvariantString(right);
            bool passed;

            try
            {
                passed = Compare(left, right, _comparisonType);
            }
            catch (FormatException ex)
            {
                return Task.FromResult(Fail(context, leftText, rightText, ex.Message));
            }

            context.SetVariable("LastCheck.VariableName", _leftVariableName);
            context.SetVariable("LastCheck.ActualValue", leftText);
            context.SetVariable("LastCheck.ExpectedValue", rightText);
            context.SetVariable("LastCheck.Passed", passed);

            _logger.Info($"[ШАГ] Сравнение переменных: {_leftVariableName}='{leftText}', {_rightVariableName}='{rightText}', тип {_comparisonType}.");

            if (!passed)
            {
                var message = string.IsNullOrWhiteSpace(_failMessage)
                    ? $"Переменные '{_leftVariableName}' и '{_rightVariableName}' не равны."
                    : _failMessage;

                _logger.Warning($"[ОШИБКА] {message} Факт: '{leftText}', ожидалось: '{rightText}'.");
                return Task.FromResult(StepResult.False);
            }

            _logger.Info("[OK] Переменные равны.");
            return Task.FromResult(StepResult.True);
        }

        private StepResult Fail(TestContext context, string left, string right, string error)
        {
            context.SetVariable("LastCheck.VariableName", _leftVariableName);
            context.SetVariable("LastCheck.ActualValue", left);
            context.SetVariable("LastCheck.ExpectedValue", right);
            context.SetVariable("LastCheck.Passed", false);
            _logger.Warning($"[ОШИБКА] {error}");
            return StepResult.False;
        }

        private static bool Compare(object left, object right, VariableComparisonType comparisonType)
        {
            return comparisonType switch
            {
                VariableComparisonType.Number => ParseDouble(left) == ParseDouble(right),
                VariableComparisonType.String => string.Equals(ToInvariantString(left), ToInvariantString(right), StringComparison.Ordinal),
                VariableComparisonType.Boolean => ParseBoolean(left) == ParseBoolean(right),
                VariableComparisonType.HexString => string.Equals(NormalizeHex(left), NormalizeHex(right), StringComparison.OrdinalIgnoreCase),
                VariableComparisonType.Version => CompareVersions(left, right) == 0,
                VariableComparisonType.MacAddress => string.Equals(NormalizeMac(left), NormalizeMac(right), StringComparison.OrdinalIgnoreCase),
                _ => throw new FormatException($"Неизвестный тип сравнения: {comparisonType}.")
            };
        }

        private static int CompareVersions(object left, object right)
        {
            var leftVersion = ParseVersion(ToInvariantString(left));
            var rightVersion = ParseVersion(ToInvariantString(right));
            return leftVersion.CompareTo(rightVersion);
        }

        private static Version ParseVersion(string value)
        {
            var parts = value.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts.Length > 4)
            {
                throw new FormatException($"Некорректная версия: '{value}'.");
            }

            while (parts.Length < 4)
            {
                parts = parts.Append("0").ToArray();
            }

            return Version.TryParse(string.Join(".", parts), out var version)
                ? version
                : throw new FormatException($"Некорректная версия: '{value}'.");
        }

        private static double ParseDouble(object value)
        {
            if (value is double d) return d;
            if (value is float f) return f;
            if (value is int or long or short or byte or decimal)
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }

            var text = ToInvariantString(value).Trim();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            throw new FormatException($"Значение '{text}' не является числом.");
        }

        private static bool ParseBoolean(object value)
        {
            if (value is bool boolean) return boolean;

            var text = ToInvariantString(value).Trim();
            if (bool.TryParse(text, out var parsed)) return parsed;

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
                text = text[2..];
            }

            var chars = text.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray();
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
