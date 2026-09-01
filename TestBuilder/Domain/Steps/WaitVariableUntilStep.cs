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
        private readonly string _endpoint;
        private readonly HttpResponseValueType _responseType;
        private readonly int _requestTimeoutMs;
        private readonly int _timeoutMs;
        private readonly int _intervalMs;
        private readonly bool _failOnTimeout;
        private readonly bool _useBrowserForSelftest;

        public WaitVariableUntilStep(
            IHttpRequestService httpRequestService,
            ILogger logger,
            string variableName,
            string expectedValue,
            VariableComparisonType comparisonType,
            string pollAction,
            string baseUrl,
            string endpoint,
            HttpResponseValueType responseType,
            int requestTimeoutMs,
            int timeoutMs,
            int intervalMs,
            bool failOnTimeout,
            bool useBrowserForSelftest = true)
        {
            _httpRequestService = httpRequestService ?? throw new ArgumentNullException(nameof(httpRequestService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _variableName = string.IsNullOrWhiteSpace(variableName) ? "Dut.ups_rez" : variableName.Trim();
            _expectedValue = expectedValue ?? string.Empty;
            _comparisonType = comparisonType;
            _pollAction = string.IsNullOrWhiteSpace(pollAction) ? "None" : pollAction.Trim();
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://192.168.0.1" : baseUrl.Trim();
            _endpoint = endpoint?.Trim() ?? string.Empty;
            _responseType = responseType;
            _requestTimeoutMs = Math.Max(1, requestTimeoutMs);
            _timeoutMs = Math.Max(1, timeoutMs);
            _intervalMs = Math.Max(1, intervalMs);
            _failOnTimeout = failOnTimeout;
            _useBrowserForSelftest = useBrowserForSelftest;
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
                else
                {
                    lastError = string.Empty;
                }

                if (string.IsNullOrWhiteSpace(pollResult) &&
                    context.Variables.TryGetValue(_variableName, out var actual))
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
                else if (string.IsNullOrWhiteSpace(pollResult))
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

            if (_pollAction.Equals("SelftestSnapshot", StringComparison.OrdinalIgnoreCase))
            {
                context.Variables.Remove(_variableName);
                var snapshotUrl = ReadHttpVariableStep.BuildUrl(_baseUrl, _endpoint);
                var outputPrefix = GetVariablePrefix(_variableName);
                var snapshotStep = new SelfTestCheckStep(
                    _httpRequestService,
                    _logger,
                    snapshotUrl,
                    _requestTimeoutMs,
                    outputPrefix,
                    SelfTestCheckStep.DefaultValidationRules,
                    failOnError: false,
                    useBrowser: _useBrowserForSelftest,
                    pollIntervalMs: _intervalMs,
                    enforceMinimumDeviceReadyTimeout: false);

                var snapshotResult = await snapshotStep.ExecuteAsync(context, cancellationToken);
                if (snapshotResult == StepResult.True)
                {
                    return string.Empty;
                }

                return context.Variables.TryGetValue("SelfTest.Error", out var error)
                    ? ToInvariantString(error)
                    : "Не удалось обновить selftest-снимок DUT.";
            }

            if (_pollAction.Equals("GetIrpStatus", StringComparison.OrdinalIgnoreCase))
            {
                context.Variables.Remove(_variableName);

                var read = await GetIrpStatusStep.ReadStatusAsync(
                    _httpRequestService,
                    _logger,
                    _baseUrl,
                    _requestTimeoutMs,
                    cancellationToken);

                context.SetVariable("WaitVariable.RawResponse", read.RawResponse);
                context.SetVariable("WaitVariable.Url", read.Url);
                context.SetVariable("WaitVariable.StatusCode", read.StatusCode);

                if (read.Success)
                {
                    context.SetVariable(_variableName, read.Value);
                    return string.Empty;
                }

                return read.Error;
            }

            var poll = _pollAction.ToLowerInvariant() switch
            {
                "httpget" => (_endpoint, _responseType),
                "getupsstatus" => ("/api/getUpsStatus", HttpResponseValueType.Integer),
                "getupsvoltage" => ("/api/getUpsVoltage", HttpResponseValueType.Number),
                _ => (string.Empty, _responseType)
            };

            if (string.IsNullOrWhiteSpace(poll.Item1))
            {
                return $"Неизвестное pollAction '{_pollAction}'.";
            }

            context.Variables.Remove(_variableName);
            var httpRead = await ReadHttpVariableStep.ReadAsync(
                _httpRequestService,
                _logger,
                _baseUrl,
                poll.Item1,
                poll.Item2,
                _requestTimeoutMs,
                cancellationToken);
            ReadHttpVariableStep.SaveDiagnostics(
                context,
                "WaitVariable",
                httpRead,
                _variableName,
                poll.Item2);

            if (httpRead.Success)
            {
                context.SetVariable(_variableName, httpRead.Value!);
                return string.Empty;
            }

            return httpRead.Error;
        }

        private static string GetVariablePrefix(string variableName)
        {
            var separator = variableName.LastIndexOf('.');
            return separator > 0 ? variableName[..separator] : SelfTestCheckStep.DefaultOutputPrefix;
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
