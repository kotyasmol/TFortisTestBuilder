using Avalonia.Threading;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

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

            // Определяем цвет по содержимому сообщения — только для цветных меток
            if (Message.Contains("[OK]"))
                HighlightColor = "#16A34A";
            else if (Message.Contains("[ОШИБКА]"))
                HighlightColor = "#DC2626";
            else if (Message.Contains("[ШАГ]"))
                HighlightColor = "#2563EB";
            else
                HighlightColor = null; // null = использовать DynamicResource из XAML
        }

        public DateTime Timestamp { get; }
        public LogLevel Level { get; }
        public string Category { get; }
        public string Message { get; }

        /// <summary>null = обычный текст (тема-зависимый), иначе фиксированный цвет</summary>
        public string? HighlightColor { get; }

        public bool IsHighlighted => HighlightColor != null;

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
        private static readonly object FileLock = new();
        private static readonly string LogDirectory = GetLogDirectory();

        public static string CurrentLogFilePath => GetLogFilePath(DateTime.Now);

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

                WriteToFile(entry);

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

        private static string GetLogDirectory()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            return string.IsNullOrWhiteSpace(localAppData)
                ? Path.Combine(AppContext.BaseDirectory, "logs")
                : Path.Combine(localAppData, "TFortisTestBuilder", "logs");
        }

        private static string GetLogFilePath(DateTime timestamp) =>
            Path.Combine(LogDirectory, $"testbuilder-{timestamp:yyyyMMdd}.log");

        private static void WriteToFile(LogEntry entry)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);

                var line =
                    $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] " +
                    $"[{entry.Level}] [{entry.Category}] {entry.Message}{Environment.NewLine}";

                lock (FileLock)
                {
                    File.AppendAllText(GetLogFilePath(entry.Timestamp), line, Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never break the test runner.
            }
        }
    }
}
