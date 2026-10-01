using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public sealed class ClearArpCacheStep : ITestStep
    {
        private readonly ILogger _logger;
        private readonly bool _runArpdBat;
        private readonly string _arpdBatPath;
        private readonly string _command;
        private readonly string _arguments;
        private readonly int _timeoutMs;
        private readonly bool _failOnError;

        public ClearArpCacheStep(
            ILogger logger,
            bool runArpdBat,
            string arpdBatPath,
            string command,
            string arguments,
            int timeoutMs,
            bool failOnError)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _runArpdBat = runArpdBat;
            _arpdBatPath = string.IsNullOrWhiteSpace(arpdBatPath) ? "arpd.bat" : arpdBatPath.Trim();
            _command = string.IsNullOrWhiteSpace(command) ? "arp" : command.Trim();
            _arguments = NormalizeArguments(_command, arguments);
            _timeoutMs = Math.Max(1, timeoutMs);
            _failOnError = failOnError;
        }

        public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            _logger.Info("[ШАГ] Очистка ARP-таблицы.");

            ProcessRunResult? bat = null;
            if (_runArpdBat)
            {
                bat = await RunProcessAsync(ResolveConfiguredPath(_arpdBatPath), string.Empty, cancellationToken);
                var batMessage = $"arpd.bat: exit {bat.ExitCode}, stdout: {bat.StdOut}, stderr: {bat.StdErr}";
                if (IsSuccessfulProcess(bat.ExitCode, bat.StdErr))
                {
                    _logger.Info(batMessage);
                }
                else
                {
                    _logger.Warning($"[ОШИБКА] {batMessage}");
                }
            }

            var result = await RunProcessAsync(_command, _arguments, cancellationToken);
            var batSuccess = bat == null || IsSuccessfulProcess(bat.ExitCode, bat.StdErr);
            var commandSuccess = IsSuccessfulProcess(result.ExitCode, result.StdErr);
            var success = batSuccess && commandSuccess;

            context.SetVariable("ArpClear.ExitCode", result.ExitCode);
            context.SetVariable("ArpClear.StdOut", result.StdOut);
            context.SetVariable("ArpClear.StdErr", result.StdErr);
            context.SetVariable("ArpClear.BatExitCode", bat?.ExitCode ?? 0);
            context.SetVariable("ArpClear.BatStdOut", bat?.StdOut ?? string.Empty);
            context.SetVariable("ArpClear.BatStdErr", bat?.StdErr ?? string.Empty);
            context.SetVariable("ArpClear.Success", success);

            if (success)
            {
                _logger.Info("[OK] arp-таблица очищена.");
                return StepResult.True;
            }

            var error = batSuccess
                ? $"Очистка ARP завершилась с кодом {result.ExitCode}. stderr: {result.StdErr}"
                : $"arpd.bat не очистил ARP (код {bat!.ExitCode}). stderr: {bat.StdErr}";
            _logger.Warning($"[ОШИБКА] {error}");
            return _failOnError ? StepResult.False : StepResult.True;
        }

        internal static bool IsSuccessfulProcess(int exitCode, string? stdErr)
        {
            return exitCode == 0 && string.IsNullOrWhiteSpace(stdErr);
        }

        private async Task<ProcessRunResult> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
        {
            using var process = new Process { StartInfo = CreateStartInfo(fileName, arguments) };
            using var outputCts = new CancellationTokenSource();
            var started = false;
            Task<string> stdout = Task.FromResult(string.Empty);
            Task<string> stderr = Task.FromResult(string.Empty);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                started = process.Start();
                if (!started) return new ProcessRunResult(-1, string.Empty, $"Не удалось запустить {fileName}.");

                // Drain both pipes while the command is running; waiting for exit first can
                // deadlock when a command fills either OS pipe buffer.
                stdout = process.StandardOutput.ReadToEndAsync(outputCts.Token);
                stderr = process.StandardError.ReadToEndAsync(outputCts.Token);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_timeoutMs);
                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token);
                    await Task.WhenAll(stdout, stderr).WaitAsync(timeoutCts.Token);
                    return new ProcessRunResult(process.ExitCode, stdout.Result.Trim(), stderr.Result.Trim());
                }
                catch (OperationCanceledException cancellation)
                {
                    // Returning from a cancelled branch is a barrier: no ARP helper may
                    // remain active when the executor starts emergency shutdown.
                    try { await StopAndDrainAsync(process, stdout, stderr, outputCts); }
                    catch (ProcessCleanupException cleanup)
                    {
                        throw new ProcessCleanupException(
                            $"Не удалось остановить процесс {fileName} после отмены или таймаута.",
                            new AggregateException(cancellation, cleanup));
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    return new ProcessRunResult(-1, stdout.Result.Trim(),
                        $"Таймаут процесса {fileName}: {_timeoutMs} мс. {stderr.Result.Trim()}".Trim());
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (ProcessCleanupException) { throw; } // Cleanup failures are never downgraded by FailOnError.
            catch (Exception error)
            {
                if (started)
                {
                    try { await StopAndDrainAsync(process, stdout, stderr, outputCts); }
                    catch (ProcessCleanupException cleanup)
                    {
                        throw new ProcessCleanupException($"Не удалось остановить процесс {fileName} после ошибки.",
                            new AggregateException(error, cleanup));
                    }
                }
                return new ProcessRunResult(-1, string.Empty, error.Message);
            }
        }

        private static async Task StopAndDrainAsync(Process process, Task<string> stdout, Task<string> stderr,
            CancellationTokenSource outputCts)
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Exception? cleanupError = null;
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // The process exited between HasExited and Kill.
            }
            catch (Exception error) { cleanupError = error; }
            try { await process.WaitForExitAsync(cleanupCts.Token); }
            catch (Exception error)
            {
                cleanupError = cleanupError == null ? error : new AggregateException(cleanupError, error);
            }
            try { await Task.WhenAll(stdout, stderr).WaitAsync(cleanupCts.Token); }
            catch (Exception error)
            {
                cleanupError = cleanupError == null ? error : new AggregateException(cleanupError, error);
                // A descendant retaining inherited handles must not keep managed pipe reads alive.
                outputCts.Cancel();
                process.StandardOutput.Dispose();
                process.StandardError.Dispose();
                try { await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(1)); }
                catch { /* The original drain/termination error below remains the cause. */ }
            }
            if (cleanupError != null)
                throw new ProcessCleanupException($"Процесс PID {process.Id} или его потоки не завершились корректно.", cleanupError);
        }

        private sealed class ProcessCleanupException(string message, Exception innerException)
            : InvalidOperationException(message, innerException);

        private static string NormalizeArguments(string command, string? arguments)
        {
            var normalized = string.IsNullOrWhiteSpace(arguments) ? "-d *" : arguments.Trim();

            return IsArpCommand(command) && string.Equals(normalized, "-d", StringComparison.OrdinalIgnoreCase)
                ? "-d *"
                : normalized;
        }

        private static bool IsArpCommand(string command)
        {
            var fileName = Path.GetFileNameWithoutExtension(command);
            return string.Equals(fileName, "arp", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveConfiguredPath(string fileName)
        {
            if (Path.IsPathRooted(fileName) || File.Exists(fileName))
            {
                return fileName;
            }

            var appPath = Path.Combine(AppContext.BaseDirectory, fileName);
            return File.Exists(appPath) ? appPath : fileName;
        }

        private static ProcessStartInfo CreateStartInfo(string fileName, string arguments)
        {
            var extension = Path.GetExtension(fileName);
            var outputEncoding = ResolveProcessOutputEncoding(
                OperatingSystem.IsWindows(),
                CultureInfo.CurrentCulture.TextInfo.OEMCodePage);

            if (string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase))
            {
                var batchCommand = string.IsNullOrWhiteSpace(arguments)
                    ? $"\"{fileName}\""
                    : $"\"{fileName}\" {arguments}";

                return new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/d /c \"{batchCommand}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = outputEncoding,
                    StandardErrorEncoding = outputEncoding,
                    CreateNoWindow = true
                };
            }

            return new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = outputEncoding,
                StandardErrorEncoding = outputEncoding,
                CreateNoWindow = true
            };
        }

        internal static Encoding ResolveProcessOutputEncoding(bool isWindows, int oemCodePage)
        {
            if (!isWindows)
            {
                return Encoding.UTF8;
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            try
            {
                return Encoding.GetEncoding(
                    oemCodePage,
                    EncoderFallback.ReplacementFallback,
                    DecoderFallback.ReplacementFallback);
            }
            catch (ArgumentException)
            {
                return Encoding.UTF8;
            }
        }

        private sealed record ProcessRunResult(int ExitCode, string StdOut, string StdErr);
    }
}
