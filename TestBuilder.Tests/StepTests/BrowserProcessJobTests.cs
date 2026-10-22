using System.Diagnostics;
using System.Text;
using TestBuilder.Services;

namespace TestBuilder.Tests.StepTests;

public class BrowserProcessJobTests
{
    [Theory]
    [InlineData("", "\"\"")]
    [InlineData("C:\\Program Files\\Chrome\\", "\"C:\\Program Files\\Chrome\\\\\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData("a\\\"b", "\"a\\\\\\\"b\"")]
    public void WindowsArguments_PreserveQuotesSpacesAndTrailingSlashes(string value, string expected) =>
        Assert.Equal(expected, WindowsProcessJob.QuoteArgument(value));

    [WindowsFact]
    public async Task ParentExit_DoesNotLeaveChildAlive_OrKillUnrelatedProcess()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Browser job test " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var childPidFile = Path.Combine(directory, "child.txt");
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var childCode = Encode("Start-Sleep -Seconds 60");
        var unrelatedInfo = Command(powershell, childCode);
        using var unrelated = Process.Start(unrelatedInfo)!;
        try
        {
            // Repeated groups must all reach zero, including after their parent exited.
            for (var i = 0; i < 3; i++)
            {
                File.Delete(childPidFile);
                var script = "$p = Start-Process -FilePath '" + powershell.Replace("'", "''") +
                    "' -ArgumentList '-NoLogo -NoProfile -NonInteractive -EncodedCommand " + childCode + "' -PassThru; " +
                    "[IO.File]::WriteAllText('" + childPidFile.Replace("'", "''") + "', $p.Id.ToString())";
                using var job = new WindowsProcessJob();
                using var parent = job.Start(Command(powershell, Encode(script)));
                await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                Assert.Equal(0, parent.ExitCode);
                using var child = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(childPidFile)));
                Assert.False(child.HasExited);
                Assert.True(job.ActiveProcessCount >= 1);
                await job.TerminateAndWaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(0u, job.ActiveProcessCount);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                Assert.False(unrelated.HasExited);
            }
        }
        finally
        {
            if (!unrelated.HasExited) unrelated.Kill(entireProcessTree: true);
            await unrelated.WaitForExitAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    [WindowsFact]
    public async Task Dispose_KillsOwnedTreeEvenWithoutExplicitStop()
    {
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var job = new WindowsProcessJob();
        using var process = job.Start(Command(powershell, Encode("Start-Sleep -Seconds 60")));
        job.Dispose();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    private static ProcessStartInfo Command(string executable, string code)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", code }) info.ArgumentList.Add(arg);
        return info;
    }

    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Requires native Windows Job Objects."; }
    }
}
