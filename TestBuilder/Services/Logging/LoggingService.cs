using Avalonia.Threading;
using System;
using System.Collections.ObjectModel;

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

    /// <summary>
    /// Одна запись лога.
    /// </summary>
    public sealed class LogEntry
    {
        public LogEntry(DateTime timestamp, LogLevel level, string category, string message)
        {
            Timestamp = timestamp;
            Level = level;
            Category = category ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public DateTime Timestamp { get; }
        public LogLevel Level { get; }
        public string Category { get; }
        public string Message { get; }

        public override string ToString()
            => $"[{Timestamp:HH:mm:ss}] {Message}";
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

        void Trace(string message);
        void Debug(string message);
        void Info(string message);
        void Warning(string message);
        void Error(string message);

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
    /// Простая реализация сервиса логирования для UI.
    /// </summary>
    public sealed class LoggingService : ILoggingService
    {
        /// <summary>
        /// Глобальный экземпляр сервиса. Можно использовать напрямую,
        /// либо подменить в тестах.
        /// </summary>
        public static LoggingService Instance { get; } = new LoggingService();

        private LoggingService()
        {
        }

        public ILogger CreateLogger(string category)
            => new Logger(category);

        /// <summary>
        /// Внутренняя реализация логгера.
        /// </summary>
        private sealed class Logger : ILogger
        {
            public Logger(string category)
            {
                Category = category ?? string.Empty;
            }

            public string Category { get; }

            public ObservableCollection<LogEntry> Entries { get; } = new ObservableCollection<LogEntry>();

            public void Log(LogLevel level, string message)
            {
                var entry = new LogEntry(DateTime.Now, level, Category, message);

                Dispatcher.UIThread.Post(() =>
                {
                    // Добавляем новую запись
                    Entries.Add(entry);

                    // Ограничиваем размер
                    if (Entries.Count > 1000)
                    {
                        Entries.RemoveAt(0);
                    }
                });
            }


            public void Trace(string message) => Log(LogLevel.Trace, message);
            public void Debug(string message) => Log(LogLevel.Debug, message);
            public void Info(string message) => Log(LogLevel.Info, message);
            public void Warning(string message) => Log(LogLevel.Warning, message);
            public void Error(string message) => Log(LogLevel.Error, message);

            public void Clear() => Entries.Clear();
        }
    }
}