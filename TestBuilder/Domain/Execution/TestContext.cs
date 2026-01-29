using System;
using System.Collections.Generic;
using System.Threading;
using TestBuilder.Domain.Monitoring;

namespace TestBuilder.Domain.Execution
{
    /// <summary>
    /// Контекст теста. Хранит состояние регистров, мониторинг и токен отмены.
    /// </summary>
    public sealed class TestContext
    {
        /// <summary>
        /// Мониторинг регистров (параллельный процесс).
        /// </summary>
        public RegisterMonitor RegisterMonitor { get; set; }

        /// <summary>
        /// Потокобезопасное хранилище актуальных значений регистров.
        /// Обновляется RegisterMonitor и используется шагами теста.
        /// </summary>
        public RegisterState RegisterState { get; } = new();

        /// <summary>
        /// Токен отмены для управления всем тестом.
        /// </summary>
        public CancellationToken CancellationToken { get; set; }

        /// <summary>
        /// Для совместимости со старым кодом: возвращает snapshot всех регистров.
        /// </summary>
        public Dictionary<string, int> Registers => RegisterState.GetSnapshot();

        /// <summary>
        /// Дополнительные поля для состояния теста:
        /// - подключение к COM/серверу
        /// - профиль тестирования
        /// - флаги остановки и ошибок
        /// </summary>
        public bool IsConnected { get; set; }
        public string ProfileName { get; set; }
        public bool HasCriticalError { get; set; }
    }
}
