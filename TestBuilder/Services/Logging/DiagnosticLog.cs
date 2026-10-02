using System;
using System.IO;
using System.Text;

namespace TestBuilder.Services.Logging;

/// <summary>Startup/UI failures must survive a restart, even before a test run starts.</summary>
internal static class DiagnosticLog
{
    private static readonly object WriteLock = new();

    internal static string? WriteException(string operation, Exception exception, string? directory = null)
    {
        try
        {
            directory ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TestBuilder", "logs");
            var now = DateTimeOffset.Now;
            var path = Path.Combine(directory, $"errors-{now:yyyyMMdd}.log");
            lock (WriteLock)
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(path,
                    $"[{now:O}] {operation}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}",
                    Encoding.UTF8);
            }
            return path;
        }
        catch
        {
            // Diagnostics must never replace the original failure with a file access error.
            return null;
        }
    }
}
