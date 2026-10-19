using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Services;
public sealed record ProgramResult(int ExitCode, string Output, string Error);
public interface IExternalProgramRunner
{
    Task<ProgramResult> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory, int timeoutMs, CancellationToken token);
}
public sealed class ExternalProgramRunner : IExternalProgramRunner
{
    public static ProcessStartInfo CreateStartInfo(string executable, IReadOnlyList<string> arguments, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException("Укажите исполняемый файл.");
        var info = new ProcessStartInfo { FileName = executable, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (!string.IsNullOrWhiteSpace(workingDirectory)) info.WorkingDirectory = workingDirectory;
        if (Path.GetExtension(executable).ToLowerInvariant() is ".bat" or ".cmd")
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("BAT/CMD запускаются только в Windows.");
            // cmd parses a command string even through ArgumentList. Reject expansion and operators.
            foreach (var value in arguments.Prepend(executable))
                if (value.IndexOfAny(new[] { '"', '&', '|', '<', '>', '^', '%', '!', '\r', '\n', '\0' }) >= 0)
                    throw new InvalidOperationException("Аргументы BAT/CMD содержат управляющие символы командной строки.");
            info.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            info.Arguments = "/d /s /c \"\"" + executable + "\" " + string.Join(" ", arguments.Select(x => "\"" + x + "\"")) + "\"";
        }
        else foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }
    public async Task<ProgramResult> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory, int timeoutMs, CancellationToken token)
    {
        if (timeoutMs is < 100 or > 3600000) throw new InvalidOperationException("Таймаут процесса: 100..3600000 мс.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(timeoutMs);
        using var job = OperatingSystem.IsWindows() ? new WindowsProcessJob() : null;
        using var process = new Process { StartInfo = CreateStartInfo(executable, arguments, workingDirectory) };
        var started = false;
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Процесс не запущен.");
            started = true;
            job?.Attach(process);
            async Task<string> CaptureAsync(StreamReader reader)
            {
                var text = new StringBuilder(); var buffer = new char[4096];
                while (true)
                {
                    var count = await reader.ReadAsync(buffer.AsMemory(), timeout.Token);
                    if (count == 0) return text.ToString();
                    if (text.Length + count > 1_048_576) { timeout.Cancel(); throw new InvalidOperationException("Вывод процесса превышает 1 МБ."); }
                    text.Append(buffer, 0, count);
                }
            }
            var stdout = CaptureAsync(process.StandardOutput); var stderr = CaptureAsync(process.StandardError);
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), stdout, stderr);
            return new ProgramResult(process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("Превышено время выполнения программы или лимит её вывода."); }
        finally
        {
            job?.Dispose(); // Terminates owned descendants even after the direct parent exits.
            if (started && !process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
            }
        }
    }
}
