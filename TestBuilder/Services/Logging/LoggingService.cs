using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using TestBuilder.Domain.Execution;

namespace TestBuilder.Services.Logging
{
    /// <summary>
    /// Уровни логирования для UI‑логов.
    /// </summary>
    public enum LogLevel
    {
        Trace,
        Debug,
        Info,
        Warning,
        Error
    }

    public enum LogHighlight { None, Error, Success, Step, Warning }

    /// <summary>
    /// Одна запись лога.
    /// </summary>
    public sealed class LogEntry
    {
        public LogEntry(DateTime timestamp, LogLevel level, string category, string message, TestErrorCode? code = null)
        {
            Timestamp = timestamp;
            Level = level;
            Category = category ?? string.Empty;
            Message = message ?? string.Empty;

            // Error severity wins even when an exception quotes an earlier [OK] message.
            if (IsErrorMessage(level, Message))
                Highlight = LogHighlight.Error;
            else if (Message.Contains("[OK]"))
                Highlight = LogHighlight.Success;
            else if (Message.Contains("[ШАГ]"))
                Highlight = LogHighlight.Step;
            else if (level == LogLevel.Warning || Message.Contains("[ОСТАНОВ]"))
                Highlight = LogHighlight.Warning;
            Code = code ?? (IsError ? TestErrorCode.Unexpected : null);
        }

        public DateTime Timestamp { get; }
        public LogLevel Level { get; }
        public string Category { get; }
        public string Message { get; }
        public TestErrorCode? Code { get; }
        public string? CodeText => Code?.Format();
        public bool HasCode => Code.HasValue;
        public string DisplayText => $"[{Timestamp:HH:mm:ss}] {Message}";
        internal string DiagnosticText => CodeText is { } code ? $"{Message} [{code}]" : Message;

        public LogHighlight Highlight { get; }
        public bool IsError => Highlight == LogHighlight.Error;
        public bool IsSuccess => Highlight == LogHighlight.Success;
        public bool IsStep => Highlight == LogHighlight.Step;
        public bool IsWarning => Highlight == LogHighlight.Warning;
        public bool IsFailureSummary => Message.StartsWith("[ПРИЧИНА СБОЯ]", StringComparison.Ordinal);

        internal static bool IsErrorMessage(LogLevel level, string message) =>
            level == LogLevel.Error || message.Contains("[ОШИБКА]") || message.Contains("[ERROR]") ||
            message.StartsWith("[ПРИЧИНА СБОЯ]", StringComparison.Ordinal);

        public override string ToString() => $"[{Timestamp:HH:mm:ss}] {DiagnosticText}";
    }

    /// <summary>
    /// Логгер, который можно прямо привязывать к GUI.
    /// </summary>
    public interface ILogger
    {
        string Category { get; }

        /// <summary>
        /// Коллекция записей для привязки в XAML (ListBox / ItemsControl).
        /// </summary>
        ObservableCollection<LogEntry> Entries { get; }

        void Log(LogLevel level, string message);
        void Log(LogLevel level, string message, TestErrorCode? code) => Log(level, message);

        void Trace(string message);
        void Debug(string message);
        void Info(string message);
        void Warning(string message);
        void Error(string message);
        void Warning(string message, TestErrorCode code) => Log(LogLevel.Warning, message, code);
        void Error(string message, TestErrorCode code) => Log(LogLevel.Error, message, code);

        void Clear();
    }

    /// <summary>
    /// Сервис логирования. Через него создаются логгеры для отдельных ViewModel / вкладок.
    /// </summary>
    public interface ILoggingService
    {
        ILogger CreateLogger(string category);
    }

    /// <summary>
    /// Простая реализация сервера логирования для UI.
    /// </summary>
    public sealed class LoggingService : ILoggingService
    {
        /// <summary>
        /// Глобальный экземпляр сервиса. Можно использовать напрямую,
        /// либо подменить в тестах.
        /// </summary>
        public static LoggingService Instance { get; } = new LoggingService();

        private static readonly object FileLogLock = new();
        private static FileLog? _activeFileLog;
        private sealed record FileLog(string Path, Action<string>? ReportFailure);

        private LoggingService()
        {
        }

        public string? CurrentLogFilePath
        {
            get
            {
                lock (FileLogLock)
                {
                    return _activeFileLog?.Path;
                }
            }
        }

        /// <summary>
        /// Возвращает путь к файлу текущего прогона или null, если файл не инициализирован.
        /// </summary>
        public string? StartFileLogForRun(string? profileName) => StartFileLogForRun(profileName, null);

        internal string? StartFileLogForRun(string? profileName, Action<string>? reportFailure)
        {
            if (!AppSettings.Instance.EnableFileLogging)
            {
                return null;
            }

            var folder = AppSettings.Instance.LogFolder;
            if (string.IsNullOrWhiteSpace(folder))
            {
                folder = Path.Combine(AppContext.BaseDirectory, AppSettings.DefaultLogFolder);
            }

            string failure;
            lock (FileLogLock)
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    var filePath = Path.Combine(folder, BuildRunLogFileName(profileName));
                    using (var file = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    using (var writer = new StreamWriter(file))
                        writer.WriteLine(BuildHeaderLine(profileName));
                    _activeFileLog = new FileLog(filePath, reportFailure);
                    return filePath;
                }
                catch (Exception ex) when (IsFileError(ex))
                {
                    failure = $"Не удалось создать файл журнала в папке «{folder}»: {ex.Message}";
                }
            }

            reportFailure?.Invoke(failure);
            return null;
        }

        /// <summary>
        /// Закрывает текущий логовый файл прогона.
        /// </summary>
        public void StopFileLogForRun() => StopFileLogForRun(null);

        internal void StopFileLogForRun(string? expectedPath)
        {
            lock (FileLogLock)
            {
                if (expectedPath == null || _activeFileLog?.Path == expectedPath)
                    _activeFileLog = null;
            }
        }

        public ILogger CreateLogger(string category)
            => new Logger(category);

        internal ILogger CreateTrainingLogger() => new Logger("Обучение", false);

        private static string BuildRunLogFileName(string? profileName)
        {
            var safeProfile = NormalizeProfileName(profileName);
            return $"test-run_{DateTime.Now:yyyyMMdd_HHmmss}_{safeProfile}_{Guid.NewGuid():N}.txt";
        }

        private static string BuildHeaderLine(string? profileName)
            => $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] [System] Профиль проверки: {profileName ?? "Без имени профиля"}";

        private static string BuildLogLine(LogEntry entry)
            => $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level}] [{entry.Category}] {entry.DiagnosticText}";

        private static bool IsFileError(Exception ex) =>
            ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException;

        private static string NormalizeProfileName(string? profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
                return "NoProfile";

            var invalid = Path.GetInvalidFileNameChars();
            var normalized = new string(
                profileName
                    .Select(ch => invalid.Contains(ch) ? '_' : ch)
                    .ToArray());

            normalized = normalized.Replace(' ', '_');
            // Leave room for the timestamp and run ID on filesystems with a byte limit.
            return string.Concat(normalized.EnumerateRunes().Take(40));
        }

        private static void WriteLogEntry(LogEntry entry)
        {
            FileLog log;
            string failure;
            lock (FileLogLock)
            {
                if (_activeFileLog is not { } activeLog) return;
                log = activeLog;
                try
                {
                    // Open the existing file so deletion is reported instead of silently dropping its history.
                    using var file = new FileStream(log.Path, FileMode.Open, FileAccess.Write, FileShare.Read);
                    file.Seek(0, SeekOrigin.End);
                    using var writer = new StreamWriter(file);
                    writer.WriteLine(BuildLogLine(entry));
                    return;
                }
                catch (Exception ex) when (IsFileError(ex))
                {
                    _activeFileLog = null;
                    failure = $"Запись в файл журнала «{log.Path}» остановлена: {ex.Message}";
                }
            }

            log.ReportFailure?.Invoke(failure);
        }

        /// <summary>
        /// Внутренняя реализация логгера.
        /// </summary>
        private sealed class Logger : ILogger
        {
            private const int DisplayLimit = 1000;
            private readonly bool _writeToFile;
            private readonly Queue<LogEntry> _pendingEntries = new();
            private bool _publishScheduled;

            public Logger(string category, bool writeToFile = true)
            {
                Category = category ?? string.Empty;
                _writeToFile = writeToFile;
            }

            public string Category { get; }

            public ObservableCollection<LogEntry> Entries { get; } = new ObservableCollection<LogEntry>();

            public void Log(LogLevel level, string message) => Log(level, message, null);

            public void Log(LogLevel level, string message, TestErrorCode? code)
            {
                var entry = new LogEntry(DateTime.Now, level, Category, message, code);

                lock (_pendingEntries)
                {
                    if (_pendingEntries.Count == DisplayLimit) _pendingEntries.Dequeue();
                    _pendingEntries.Enqueue(entry);
                    if (!_publishScheduled)
                    {
                        _publishScheduled = true;
                        Dispatcher.UIThread.Post(PublishPendingEntries, DispatcherPriority.Normal);
                    }
                }

                if (_writeToFile) LoggingService.WriteLogEntry(entry);
            }

            private void PublishPendingEntries()
            {
                LogEntry[] pending;
                lock (_pendingEntries)
                {
                    pending = _pendingEntries.ToArray();
                    _pendingEntries.Clear();
                    _publishScheduled = false;
                }

                foreach (var entry in pending)
                {
                    Entries.Add(entry);
                    if (Entries.Count > DisplayLimit) Entries.RemoveAt(0);
                }
            }

            public void Trace(string message) => Log(LogLevel.Trace, message);
            public void Debug(string message) => Log(LogLevel.Debug, message);
            public void Info(string message) => Log(LogLevel.Info, message);
            public void Warning(string message) => Log(LogLevel.Warning, message);
            public void Error(string message) => Log(LogLevel.Error, message);

            public void Clear()
            {
                lock (_pendingEntries) _pendingEntries.Clear();
                Entries.Clear();
            }
        }
    }
}
