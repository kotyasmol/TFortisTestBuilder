using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TestBuilder.Domain.Steps;

namespace TestBuilder.Tests.StepTests;

[Collection("Browser process lifecycle")]
public class BrowserLiveProcessTests
{
    [LiveBrowserFact]
    public async Task DelayedHttpDocument_IsReadAfterNavigationAndCompleteBody()
    {
        // Cover both a slow first byte (the completed about:blank race) and a
        // partially received document whose correct URL is already visible.
        foreach (var headersFirst in new[] { false, true })
        {
            var result = await ReadLocalResponseAsync(200, TimeSpan.FromSeconds(15), headersFirst);
            Assert.True(string.IsNullOrEmpty(result.ErrorMessage), result.ErrorMessage);
            Assert.Contains("TFortisDelayedDocumentEnd", result.Body);
            Assert.True(result.Elapsed >= TimeSpan.FromSeconds(25), result.Elapsed.ToString());
        }
    }

    [LiveBrowserFact]
    public async Task HttpError_IsReportedInsteadOfAcceptingItsCompleteDocument()
    {
        var result = await ReadLocalResponseAsync(503, TimeSpan.Zero, false);
        Assert.Contains("HTTP 503", result.ErrorMessage);
        Assert.Empty(result.Body);
    }

    private static async Task<TestBuilder.Services.Http.HttpRequestResult> ReadLocalResponseAsync(
        int status, TimeSpan delay, bool headersFirst)
    {
        using var stop = new CancellationTokenSource();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = ServeAsync();
        try
        {
            return await SelfTestCheckStep.GetPageWithBrowserAsync(
                Environment.GetEnvironmentVariable("TFORTIS_BROWSER_TEST_EXECUTABLE")!,
                $"http://127.0.0.1:{port}/deviceinfo", TimeSpan.FromSeconds(40), stop.Token);
        }
        finally
        {
            stop.Cancel();
            try { await server; }
            catch (OperationCanceledException) { }
        }

        async Task ServeAsync()
        {
            using var client = await listener.AcceptTcpClientAsync(stop.Token);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(stop.Token))) { }
            var body = Encoding.UTF8.GetBytes("<html><body>TFortisDelayedDocumentEnd</body></html>");
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status} Fixture\r\nContent-Type: text/html\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            if (headersFirst)
            {
                await stream.WriteAsync(header, stop.Token);
                await stream.WriteAsync(body.AsMemory(0, 12), stop.Token);
            }
            await Task.Delay(delay, stop.Token);
            if (!headersFirst) await stream.WriteAsync(header, stop.Token);
            await stream.WriteAsync(body.AsMemory(headersFirst ? 12 : 0), stop.Token);
        }
    }

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
