using System;
using System.Diagnostics;
using System.IO;
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

            if (_runArpdBat)
            {
                var bat = await RunProcessAsync(ResolveConfiguredPath(_arpdBatPath), string.Empty, cancellationToken);
                _logger.Info($"arpd.bat: exit {bat.ExitCode}, stdout: {bat.StdOut}, stderr: {bat.StdErr}");
            }

            var result = await RunProcessAsync(_command, _arguments, cancellationToken);
            var success = result.ExitCode == 0;

            context.SetVariable("ArpClear.ExitCode", result.ExitCode);
            context.SetVariable("ArpClear.StdOut", result.StdOut);
            context.SetVariable("ArpClear.StdErr", result.StdErr);
            context.SetVariable("ArpClear.Success", success);

            if (success)
            {
                _logger.Info("[OK] arp-таблица очищена.");
                return StepResult.True;
            }

            var error = $"Очистка ARP завершилась с кодом {result.ExitCode}. stderr: {result.StdErr}";
            _logger.Warning($"[ОШИБКА] {error}");
            return _failOnError ? StepResult.False : StepResult.True;
        }

        private async Task<ProcessRunResult> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
        {
            try
            {
                var startInfo = CreateStartInfo(fileName, arguments);

                using var process = new Process
                {
                    StartInfo = startInfo
                };

                process.Start();

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_timeoutMs);

                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    TryKill(process);
                    return new ProcessRunResult(-1, string.Empty, $"Таймаут процесса {fileName}: {_timeoutMs} мс.");
                }

                var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);

                return new ProcessRunResult(process.ExitCode, stdout.Trim(), stderr.Trim());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new ProcessRunResult(-1, string.Empty, ex.Message);
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
            }
        }

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
                CreateNoWindow = true
            };
        }

        private sealed record ProcessRunResult(int ExitCode, string StdOut, string StdErr);
    }
}
