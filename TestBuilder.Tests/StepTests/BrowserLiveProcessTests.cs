using System.Diagnostics;
using TestBuilder.Domain.Steps;

namespace TestBuilder.Tests.StepTests;

[Collection("Browser process lifecycle")]
public class BrowserLiveProcessTests
{
    // Opt-in: uses only a data URL and fresh temporary profiles, never the user's profile or DUT.
    [LiveBrowserFact]
    public async Task ParallelReadsCancellationAndTimeout_LeaveNoOwnedBrowserRunning()
    {
        var executable = Environment.GetEnvironmentVariable("TFORTIS_BROWSER_TEST_EXECUTABLE")!;
        var processes = new List<Process>();
        var overlapped = false;
        var cancelledStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Record(string stage)
        {
            if (!stage.StartsWith("Chrome PID ", StringComparison.Ordinal)) return;
            lock (processes)
            {
                overlapped |= processes.Any(process => !process.HasExited);
                processes.Add(Process.GetProcessById(int.Parse(stage.Split(' ')[2])));
                if (processes.Count == 3) cancelledStarted.TrySetResult();
            }
        }
        const string url = "data:text/html,<html><body>TFortisBrowserLifecycleFixture</body></html>";
        try
        {
            var first = SelfTestCheckStep.GetPageWithBrowserAsync(executable, url, TimeSpan.FromSeconds(45), CancellationToken.None, Record);
            var second = SelfTestCheckStep.GetPageWithBrowserAsync(executable, url, TimeSpan.FromSeconds(45), CancellationToken.None, Record);
            foreach (var result in await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(55)))
            {
                Assert.Equal(0, result.StatusCode);
                Assert.True(string.IsNullOrEmpty(result.ErrorMessage), result.ErrorMessage);
                Assert.Contains("TFortisBrowserLifecycleFixture", result.Body);
            }
            Assert.Equal(2, processes.Count);
            Assert.All(processes, process => Assert.True(process.HasExited));

            using var stop = new CancellationTokenSource();
            var cancelled = SelfTestCheckStep.GetPageWithBrowserAsync(executable, url, TimeSpan.FromSeconds(30), stop.Token, Record);
            await cancelledStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WaitAsync(TimeSpan.FromSeconds(8)));
            Assert.All(processes, process => Assert.True(process.HasExited));

            var timedOut = await SelfTestCheckStep.GetPageWithBrowserAsync(executable, url, TimeSpan.FromMilliseconds(200), CancellationToken.None, Record);
            Assert.False(timedOut.IsSuccessStatusCode);
            Assert.Equal(4, processes.Count);
            Assert.False(overlapped);
            Assert.All(processes, process => Assert.True(process.HasExited));
        }
        finally
        {
            foreach (var process in processes)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                process.Dispose();
            }
        }
    }

    private sealed class LiveBrowserFactAttribute : FactAttribute
    {
        public LiveBrowserFactAttribute()
        {
            if (!File.Exists(Environment.GetEnvironmentVariable("TFORTIS_BROWSER_TEST_EXECUTABLE")))
                Skip = "Set TFORTIS_BROWSER_TEST_EXECUTABLE to opt into a local Chrome/Edge lifecycle test.";
        }
    }
}
