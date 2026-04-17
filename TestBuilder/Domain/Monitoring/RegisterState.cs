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
        private readonly ConcurrentDictionary<RegisterKey, int> _values = new();

        /// <summary>
        /// Обновляет значение регистра. Используется мониторингом.
        /// </summary>
        public void Update(byte slaveId, int address, int value)
        {
            _values[new RegisterKey(slaveId, address)] = value;
        }

        /// <summary>
        /// Попытка получить последнее известное значение регистра.
        /// </summary>
        public bool TryGet(byte slaveId, int address, out int value)
        {
            return _values.TryGetValue(new RegisterKey(slaveId, address), out value);
        }

    }
}
