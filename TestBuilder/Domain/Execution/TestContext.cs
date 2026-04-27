using System.Collections.Generic;
using System.Threading;
using TestBuilder.Domain.Monitoring;

namespace TestBuilder.Domain.Execution
{
    public sealed class TestContext
    {
        public RegisterMonitor RegisterMonitor { get; set; }

        public RegisterState RegisterState { get; }

        public CancellationToken CancellationToken { get; set; }

        public bool IsConnected { get; set; }

        public string ProfileName { get; set; } = string.Empty;

        public bool HasCriticalError { get; set; }

        /// <summary>
        /// Текущий Modbus slaveId, заданный управляющей нодой цикла.
        /// Шаги внутри цикла могут брать slaveId отсюда вместо фиксированного значения.
        /// </summary>
        public byte? CurrentSlaveId { get; set; }

        /// <summary>
        /// Универсальное хранилище переменных сценария.
        /// Сейчас используется для slaveId, позже можно использовать для обычных for/while-блоков.
        /// </summary>
        public Dictionary<string, object> Variables { get; } = new();

        public TestContext(RegisterState registerState)
        {
            RegisterState = registerState;
        }

        public void SetVariable(string name, object value)
        {
            Variables[name] = value;
        }

        public bool TryGetVariable<T>(string name, out T? value)
        {
            if (Variables.TryGetValue(name, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }

            value = default;
            return false;
        }
    }
}
