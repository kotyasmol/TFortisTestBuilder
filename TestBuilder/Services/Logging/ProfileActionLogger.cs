using System.Collections.ObjectModel;
namespace TestBuilder.Services.Logging;

internal sealed class ProfileActionLogger(ILogger inner, string name) : ILogger
{
    public string Category => inner.Category;
    public ObservableCollection<LogEntry> Entries => inner.Entries;
    public void Log(LogLevel level, string message) => inner.Log(level, $"[Кнопка: {name}] {message}");
    public void Trace(string m) => Log(LogLevel.Trace, m);
    public void Debug(string m) => Log(LogLevel.Debug, m);
    public void Info(string m) => Log(LogLevel.Info, m);
    public void Warning(string m) => Log(LogLevel.Warning, m);
    public void Error(string m) => Log(LogLevel.Error, m);
    public void Clear() { }
}
