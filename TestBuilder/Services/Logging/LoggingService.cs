using Avalonia.Threading;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using TestBuilder.Services;

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
            if (message.Contains("[OK]"))
                HighlightColor = "#16A34A";
            else if (message.Contains("[ОШИБКА]"))
                HighlightColor = "#DC2626";
            else if (message.Contains("[ШАГ]"))
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
    /// Простая реализация сервера логирования для UI.
    /// </summary>
    public sealed class LoggingService : ILoggingService
    {
        /// <summary>
        /// Глобальный экземпляр сервиса. Можно использовать напрямую,
        /// либо подменить в тестах.
        /// </summary>
        public static LoggingService Instance { get; } = new LoggingService();

        private static readonly object ActiveLogFilePathLock = new();
        private static readonly object WriteFileLock = new();

        private static string? _activeLogFilePath;

        private LoggingService()
        {
        }

        public string? CurrentLogFilePath
        {
            get
            {
                lock (ActiveLogFilePathLock)
                {
                    return _activeLogFilePath;
                }
            }
        }

        /// <summary>
        /// Возвращает путь к файлу текущего прогона или null, если файл не инициализирован.
        /// </summary>
        public string? StartFileLogForRun(string? profileName)
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

            try
            {
                Directory.CreateDirectory(folder);
            }
            catch
            {
                return null;
            }

            var fileName = BuildRunLogFileName(profileName);
            var filePath = Path.Combine(folder, fileName);

            lock (ActiveLogFilePathLock)
            {
                _activeLogFilePath = filePath;
            }

            WriteLogLine(filePath, BuildHeaderLine(profileName));
            return filePath;
        }

        /// <summary>
        /// Закрывает текущий логовый файл прогона.
        /// </summary>
        public void StopFileLogForRun()
        {
            lock (ActiveLogFilePathLock)
            {
                _activeLogFilePath = null;
            }
        }

        public ILogger CreateLogger(string category)
            => new Logger(category);

        private static string BuildRunLogFileName(string? profileName)
        {
            var safeProfile = NormalizeProfileName(profileName);
            return $"test-run_{DateTime.Now:yyyyMMdd_HHmmss}_{safeProfile}.txt";
        }

        private static string BuildHeaderLine(string? profileName)
            => $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] [System] Запуск теста: {profileName ?? "Без имени профиля"}";

        private static string BuildLogLine(LogEntry entry)
            => $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level}] [{entry.Category}] {entry.Message}";

        private static void WriteLogLine(string path, string line)
        {
            try
            {
                lock (WriteFileLock)
                {
                    File.AppendAllText(path, line + Environment.NewLine);
                }
            }
            catch
            {
                // Игнорируем ошибки записи лога: не влияют на выполнение теста.
            }
        }

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
            return string.IsNullOrWhiteSpace(normalized)
                ? "NoProfile"
                : normalized;
        }

        private static void WriteLogEntry(LogEntry entry)
        {
            if (!AppSettings.Instance.EnableFileLogging)
                return;

            string? path;
            lock (ActiveLogFilePathLock)
            {
                path = _activeLogFilePath;
            }

            if (string.IsNullOrWhiteSpace(path))
                return;

            WriteLogLine(path, BuildLogLine(entry));
        }

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

                LoggingService.WriteLogEntry(entry);

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
