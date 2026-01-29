using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestBuilder.Domain.Monitoring
{
    /// <summary>
    /// Потокобезопасное хранилище актуальных значений регистров.
    /// Обновляется мониторингом и используется шагами теста.
    /// </summary>
    public class RegisterState
    {
        private readonly ConcurrentDictionary<string, int> _values = new();

        /// <summary>
        /// Обновляет значение регистра. Используется мониторингом.
        /// </summary>
        public void Update(string registerName, int value)
        {
            _values[registerName] = value;
        }

        /// <summary>
        /// Попытка получить последнее известное значение регистра.
        /// </summary>
        public bool TryGet(string registerName, out int value)
        {
            return _values.TryGetValue(registerName, out value);
        }

        /// <summary>
        /// Получить значение регистра по имени. Возвращает 0, если регистра нет.
        /// </summary>
        public int Get(string registerName) => _values.TryGetValue(registerName, out var val) ? val : 0;

        /// <summary>
        /// Получить снимок текущих значений всех регистров.
        /// </summary>
        public Dictionary<string, int> GetSnapshot() => new Dictionary<string, int>(_values);
    }
}
