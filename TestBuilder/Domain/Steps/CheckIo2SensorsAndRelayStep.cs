using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Steps
{
    /// <summary>
    /// Проверяет цепи Sensor1, Sensor2 и релейный выход DUT через плату IO-2 стенда.
    /// IO-2 выбирается по результату сканирования стенда или по явно заданному Slave ID.
    /// </summary>
    public sealed class CheckIo2SensorsAndRelayStep : ITestStep
    {
        public const string DefaultBaseUrl = "http://192.168.0.1";
        public const string DefaultSelftestEndpoint =
            "/cgi-bin/luci/admin/statistics/deviceinfo?luci_username=admin&luci_password=admin";
        public const string DefaultRelayEndpointTemplate = "/test.shtml?set_mb_output={state}";
        public const int DefaultRequestTimeoutMs = 5000;
        public const int DefaultStateTimeoutMs = 30000;
        public const int DefaultPollIntervalMs = 1000;

        private const ushort Io2TypeRegister = 0;
        private const ushort Io2TypeValue = 5;
        private const ushort Sensor1OutputRegister = 1500;
        private const ushort Sensor2OutputRegister = 1501;
        private const ushort RelayInputRegister = 1507;
        private const int CleanupVerifyTimeoutMs = 3000;

        private readonly IModbusService _modbusService;
        private readonly IHttpRequestService _httpRequestService;
        private readonly ILogger _logger;
        private readonly byte _io2SlaveId;
        private readonly string _baseUrl;
        private readonly string _selftestEndpoint;
        private readonly string _relayEndpointTemplate;
        private readonly int _requestTimeoutMs;
        private readonly int _stateTimeoutMs;
        private readonly int _pollIntervalMs;
        private readonly bool _useBrowserForSelftest;
        private readonly bool _checkRelay;

        public CheckIo2SensorsAndRelayStep(
            IModbusService modbusService,
            IHttpRequestService httpRequestService,
            ILogger logger,
            byte io2SlaveId,
            string baseUrl,
            string selftestEndpoint,
            string relayEndpointTemplate,
            int requestTimeoutMs,
            int stateTimeoutMs,
            int pollIntervalMs,
            bool useBrowserForSelftest,
            bool checkRelay = true)
        {
            _modbusService = modbusService ?? throw new ArgumentNullException(nameof(modbusService));
            _httpRequestService = httpRequestService ?? throw new ArgumentNullException(nameof(httpRequestService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _io2SlaveId = io2SlaveId;
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim();
            _selftestEndpoint = string.IsNullOrWhiteSpace(selftestEndpoint)
                ? DefaultSelftestEndpoint
                : selftestEndpoint.Trim();
            _relayEndpointTemplate = string.IsNullOrWhiteSpace(relayEndpointTemplate)
                ? DefaultRelayEndpointTemplate
                : relayEndpointTemplate.Trim();
            _requestTimeoutMs = Math.Max(1, requestTimeoutMs);
            _stateTimeoutMs = Math.Max(1, stateTimeoutMs);
            _pollIntervalMs = Math.Max(1, pollIntervalMs);
            _useBrowserForSelftest = useBrowserForSelftest;
            _checkRelay = checkRelay;
        }

        public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            _logger.Info(_checkRelay
                ? "[ШАГ] Проверка Sensor1, Sensor2 и релейного выхода через IO-2."
                : "[ШАГ] Проверка Sensor1 и Sensor2 через IO-2; реле не входит в профиль.");
            SaveConfiguration(context);

            byte? io2SlaveId = null;
            var passed = false;
            var cleanupPassed = false;
            var failureMessage = string.Empty;
            try
            {
                io2SlaveId = await ResolveIo2SlaveIdAsync(context, cancellationToken);
                if (io2SlaveId == null)
                {
                    failureMessage = "Плата IO-2 не найдена. Подключите стенд и выполните сканирование устройств.";
                }
                else
                {
                    context.SetVariable("InOut.Io2SlaveId", io2SlaveId.Value);

                    if (!await ResetOutputsAsync(context, io2SlaveId.Value, cancellationToken))
                    {
                        failureMessage = "Исходное состояние IO-2 не подтверждено: OUT1 и OUT2 должны быть 0. Проверка не запущена.";
                    }
                    else if (_checkRelay && !await EnsureRelayStateAsync(context, io2SlaveId.Value, 0, cancellationToken, "InOut.Relay.Initial"))
                    {
                        failureMessage = "Не удалось привести релейный выход DUT в исходное состояние.";
                    }
                    else
                    {
                        var sensor1Passed = await TestSensorAsync(
                            context,
                            io2SlaveId.Value,
                            "Sensor1",
                            Sensor1OutputRegister,
                            "Dut.sensor_1",
                            cancellationToken);
                        context.AddReportEntry("Sensor1", sensor1Passed, sensor1Passed ? "1" : "0");
                        if (!sensor1Passed)
                        {
                            failureMessage = "Проверка Sensor1 не пройдена.";
                        }
                        else
                        {
                            var sensor2Passed = await TestSensorAsync(
                                context,
                                io2SlaveId.Value,
                                "Sensor2",
                                Sensor2OutputRegister,
                                "Dut.sensor_2",
                                cancellationToken);
                            context.AddReportEntry("Sensor2", sensor2Passed, sensor2Passed ? "1" : "0");
                            if (!sensor2Passed)
                            {
                                failureMessage = "Проверка Sensor2 не пройдена.";
                            }
                            else
                            {
                                if (!_checkRelay)
                                {
                                    context.SetVariable("InOut.Relay.Skipped", true);
                                    passed = true;
                                }
                                else
                                {
                                    var relayPassed = await TestRelayAsync(context, io2SlaveId.Value, cancellationToken);
                                    context.AddReportEntry("Релейный выход", relayPassed, relayPassed ? "1" : "0");
                                    if (!relayPassed)
                                    {
                                        failureMessage = "Проверка релейного выхода не пройдена.";
                                    }
                                    else
                                    {
                                        passed = true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                cleanupPassed = await RestoreSafeStateAsync(context, io2SlaveId, passed);
            }

            if (!passed)
            {
                return Fail(context, failureMessage);
            }

            if (!cleanupPassed)
            {
                return Fail(context, "Проверка прошла, но не удалось вернуть цепи IO-2/DUT в безопасное состояние.");
            }

            context.SetVariable("InOut.Ok", true);
            context.SetVariable("InOut.Error", string.Empty);
            _logger.Info(_checkRelay
                ? "[OK] Sensor1, Sensor2 и релейный выход проверены."
                : "[OK] Sensor1 и Sensor2 проверены; реле пропущено по настройке профиля.");
            return StepResult.True;
        }

        private void SaveConfiguration(TestContext context)
        {
            context.SetVariable("InOut.Ok", false);
            context.SetVariable("InOut.Error", string.Empty);
            context.SetVariable("InOut.InitialOutputsOff", false);
            context.SetVariable("InOut.ConfiguredIo2SlaveId", _io2SlaveId);
            context.SetVariable("InOut.BaseUrl", _baseUrl);
            context.SetVariable("InOut.SelftestEndpoint", _selftestEndpoint);
            context.SetVariable("InOut.RequestTimeoutMs", _requestTimeoutMs);
            context.SetVariable("InOut.StateTimeoutMs", _stateTimeoutMs);
            context.SetVariable("InOut.PollIntervalMs", _pollIntervalMs);
            context.SetVariable("InOut.CheckRelay", _checkRelay);
        }

        private async Task<bool> ResetOutputsAsync(TestContext context, byte io2SlaveId, CancellationToken cancellationToken)
        {
            _logger.Info("[ШАГ] Подготовка IO-2: размыкаю OUT1 (1500) и OUT2 (1501), подтверждаю оба нуля чтением.");
            // Attempt both resets even if the first one cannot be confirmed.
            var output1Off = await WriteIo2RegisterAsync(context, io2SlaveId,
                Sensor1OutputRegister, 0, cancellationToken, "InOut.Initial.Sensor1");
            var output2Off = await WriteIo2RegisterAsync(context, io2SlaveId,
                Sensor2OutputRegister, 0, cancellationToken, "InOut.Initial.Sensor2");
            context.SetVariable("InOut.InitialOutputsOff", output1Off && output2Off);
            return output1Off && output2Off;
        }

        private async Task<byte?> ResolveIo2SlaveIdAsync(TestContext context, CancellationToken cancellationToken)
        {
            if (_io2SlaveId == 0)
            {
                var discovered = SlaveRegistry.Instance.Slaves
                    .OfType<IO2Model>()
                    .Select(model => model.SlaveId)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList();

                if (discovered.Count == 0)
                {
                    context.SetVariable("InOut.ResolveError", "IO-2 отсутствует среди устройств последнего сканирования.");
                    return null;
                }

                if (discovered.Count > 1)
                {
                    _logger.Warning(
                        $"[ПРЕДУПРЕЖДЕНИЕ] Найдено несколько IO-2 ({string.Join(", ", discovered)}). Используется {discovered[0]}.");
                }

                _logger.Info($"[INFO] IO-2 найден автоматически: Slave ID {discovered[0]}.");
                return discovered[0];
            }

            try
            {
                var type = await _modbusService.ReadRegistersAsync(
                    _io2SlaveId,
                    Io2TypeRegister,
                    1,
                    cancellationToken);
                if (type.Length == 0 || type[0] != Io2TypeValue)
                {
                    var actual = type.Length == 0 ? "нет ответа" : type[0].ToString(CultureInfo.InvariantCulture);
                    context.SetVariable("InOut.ResolveError", $"Slave ID {_io2SlaveId}: ожидался тип IO-2 (5), получено {actual}.");
                    return null;
                }

                _logger.Info($"[INFO] Используется заданный IO-2: Slave ID {_io2SlaveId}.");
                return _io2SlaveId;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                context.SetVariable("InOut.ResolveError", ex.Message);
                _logger.Warning($"[ОШИБКА] Не удалось проверить IO-2 с Slave ID {_io2SlaveId}: {ex.Message}");
                return null;
            }
        }

        private async Task<bool> TestSensorAsync(
            TestContext context,
            byte io2SlaveId,
            string name,
            ushort outputRegister,
            string selftestVariableName,
            CancellationToken cancellationToken)
        {
            var prefix = $"InOut.{name}";
            _logger.Info($"[INFO] {name}: замыкаю цепь через IO-2 register {outputRegister}.");
            context.SetVariable($"{prefix}.OutputRegister", outputRegister);
            context.SetVariable($"{prefix}.SelftestVariable", selftestVariableName);

            var outputEnabled = false;
            var passed = false;
            var outputDisabled = false;
            try
            {
                outputEnabled = await WriteIo2RegisterAsync(
                    context,
                    io2SlaveId,
                    outputRegister,
                    1,
                    cancellationToken,
                    prefix);
                if (outputEnabled)
                {
                    passed = await WaitForSelftestValueAsync(
                        context,
                        selftestVariableName,
                        "1",
                        cancellationToken,
                        prefix);
                }
            }
            finally
            {
                outputDisabled = await WriteIo2RegisterAsync(
                    context,
                    io2SlaveId,
                    outputRegister,
                    0,
                    CancellationToken.None,
                    $"{prefix}.Cleanup",
                    verificationTimeoutMs: Math.Min(_stateTimeoutMs, CleanupVerifyTimeoutMs));

                context.SetVariable($"{prefix}.OutputDisabled", outputDisabled);
                if (!outputDisabled)
                {
                    passed = false;
                    context.SetVariable($"{prefix}.Error", "Не удалось разомкнуть выход IO-2 после проверки.");
                    _logger.Error($"[ОШИБКА] {name}: выход IO-2 {outputRegister} не удалось выключить.");
                }

                context.SetVariable($"{prefix}.Passed", passed && outputDisabled);
            }

            return passed && outputDisabled;
        }

        private async Task<bool> TestRelayAsync(TestContext context, byte io2SlaveId, CancellationToken cancellationToken)
        {
            const string prefix = "InOut.Relay";
            _logger.Info("[INFO] Релейный выход: включаю тестовый режим DUT и проверяю IO-2 IN1.");
            context.SetVariable($"{prefix}.InputRegister", RelayInputRegister);

            if (!await EnsureRelayStateAsync(context, io2SlaveId, 0, cancellationToken, $"{prefix}.BeforeOn"))
            {
                context.SetVariable($"{prefix}.Passed", false);
                return false;
            }

            if (!await SendRelayCommandAsync(context, 1, cancellationToken, $"{prefix}.On"))
            {
                context.SetVariable($"{prefix}.Passed", false);
                return false;
            }

            if (!await WaitForIo2InputAsync(context, io2SlaveId, 1, cancellationToken, $"{prefix}.On"))
            {
                context.SetVariable($"{prefix}.Passed", false);
                return false;
            }

            if (!await EnsureRelayStateAsync(context, io2SlaveId, 0, cancellationToken, $"{prefix}.Off"))
            {
                context.SetVariable($"{prefix}.Passed", false);
                return false;
            }

            context.SetVariable($"{prefix}.Passed", true);
            return true;
        }

        private async Task<bool> EnsureRelayStateAsync(
            TestContext context,
            byte io2SlaveId,
            ushort expectedInputValue,
            CancellationToken cancellationToken,
            string prefix)
        {
            if (!await SendRelayCommandAsync(context, expectedInputValue, cancellationToken, prefix))
            {
                return false;
            }

            // Legacy IO-2 explicitly clears the input register after releasing
            // the DUT relay. A previous captured 1 must not satisfy a new test.
            if (expectedInputValue == 0 && !await WriteIo2RegisterAsync(
                    context, io2SlaveId, RelayInputRegister, 0, cancellationToken, $"{prefix}.ResetInput"))
            {
                return false;
            }

            return await WaitForIo2InputAsync(context, io2SlaveId, expectedInputValue, cancellationToken, prefix);
        }

        private async Task<bool> SendRelayCommandAsync(
            TestContext context,
            ushort state,
            CancellationToken cancellationToken,
            string prefix)
        {
            var endpoint = _relayEndpointTemplate.Replace(
                "{state}",
                state.ToString(CultureInfo.InvariantCulture),
                StringComparison.OrdinalIgnoreCase);
            var url = ReadHttpVariableStep.BuildUrl(_baseUrl, endpoint);
            context.SetVariable($"{prefix}.Url", url);
            context.SetVariable($"{prefix}.RequestedState", state);

            if (string.IsNullOrWhiteSpace(url))
            {
                context.SetVariable($"{prefix}.Error", "Не задан URL команды релейного выхода.");
                return false;
            }

            try
            {
                var result = await _httpRequestService.GetAsync(
                    url,
                    TimeSpan.FromMilliseconds(_requestTimeoutMs),
                    cancellationToken);
                var error = !string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? result.ErrorMessage
                    : !result.IsSuccessStatusCode
                        ? $"HTTP {result.StatusCode ?? 0}."
                        : string.Empty;

                context.SetVariable($"{prefix}.StatusCode", result.StatusCode ?? 0);
                context.SetVariable($"{prefix}.ElapsedMs", (int)Math.Clamp(result.Elapsed.TotalMilliseconds, 0, int.MaxValue));
                context.SetVariable($"{prefix}.RawResponse", result.Body);
                context.SetVariable($"{prefix}.Success", string.IsNullOrWhiteSpace(error));
                context.SetVariable($"{prefix}.Error", error);

                if (!string.IsNullOrWhiteSpace(error))
                {
                    _logger.Warning($"[ОШИБКА] Команда реле state={state} не выполнена: {error}");
                    return false;
                }

                _logger.Info($"[OK] Команда реле state={state} принята DUT.");
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                context.SetVariable($"{prefix}.Success", false);
                context.SetVariable($"{prefix}.Error", ex.Message);
                _logger.Warning($"[ОШИБКА] Команда реле state={state}: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> WaitForIo2InputAsync(
            TestContext context,
            byte io2SlaveId,
            ushort expectedValue,
            CancellationToken cancellationToken,
            string prefix)
        {
            var deadline = DateTimeOffset.UtcNow.AddMilliseconds(_stateTimeoutMs);
            var attempts = 0;
            var lastValue = -1;
            var lastError = string.Empty;

            while (DateTimeOffset.UtcNow <= deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await context.WaitWhilePausedAsync(cancellationToken);
                attempts++;

                try
                {
                    var values = await _modbusService.ReadRegistersAsync(
                        io2SlaveId,
                        RelayInputRegister,
                        1,
                        cancellationToken);
                    if (values.Length == 0)
                    {
                        lastError = "IO-2 вернул пустой ответ.";
                    }
                    else
                    {
                        lastValue = values[0];
                        if (values[0] == expectedValue)
                        {
                            context.SetVariable($"{prefix}.InputValue", values[0]);
                            context.SetVariable($"{prefix}.InputAttempts", attempts);
                            context.SetVariable($"{prefix}.InputPassed", true);
                            context.SetVariable($"{prefix}.InputError", string.Empty);
                            _logger.Info($"[OK] IO-2 IN1 = {expectedValue} за {attempts} попыток.");
                            return true;
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastError = ex.Message;
                }

                var remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(_pollIntervalMs, remaining.TotalMilliseconds)), cancellationToken);
            }

            var error = $"Таймаут IO-2 IN1: ожидалось {expectedValue}, получено {lastValue}. {lastError}".Trim();
            context.SetVariable($"{prefix}.InputValue", lastValue);
            context.SetVariable($"{prefix}.InputAttempts", attempts);
            context.SetVariable($"{prefix}.InputPassed", false);
            context.SetVariable($"{prefix}.InputError", error);
            _logger.Warning($"[ОШИБКА] {error}");
            return false;
        }

        private async Task<bool> WaitForSelftestValueAsync(
            TestContext context,
            string variableName,
            string expectedValue,
            CancellationToken cancellationToken,
            string prefix)
        {
            var waitStep = new WaitVariableUntilStep(
                _httpRequestService,
                _logger,
                variableName,
                expectedValue,
                VariableComparisonType.Number,
                "SelftestSnapshot",
                _baseUrl,
                _selftestEndpoint,
                HttpResponseValueType.String,
                _requestTimeoutMs,
                _stateTimeoutMs,
                _pollIntervalMs,
                failOnTimeout: true,
                useBrowserForSelftest: _useBrowserForSelftest);

            var result = await waitStep.ExecuteAsync(context, cancellationToken);
            var passed = result == StepResult.True;
            context.SetVariable($"{prefix}.SelftestValue", context.Variables.TryGetValue(variableName, out var value) ? value : string.Empty);
            context.SetVariable($"{prefix}.SelftestPassed", passed);
            context.SetVariable(
                $"{prefix}.Error",
                passed
                    ? string.Empty
                    : context.GetVariable<string>("WaitVariable.Error") ?? "Не удалось получить ожидаемое состояние из selftest.");
            return passed;
        }

        private async Task<bool> WriteIo2RegisterAsync(
            TestContext context,
            byte io2SlaveId,
            ushort register,
            ushort value,
            CancellationToken cancellationToken,
            string prefix,
            int? verificationTimeoutMs = null)
        {
            context.SetVariable($"{prefix}.WriteRegister", register);
            context.SetVariable($"{prefix}.WriteValue", value);
            context.SetVariable($"{prefix}.WriteSuccess", false);
            context.SetVariable($"{prefix}.WriteAttempts", 0);
            context.SetVariable($"{prefix}.WriteActualValue", -1);
            var timeoutMs = verificationTimeoutMs ?? _stateTimeoutMs;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(timeoutMs);
            var operationToken = timeout.Token;
            try
            {
                operationToken.ThrowIfCancellationRequested();
                var success = await _modbusService.WriteRegisterAsync(
                    io2SlaveId,
                    register,
                    value,
                    verify: false,
                    cancellationToken: operationToken);
                if (!success)
                {
                    context.SetVariable($"{prefix}.WriteError", "Команда Modbus-записи не выполнена.");
                    _logger.Warning($"[ОШИБКА] IO-2 {io2SlaveId}, register {register} ← {value}: команда не выполнена.");
                    return false;
                }

                // IO-2 can expose the previous value just after a write. Poll
                // read-back instead of immediately failing or repeating writes.
                var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
                var attempts = 0;
                var lastValue = -1;
                var lastError = string.Empty;
                do
                {
                    operationToken.ThrowIfCancellationRequested();
                    attempts++;
                    try
                    {
                        var values = await _modbusService.ReadRegistersAsync(io2SlaveId, register, 1, operationToken);
                        lastValue = values.Length == 0 ? -1 : values[0];
                        context.SetVariable($"{prefix}.WriteAttempts", attempts);
                        context.SetVariable($"{prefix}.WriteActualValue", lastValue);
                        if (lastValue == value)
                        {
                            context.RegisterState.Update(io2SlaveId, register, value);
                            context.SetVariable($"{prefix}.WriteSuccess", true);
                            context.SetVariable($"{prefix}.WriteError", string.Empty);
                            _logger.Info($"[OK] IO-2 {io2SlaveId}, register {register} = {value}, подтверждено чтением ({attempts}).");
                            return true;
                        }
                        lastError = values.Length == 0 ? "Пустой ответ IO-2." : string.Empty;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        lastError = ex.Message;
                    }

                    var remaining = deadline - DateTimeOffset.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(_pollIntervalMs, remaining.TotalMilliseconds)), operationToken);
                } while (DateTimeOffset.UtcNow <= deadline);

                var error = $"Запись IO-2 {register} не подтверждена: ожидалось {value}, прочитано {lastValue}. {lastError}".Trim();
                context.SetVariable($"{prefix}.WriteError", error);
                _logger.Warning($"[ОШИБКА] {error}");
                return false;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                var error = $"Таймаут подтверждения IO-2 {register} = {value} ({timeoutMs} мс).";
                context.SetVariable($"{prefix}.WriteError", error);
                _logger.Warning($"[ОШИБКА] {error}");
                return false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                context.SetVariable($"{prefix}.WriteSuccess", false);
                context.SetVariable($"{prefix}.WriteError", ex.Message);
                _logger.Warning($"[ОШИБКА] IO-2 {io2SlaveId}, register {register} ← {value}: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> RestoreSafeStateAsync(TestContext context, byte? io2SlaveId, bool testPassed)
        {
            var outputsSafe = true;
            if (io2SlaveId != null)
            {
                outputsSafe &= await WriteIo2RegisterAsync(
                    context,
                    io2SlaveId.Value,
                    Sensor1OutputRegister,
                    0,
                    CancellationToken.None,
                    "InOut.Finally.Sensor1",
                    verificationTimeoutMs: Math.Min(_stateTimeoutMs, CleanupVerifyTimeoutMs));
                outputsSafe &= await WriteIo2RegisterAsync(
                    context,
                    io2SlaveId.Value,
                    Sensor2OutputRegister,
                    0,
                    CancellationToken.None,
                    "InOut.Finally.Sensor2",
                    verificationTimeoutMs: Math.Min(_stateTimeoutMs, CleanupVerifyTimeoutMs));
            }

            var relaySafe = !_checkRelay || await SendRelayCommandAsync(
                context,
                0,
                CancellationToken.None,
                "InOut.Finally.Relay");

            if (_checkRelay && relaySafe && io2SlaveId != null)
            {
                relaySafe = await WriteIo2RegisterAsync(context, io2SlaveId.Value,
                    RelayInputRegister, 0, CancellationToken.None, "InOut.Finally.Relay.ResetInput",
                    verificationTimeoutMs: Math.Min(_stateTimeoutMs, CleanupVerifyTimeoutMs));
            }

            context.SetVariable("InOut.CleanupOutputsOff", outputsSafe);
            context.SetVariable("InOut.CleanupRelayOff", relaySafe);
            context.SetVariable("InOut.CleanupOk", outputsSafe && relaySafe);

            if (!outputsSafe || !relaySafe)
            {
                context.SetVariable("InOut.Ok", false);
                if (testPassed)
                {
                    context.SetVariable("InOut.Error", "Проверка прошла, но не удалось вернуть цепи IO-2/DUT в безопасное состояние.");
                }

                _logger.Error("[ОШИБКА] Не удалось полностью вернуть IO-2 или реле DUT в безопасное состояние.");
            }

            return outputsSafe && relaySafe;
        }

        private StepResult Fail(TestContext context, string error)
        {
            context.SetVariable("InOut.Ok", false);
            context.SetVariable("InOut.Error", error);
            _logger.Warning($"[ОШИБКА] {error}");
            return StepResult.False;
        }
    }
}
