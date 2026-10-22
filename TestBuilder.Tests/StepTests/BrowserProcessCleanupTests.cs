using System.Diagnostics;
using System.Globalization;
using TestBuilder.Domain.Steps;

namespace TestBuilder.Tests.StepTests;

[Collection("Browser process lifecycle")]
public class BrowserProcessCleanupTests
{
    [UnixFact]
    public async Task RepeatedTimeouts_StopEachOwnedProcessBeforeReturning()
    {
        using var browser = new DummyBrowser();
        for (var i = 0; i < 3; i++)
        {
            var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var attempt = SelfTestCheckStep.GetPageWithBrowserAsync(browser.Path, "http://127.0.0.1/unused",
                TimeSpan.FromMilliseconds(350), CancellationToken.None, stage => ReportPid(stage, started));
            using var process = Process.GetProcessById(await started.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            try
            {
                var result = await attempt.WaitAsync(TimeSpan.FromSeconds(6));
                Assert.False(result.IsSuccessStatusCode);
                Assert.True(process.HasExited, "A timed-out attempt left its browser running.");
            }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        }
    }

    [UnixFact]
    public async Task Cancellation_AndThrowingLogger_StillWaitForProcessExit()
    {
        using var browser = new DummyBrowser();
        using var stop = new CancellationTokenSource();
        var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempt = SelfTestCheckStep.GetPageWithBrowserAsync(browser.Path, "http://127.0.0.1/unused",
            TimeSpan.FromSeconds(30), stop.Token, stage =>
            {
                ReportPid(stage, started);
                if (stage.StartsWith("closing Chrome", StringComparison.Ordinal)) throw new IOException("log unavailable");
            });
        using var process = Process.GetProcessById(await started.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        try
        {
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt.WaitAsync(TimeSpan.FromSeconds(6)));
            Assert.True(process.HasExited);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    private static void ReportPid(string stage, TaskCompletionSource<int> started)
    {
        if (stage.StartsWith("Chrome PID ", StringComparison.Ordinal))
            started.TrySetResult(int.Parse(stage.Split(' ')[2], CultureInfo.InvariantCulture));
    }

    private sealed class DummyBrowser : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Browser cleanup " + Guid.NewGuid().ToString("N"));
        public string Path { get; }
        public DummyBrowser()
        {
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "dummy-browser");
            File.WriteAllText(Path, "#!/bin/sh\nexec sleep 60\n");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
    private sealed class UnixFactAttribute : FactAttribute
    {
        public UnixFactAttribute() { if (OperatingSystem.IsWindows()) Skip = "Uses a local Unix dummy executable; Windows ownership is covered by BrowserProcessJobTests."; }
    }
}
