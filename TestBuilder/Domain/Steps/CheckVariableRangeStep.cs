using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public sealed class CheckVariableRangeStep : ITestStep
    {
        private readonly ILogger _logger;
        private readonly string _variableName;
        private readonly double _min;
        private readonly double _max;
        private readonly bool _inclusive;
        private readonly string _failMessage;

        public CheckVariableRangeStep(
            ILogger logger,
            string variableName,
            double min,
            double max,
            bool inclusive,
            string failMessage)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _variableName = variableName?.Trim() ?? string.Empty;
            _min = min;
            _max = max;
            _inclusive = inclusive;
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

            if (!context.Variables.TryGetValue(_variableName, out var rawValue))
            {
                return Task.FromResult(Fail(context, string.Empty, $"VariableNotFound: переменная '{_variableName}' не найдена."));
            }

            var actualText = ToInvariantString(rawValue);
            double actual;

            try
            {
                actual = ParseDouble(rawValue);
            }
            catch (FormatException ex)
            {
                return Task.FromResult(Fail(context, actualText, ex.Message));
            }

            var passed = _inclusive
                ? actual >= _min && actual <= _max
                : actual > _min && actual < _max;

            SaveLastCheck(context, actualText, $"{_min.ToString(CultureInfo.InvariantCulture)}..{_max.ToString(CultureInfo.InvariantCulture)}", passed);

            _logger.Info(
                $"[ШАГ] Проверка диапазона переменной → {_variableName}, значение {actualText}, диапазон {FormatRange()}.");

            if (!passed)
            {
                var error = BuildMismatchMessage(actualText);
                _logger.Warning($"[ОШИБКА] {error}");
                return Task.FromResult(StepResult.False);
            }

            _logger.Info($"[OK] Переменная '{_variableName}' в диапазоне {FormatRange()}.");
            return Task.FromResult(StepResult.True);
        }

        private string BuildMismatchMessage(string actual)
        {
            if (!string.IsNullOrWhiteSpace(_failMessage))
            {
                return $"{_failMessage}. Факт: '{actual}', диапазон: {FormatRange()}.";
            }

            return $"Переменная '{_variableName}' имеет значение '{actual}' вне диапазона {FormatRange()}.";
        }

        private string FormatRange()
        {
            var left = _inclusive ? "[" : "(";
            var right = _inclusive ? "]" : ")";

            return $"{left}{_min.ToString(CultureInfo.InvariantCulture)}..{_max.ToString(CultureInfo.InvariantCulture)}{right}";
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
            SaveLastCheck(context, actual, $"{_min.ToString(CultureInfo.InvariantCulture)}..{_max.ToString(CultureInfo.InvariantCulture)}", false);
            _logger.Warning($"[ОШИБКА] {error}");
            return StepResult.False;
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
