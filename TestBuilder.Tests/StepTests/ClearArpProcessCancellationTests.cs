using System.Diagnostics;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

/// <summary>Uses disposable local dummy processes; no ARP command or network operation is invoked.</summary>
public class ClearArpProcessCancellationTests
{
    [Fact]
    public async Task Cancellation_WaitsForDummyProcessExitBeforeReturning()
    {
        using var dummy = new DummyCommand(wait: true);
        using var cancellation = new CancellationTokenSource();
        var execution = dummy.Step(timeoutMs: 30000).ExecuteAsync(Context(), cancellation.Token);
        using var process = await dummy.GetStartedProcessAsync();
        try
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(process.HasExited, "Cancellation returned while the child process was still running.");
        }
        finally { KillIfRunning(process); }
    }

    [Fact]
    public async Task Timeout_WaitsForDummyProcessExitAndReturnsFailure()
    {
        using var dummy = new DummyCommand(wait: true);
        var context = Context();
        var execution = dummy.Step(timeoutMs: 3000).ExecuteAsync(context, CancellationToken.None);
        using var process = await dummy.GetStartedProcessAsync();
        try
        {
            Assert.Equal(StepResult.False, await execution.WaitAsync(TimeSpan.FromSeconds(12)));
            Assert.True(process.HasExited, "Timeout returned while the child process was still running.");
            Assert.Contains("Таймаут", context.GetVariable<string>("ArpClear.StdErr"));
            Assert.False(context.GetVariable<bool>("ArpClear.Success"));
        }
        finally { KillIfRunning(process); }
    }

    [Fact]
    public async Task LargeStandardOutputAndError_AreDrainedWhileProcessRuns()
    {
        using var dummy = new DummyCommand(wait: false);
        var context = Context();
        await dummy.Step(timeoutMs: 15000).ExecuteAsync(context, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(0, context.GetVariable<int>("ArpClear.ExitCode"));
        Assert.True(context.GetVariable<string>("ArpClear.StdOut")!.Length >= 262144);
        Assert.True(context.GetVariable<string>("ArpClear.StdErr")!.Length >= 262144);
    }

    private static TestContext Context() => new(new RegisterState());
    private static void KillIfRunning(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }

    private sealed class DummyCommand : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "TestBuilderProcessTest_" + Guid.NewGuid().ToString("N"));
        private readonly string _script;
        private readonly string _pidFile;

        public DummyCommand(bool wait)
        {
            Directory.CreateDirectory(_directory);
            _pidFile = Path.Combine(_directory, "pid.txt");
            _script = Path.Combine(_directory, OperatingSystem.IsWindows() ? "dummy.ps1" : "dummy.sh");
            var body = OperatingSystem.IsWindows()
                ? $"[System.IO.File]::WriteAllText('{_pidFile.Replace("'", "''")}', $PID.ToString())\n" +
                  (wait ? "Start-Sleep -Seconds 30\n" : "[Console]::Out.Write(('x' * 262144)); [Console]::Error.Write(('y' * 262144))\n")
                : $"printf '%s' \"$$\" > '{_pidFile.Replace("'", "'\\''")}'\n" +
                  (wait ? "exec sleep 30\n" : "awk 'BEGIN {for(i=0;i<262144;i++) printf \"x\"}'; awk 'BEGIN {for(i=0;i<262144;i++) printf \"y\"}' >&2\n");
            File.WriteAllText(_script, body);
        }

        public ClearArpCacheStep Step(int timeoutMs) => new(NullLogger.Instance, false, string.Empty,
            OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh",
            OperatingSystem.IsWindows()
                ? $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{_script}\""
                : $"\"{_script}\"",
            timeoutMs, failOnError: true);

        public async Task<Process> GetStartedProcessAsync()
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (true)
            {
                if (File.Exists(_pidFile) && int.TryParse(await File.ReadAllTextAsync(_pidFile, limit.Token), out var pid))
                    return Process.GetProcessById(pid);
                await Task.Delay(10, limit.Token);
            }
        }

        public void Dispose()
        {
            // Also clean up a launched dummy if an assertion failed before obtaining its handle.
            if (File.Exists(_pidFile) && int.TryParse(File.ReadAllText(_pidFile), out var pid))
            {
                try { using var process = Process.GetProcessById(pid); KillIfRunning(process); }
                catch (ArgumentException) { }
            }
            Directory.Delete(_directory, recursive: true);
        }
    }
}
