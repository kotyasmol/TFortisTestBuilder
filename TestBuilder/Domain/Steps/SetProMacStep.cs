using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public sealed class SetProMacStep : ITestStep
    {
        private const string SuccessMarker = "SUCCESSFUL";
        private readonly ILogger _logger;
        private readonly string _batchPath;
        private readonly string _macVariableName;
        private readonly string _boardVersion;
        private readonly int _timeoutMs;
        private readonly bool _failOnError;
        private readonly Func<SetProMacProcessRequest, CancellationToken, Task<SetProMacProcessResult>> _processRunner;
        private readonly Func<long> _timestampProvider;

        public SetProMacStep(
            ILogger logger,
            string batchPath,
            string macVariableName,
            string boardVersion,
            int timeoutMs,
            bool failOnError)
            : this(
                logger,
                batchPath,
                macVariableName,
                boardVersion,
                timeoutMs,
                failOnError,
                RunProcessAsync,
                static () => DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
        }

        internal SetProMacStep(
            ILogger logger,
            string batchPath,
            string macVariableName,
            string boardVersion,
            int timeoutMs,
            bool failOnError,
            Func<SetProMacProcessRequest, CancellationToken, Task<SetProMacProcessResult>> processRunner,
            Func<long> timestampProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _batchPath = NormalizeBatchPath(batchPath);
            _macVariableName = string.IsNullOrWhiteSpace(macVariableName) ? "Dut.NewMac" : macVariableName.Trim();
            _boardVersion = boardVersion?.Trim() ?? string.Empty;
            _timeoutMs = Math.Max(1, timeoutMs);
            _failOnError = failOnError;
            _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
            _timestampProvider = timestampProvider ?? throw new ArgumentNullException(nameof(timestampProvider));
        }

        public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            ResetResult(context);
            cancellationToken.ThrowIfCancellationRequested();

            if (!context.Variables.TryGetValue(_macVariableName, out var rawMac) || rawMac == null)
            {
                return Fail(context, $"MAC-переменная '{_macVariableName}' не найдена.");
            }

            if (!TryNormalizeMac(rawMac.ToString() ?? string.Empty, out var normalizedMac))
            {
                return Fail(context, $"Некорректный MAC: '{rawMac}'. Ожидаются ровно 6 байт, например C0:11:A6:20:01:AC.");
            }

            if (!TryValidateArgument(_boardVersion, "Версия платы", out var argumentError))
            {
                return Fail(context, argumentError);
            }

            if (!TryValidateBatchPath(_batchPath, out var pathError))
            {
                return Fail(context, pathError);
            }

            var timestamp = _timestampProvider();
            if (timestamp <= 0)
            {
                return Fail(context, $"Некорректный Unix timestamp: {timestamp}.");
            }

            var resolvedBatchPath = ResolveConfiguredPath(_batchPath);
            var request = new SetProMacProcessRequest(
                resolvedBatchPath,
                normalizedMac,
                _boardVersion,
                timestamp,
                _timeoutMs);

            context.SetVariable("SetMac.Method", "ProBatch");
            context.SetVariable("SetMac.Mac", normalizedMac);
            context.SetVariable("SetMac.BoardVersion", _boardVersion);
            context.SetVariable("SetMac.Timestamp", timestamp);
            context.SetVariable("SetMac.BatchPath", resolvedBatchPath);

            _logger.Info(
                $"[ШАГ] Запись MAC Pro: запуск '{resolvedBatchPath}' с аргументами " +
                $"MAC={normalizedMac}, boardversion='{_boardVersion}', timestamp={timestamp}; таймаут {_timeoutMs} мс.");

            SetProMacProcessResult processResult;
            try
            {
                processResult = await _processRunner(request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Fail(context, $"Не удалось запустить set_mac_pro.bat: {ex.Message}");
            }

            var stdout = LimitOutput(processResult.StdOut);
            var stderr = LimitOutput(processResult.StdErr);
            context.SetVariable("SetMac.Started", processResult.Started);
            context.SetVariable("SetMac.TimedOut", processResult.TimedOut);
            context.SetVariable("SetMac.ExitCode", processResult.ExitCode);
            context.SetVariable("SetMac.StdOut", stdout);
            context.SetVariable("SetMac.StdErr", stderr);

            if (processResult.Started)
            {
                _logger.Info($"set_mac_pro.bat: exit {processResult.ExitCode}, stdout: {FormatForLog(stdout)}, stderr: {FormatForLog(stderr)}");
            }

            if (processResult.TimedOut)
            {
                return Fail(context, $"set_mac_pro.bat не завершился за {_timeoutMs} мс и был остановлен. stderr: {FormatForLog(stderr)}");
            }

            if (!processResult.Started)
            {
                return Fail(context, string.IsNullOrWhiteSpace(stderr)
                    ? "set_mac_pro.bat не был запущен."
                    : stderr);
            }

            if (processResult.ExitCode != 0)
            {
                return Fail(context, $"set_mac_pro.bat завершился с кодом {processResult.ExitCode}. stderr: {FormatForLog(stderr)}");
            }

            if (!ContainsSuccessMarker(processResult.StdOut))
            {
                return Fail(
                    context,
                    $"set_mac_pro.bat вернул код 0, но не напечатал маркер {SuccessMarker}. " +
                    "Команды WinSCP нельзя считать подтверждёнными.");
            }

            context.SetVariable("SetMac.Success", true);
            context.SetVariable("SetMac.Error", string.Empty);
            _logger.Info(
                $"[OK] set_mac_pro.bat подтвердил выполнение команд записи MAC {normalizedMac}. " +
                "Сохранение значения проверяется selftest после перезапуска DUT.");
            return StepResult.True;
        }

        internal static bool TryNormalizeMac(string value, out string normalized)
        {
            normalized = string.Empty;
            var compact = value.Trim().Replace(":", string.Empty).Replace("-", string.Empty);
            if (compact.Length != 12 || compact.Any(c => !Uri.IsHexDigit(c)))
            {
                return false;
            }

            var bytes = new byte[6];
            for (var i = 0; i < bytes.Length; i++)
            {
                if (!byte.TryParse(
                        compact.AsSpan(i * 2, 2),
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out bytes[i]))
                {
                    return false;
                }
            }

            normalized = string.Join(":", bytes.Select(valueByte => valueByte.ToString("X2", CultureInfo.InvariantCulture)));
            return true;
        }

        internal static bool ContainsSuccessMarker(string? stdout) =>
            stdout?.Contains(SuccessMarker, StringComparison.OrdinalIgnoreCase) == true;

        internal static string BuildCmdArguments(SetProMacProcessRequest request)
        {
            var command =
                $"\"{request.BatchPath}\" \"{request.MacAddress}\" \"{request.BoardVersion}\" \"{request.Timestamp.ToString(CultureInfo.InvariantCulture)}\"";
            return $"/d /s /c \"{command}\"";
        }

        private static async Task<SetProMacProcessResult> RunProcessAsync(
            SetProMacProcessRequest request,
            CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsWindows())
            {
                return SetProMacProcessResult.NotStarted("Запись MAC Pro поддерживается только в Windows.");
            }

            if (!File.Exists(request.BatchPath))
            {
                return SetProMacProcessResult.NotStarted(
                    $"Файл '{request.BatchPath}' не найден. Укажите Batch path или положите set_mac_pro.bat рядом с TestBuilder.exe.");
            }

            try
            {
                var encoding = ClearArpCacheStep.ResolveProcessOutputEncoding(
                    isWindows: true,
                    CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = BuildCmdArguments(request),
                    WorkingDirectory = Path.GetDirectoryName(request.BatchPath) ?? AppContext.BaseDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = encoding,
                    StandardErrorEncoding = encoding,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                if (!process.Start())
                {
                    return SetProMacProcessResult.NotStarted("Windows не запустил set_mac_pro.bat.");
                }

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(request.TimeoutMs);

                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    TryKill(process);
                    var timedOutOutput = await ReadOutputAfterStopAsync(stdoutTask, stderrTask);
                    return new SetProMacProcessResult(
                        true,
                        true,
                        -1,
                        timedOutOutput.StdOut,
                        timedOutOutput.StdErr);
                }
                catch (OperationCanceledException)
                {
                    TryKill(process);
                    throw;
                }

                var output = await ReadOutputAfterStopAsync(stdoutTask, stderrTask);
                return new SetProMacProcessResult(
                    true,
                    false,
                    process.ExitCode,
                    output.StdOut,
                    output.StdErr);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return SetProMacProcessResult.NotStarted(ex.Message);
            }
        }

        private static async Task<(string StdOut, string StdErr)> ReadOutputAfterStopAsync(
            Task<string> stdoutTask,
            Task<string> stderrTask)
        {
            try
            {
                await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(3));
                return ((await stdoutTask).Trim(), (await stderrTask).Trim());
            }
            catch (TimeoutException)
            {
                return (string.Empty, "Процесс остановлен, но его stdout/stderr не закрылись за 3 секунды.");
            }
            catch (Exception ex)
            {
                return (string.Empty, $"Не удалось прочитать stdout/stderr процесса: {ex.Message}");
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // The process may have exited between HasExited and Kill.
            }
        }

        private static string ResolveConfiguredPath(string batchPath)
        {
            if (Path.IsPathRooted(batchPath) || File.Exists(batchPath))
            {
                return batchPath;
            }

            var appPath = Path.Combine(AppContext.BaseDirectory, batchPath);
            return File.Exists(appPath) ? appPath : batchPath;
        }

        private static string NormalizeBatchPath(string? batchPath)
        {
            var normalized = string.IsNullOrWhiteSpace(batchPath) ? "set_mac_pro.bat" : batchPath.Trim();
            if (normalized.Length >= 2 && normalized[0] == '"' && normalized[^1] == '"')
            {
                normalized = normalized[1..^1].Trim();
            }

            return string.IsNullOrWhiteSpace(normalized) ? "set_mac_pro.bat" : normalized;
        }

        private static bool TryValidateArgument(string value, string name, out string error)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                error = $"{name} не задана.";
                return false;
            }

            if (value.IndexOfAny(['\r', '\n', '\0', '"', '%', '!', '&', '|', '<', '>', '^']) >= 0)
            {
                error = $"{name} содержит недопустимые для bat-аргумента символы: '{value}'.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryValidateBatchPath(string value, out string error)
        {
            if (value.IndexOfAny(['\r', '\n', '\0', '"', '%']) >= 0)
            {
                error = $"Batch path содержит недопустимые символы: '{value}'.";
                return false;
            }

            if (!string.Equals(Path.GetExtension(value), ".bat", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(Path.GetExtension(value), ".cmd", StringComparison.OrdinalIgnoreCase))
            {
                error = $"Batch path должен указывать на .bat или .cmd: '{value}'.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static string LimitOutput(string? value)
        {
            const int maxLength = 8000;
            var normalized = value?.Trim() ?? string.Empty;
            return normalized.Length <= maxLength
                ? normalized
                : normalized[..maxLength] + " …(вывод обрезан)";
        }

        private static string FormatForLog(string value) =>
            string.IsNullOrWhiteSpace(value) ? "<пусто>" : value.ReplaceLineEndings(" | ");

        private void ResetResult(TestContext context)
        {
            context.SetVariable("SetMac.Method", "ProBatch");
            context.SetVariable("SetMac.Started", false);
            context.SetVariable("SetMac.TimedOut", false);
            context.SetVariable("SetMac.Success", false);
            context.SetVariable("SetMac.Mac", string.Empty);
            context.SetVariable("SetMac.BoardVersion", _boardVersion);
            context.SetVariable("SetMac.Timestamp", 0L);
            context.SetVariable("SetMac.BatchPath", _batchPath);
            context.SetVariable("SetMac.ExitCode", -1);
            context.SetVariable("SetMac.StdOut", string.Empty);
            context.SetVariable("SetMac.StdErr", string.Empty);
            context.SetVariable("SetMac.Error", string.Empty);
        }

        private StepResult Fail(TestContext context, string error)
        {
            context.SetVariable("SetMac.Success", false);
            context.SetVariable("SetMac.Error", error);
            _logger.Warning($"[ОШИБКА] MAC Pro не записан: {error}");
            return _failOnError ? StepResult.False : StepResult.True;
        }
    }

    internal sealed record SetProMacProcessRequest(
        string BatchPath,
        string MacAddress,
        string BoardVersion,
        long Timestamp,
        int TimeoutMs);

    internal sealed record SetProMacProcessResult(
        bool Started,
        bool TimedOut,
        int ExitCode,
        string StdOut,
        string StdErr)
    {
        public static SetProMacProcessResult NotStarted(string error) =>
            new(false, false, -1, string.Empty, error);
    }
}
