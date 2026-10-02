using System;
using System.Collections.ObjectModel;
using System.Threading;

namespace TestBuilder.Services.Logging;

/// <summary>Captures step diagnostics before they are queued to the UI, isolated by async execution flow.</summary>
internal sealed class StepLogScope : IDisposable
{
    private static readonly AsyncLocal<StepLogScope?> Current = new();
    private readonly StepLogScope? _parent;
    private readonly object _gate = new();
    private string? _lastMessage;
    private string? _lastDiagnostic;
    private bool _disposed;

    public StepLogScope()
    {
        _parent = Current.Value;
        Current.Value = this;
    }

    public string Reason
    {
        get
        {
            lock (_gate)
                return _lastDiagnostic ?? _lastMessage ?? "Проверка не пройдена: шаг вернул False без пояснения.";
        }
    }

    public static void Record(LogLevel level, string message)
    {
        var scope = Current.Value;
        if (scope == null || string.IsNullOrWhiteSpace(message)) return;
        lock (scope._gate)
        {
            if (scope._disposed) return;
            scope._lastMessage = message;
            if (level >= LogLevel.Warning || message.Contains("[ОШИБКА]") || message.Contains("[ERROR]"))
                scope._lastDiagnostic = message;
        }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        Current.Value = _parent;
    }
}

internal sealed class StepDiagnosticLogger(ILogger inner) : ILogger
{
    public string Category => inner.Category;
    public ObservableCollection<LogEntry> Entries => inner.Entries;
    public void Log(LogLevel level, string message)
    {
        StepLogScope.Record(level, message);
        inner.Log(level, message);
    }
    public void Trace(string message) => Log(LogLevel.Trace, message);
    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warning(string message) => Log(LogLevel.Warning, message);
    public void Error(string message) => Log(LogLevel.Error, message);
    public void Clear() => inner.Clear();
}
