using System;
using System.Collections.ObjectModel;
using System.Threading;
using TestBuilder.Domain.Execution;

namespace TestBuilder.Services.Logging;

/// <summary>Captures step diagnostics before they are queued to the UI, isolated by async execution flow.</summary>
internal sealed class StepLogScope : IDisposable
{
    private static readonly AsyncLocal<StepLogScope?> Current = new();
    private readonly StepLogScope? _parent;
    private readonly object _gate = new();
    private readonly TestErrorCode _defaultCode;
    private string? _lastMessage;
    private string? _lastDiagnostic;
    private TestErrorCode? _lastCode;
    private bool _disposed;

    public StepLogScope(TestErrorCode defaultCode)
    {
        _parent = Current.Value;
        _defaultCode = defaultCode;
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

    public TestErrorCode Code { get { lock (_gate) return _lastCode ?? _defaultCode; } }
    internal static TestErrorCode? CurrentStepCode => Current.Value?._defaultCode;

    public static TestErrorCode? Record(LogLevel level, string message, TestErrorCode? code)
    {
        var scope = Current.Value;
        if (scope == null || string.IsNullOrWhiteSpace(message)) return code;
        lock (scope._gate)
        {
            if (scope._disposed) return code;
            if (LogEntry.IsErrorMessage(level, message))
                code ??= scope._defaultCode;
            scope._lastMessage = message;
            if (level >= LogLevel.Warning || code.HasValue)
            {
                scope._lastDiagnostic = message;
                scope._lastCode = code;
            }
            return code;
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
    public void Log(LogLevel level, string message) => Log(level, message, null);
    public void Log(LogLevel level, string message, TestErrorCode? code) =>
        inner.Log(level, message, StepLogScope.Record(level, message, code));
    public void Trace(string message) => Log(LogLevel.Trace, message);
    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warning(string message) => Log(LogLevel.Warning, message);
    public void Error(string message) => Log(LogLevel.Error, message);
    public void Clear() => inner.Clear();
}
