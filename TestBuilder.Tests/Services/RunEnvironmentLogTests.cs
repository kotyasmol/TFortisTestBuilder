using TestBuilder.Services.Logging;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.Services;

public class RunEnvironmentLogTests
{
    [Fact]
    public void FingerprintDetectsChangedGraphWithoutLoggingItsSecrets()
    {
        const string first = "{\"session\":\"sensitive-test-value\",\"node\":1}";
        Assert.NotEqual(RunEnvironmentLog.HashText(first), RunEnvironmentLog.HashText(first.Replace(":1", ":2")));
        var logger = new CaptureLogger();
        RunEnvironmentLog.Write(logger, null, () => first, "production", "Stand-17");
        var text = string.Join("\n", logger.Entries.Select(e => e.Message));
        Assert.Contains(Environment.MachineName, text);
        Assert.Contains("Stand-17", text);
        Assert.Contains(RunEnvironmentLog.HashText(first), text);
        Assert.DoesNotContain("sensitive-test-value", text);
    }

    [Fact]
    public void FingerprintFailureCannotStopTheRun()
    {
        RunEnvironmentLog.Write(new CaptureLogger(), null, () => throw new FormatException("invalid graph"), "production", "17");
    }

    private sealed class CaptureLogger : ILogger
    {
        public string Category => "Test";
        public System.Collections.ObjectModel.ObservableCollection<LogEntry> Entries { get; } = new();
        public void Log(LogLevel level, string message) => Entries.Add(new LogEntry(DateTime.UtcNow, level, Category, message));
        public void Info(string message) => Log(LogLevel.Info, message);
        public void Warning(string message) => Log(LogLevel.Warning, message);
        public void Error(string message) => Log(LogLevel.Error, message);
        public void Debug(string message) => Log(LogLevel.Debug, message);
        public void Trace(string message) => Log(LogLevel.Trace, message);
        public void Clear() => Entries.Clear();
    }
}
