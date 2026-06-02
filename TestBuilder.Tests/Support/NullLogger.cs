using System.Collections.ObjectModel;
using TestBuilder.Services.Logging;

namespace TestBuilder.Tests.Support;

public sealed class NullLogger : ILogger
{
    public static NullLogger Instance { get; } = new();

    public string Category => "Test";

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public void Log(LogLevel level, string message) { }
    public void Trace(string message) { }
    public void Debug(string message) { }
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message) { }
    public void Clear() { }
}
