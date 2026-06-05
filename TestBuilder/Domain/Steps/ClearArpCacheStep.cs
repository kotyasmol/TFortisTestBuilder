using System;
using System.Diagnostics;
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
            _arguments = arguments ?? "-d";
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
                var bat = await RunProcessAsync(_arpdBatPath, string.Empty, cancellationToken);
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
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
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

        private sealed record ProcessRunResult(int ExitCode, string StdOut, string StdErr);
    }
}
