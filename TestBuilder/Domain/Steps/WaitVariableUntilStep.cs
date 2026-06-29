using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public sealed class WaitVariableUntilStep : ITestStep
    {
        private readonly IHttpRequestService _httpRequestService;
        private readonly ILogger _logger;
        private readonly string _variableName;
        private readonly string _expectedValue;
        private readonly VariableComparisonType _comparisonType;
        private readonly string _pollAction;
        private readonly string _baseUrl;
        private readonly int _requestTimeoutMs;
        private readonly int _timeoutMs;
        private readonly int _intervalMs;
        private readonly bool _failOnTimeout;

        public WaitVariableUntilStep(
            IHttpRequestService httpRequestService,
            ILogger logger,
            string variableName,
            string expectedValue,
            VariableComparisonType comparisonType,
            string pollAction,
            string baseUrl,
            int requestTimeoutMs,
            int timeoutMs,
            int intervalMs,
            bool failOnTimeout)
        {
            _httpRequestService = httpRequestService ?? throw new ArgumentNullException(nameof(httpRequestService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _variableName = string.IsNullOrWhiteSpace(variableName) ? "Dut.ups_rez" : variableName.Trim();
            _expectedValue = expectedValue ?? string.Empty;
            _comparisonType = comparisonType;
            _pollAction = string.IsNullOrWhiteSpace(pollAction) ? "None" : pollAction.Trim();
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://192.168.0.1" : baseUrl.Trim();
            _requestTimeoutMs = Math.Max(1, requestTimeoutMs);
            _timeoutMs = Math.Max(1, timeoutMs);
            _intervalMs = Math.Max(1, intervalMs);
            _failOnTimeout = failOnTimeout;
        }

        public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            var started = DateTimeOffset.UtcNow;
            var deadline = started.AddMilliseconds(_timeoutMs);
            var attempt = 0;
            var lastActual = string.Empty;
            var lastError = string.Empty;

            _logger.Info($"[ШАГ] Ожидание переменной {_variableName} == {_expectedValue}, poll={_pollAction}, timeout={_timeoutMs} мс.");

            while (DateTimeOffset.UtcNow <= deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await context.WaitWhilePausedAsync(cancellationToken);
                attempt++;

                var pollResult = await PollAsync(context, cancellationToken);
                if (!string.IsNullOrWhiteSpace(pollResult))
                {
                    lastError = pollResult;
                    _logger.Warning($"[ПОВТОР] Poll {_pollAction}: {pollResult}");
                }

                if (context.Variables.TryGetValue(_variableName, out var actual))
                {
                    lastActual = ToInvariantString(actual);

                    try
                    {
                        if (Compare(actual, _expectedValue, _comparisonType))
                        {
                            SaveState(context, true, attempt, lastActual, string.Empty);
                            _logger.Info($"[OK] Переменная {_variableName} достигла значения {_expectedValue} за {attempt} попыток.");
                            return StepResult.True;
                        }
                    }
                    catch (FormatException ex)
                    {
                        lastError = ex.Message;
                    }
                }
                else
                {
                    lastError = $"Переменная '{_variableName}' еще не найдена.";
                }

                var remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                var delay = TimeSpan.FromMilliseconds(Math.Min(_intervalMs, remaining.TotalMilliseconds));
                await Task.Delay(delay, cancellationToken);
            }

            var error = $"Таймаут ожидания {_variableName} == {_expectedValue}. Последнее значение: '{lastActual}'. {lastError}".Trim();
            SaveState(context, false, attempt, lastActual, error);
            _logger.Warning($"[ОШИБКА] {error}");
            return _failOnTimeout ? StepResult.False : StepResult.True;
        }

        private async Task<string> PollAsync(TestContext context, CancellationToken cancellationToken)
        {
            if (_pollAction.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var endpoint = _pollAction.ToLowerInvariant() switch
            {
                "getupsstatus" => "/api/getUpsStatus",
                "getupsvoltage" => "/api/getUpsVoltage",
                "getirpstatus" => "/api/isUps",
                _ => string.Empty
            };

            if (string.IsNullOrEmpty(endpoint))
            {
                return $"Неизвестное pollAction '{_pollAction}'.";
            }

            var url = _baseUrl.TrimEnd('/') + endpoint;
            var result = await _httpRequestService.GetAsync(url, TimeSpan.FromMilliseconds(_requestTimeoutMs), cancellationToken);
            var raw = result.Body.Trim();

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage) || !result.IsSuccessStatusCode)
            {
                return !string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? result.ErrorMessage
                    : $"HTTP {result.StatusCode}, response '{raw}'.";
            }

            if (_pollAction.Equals("GetUpsVoltage", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var voltage))
                {
                    context.SetVariable(_variableName, voltage);
                    context.SetVariable("WaitVariable.RawResponse", raw);
                    return string.Empty;
                }
            }
            else if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                context.SetVariable(_variableName, value);
                context.SetVariable("WaitVariable.RawResponse", raw);
                return string.Empty;
            }

            return $"Некорректный ответ '{raw}'.";
        }

        private void SaveState(TestContext context, bool passed, int attempts, string actual, string error)
        {
            context.SetVariable("WaitVariable.VariableName", _variableName);
            context.SetVariable("WaitVariable.ExpectedValue", _expectedValue);
            context.SetVariable("WaitVariable.ActualValue", actual);
            context.SetVariable("WaitVariable.Passed", passed);
            context.SetVariable("WaitVariable.Attempts", attempts);
            context.SetVariable("WaitVariable.Error", error);

            context.SetVariable("LastCheck.VariableName", _variableName);
            context.SetVariable("LastCheck.ActualValue", actual);
            context.SetVariable("LastCheck.ExpectedValue", _expectedValue);
            context.SetVariable("LastCheck.Passed", passed);
        }

        private static bool Compare(object actual, string expected, VariableComparisonType comparisonType)
        {
            return comparisonType switch
            {
                VariableComparisonType.Number => ParseDouble(actual) == ParseDouble(expected),
                VariableComparisonType.String => string.Equals(ToInvariantString(actual), expected, StringComparison.Ordinal),
                VariableComparisonType.Boolean => ParseBoolean(actual) == ParseBoolean(expected),
                _ => string.Equals(ToInvariantString(actual), expected, StringComparison.Ordinal)
            };
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
