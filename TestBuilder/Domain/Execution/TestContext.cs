using System.Collections.Generic;
using System.Threading;
using TestBuilder.Domain.Monitoring;

namespace TestBuilder.Domain.Execution
{
    public sealed class TestContext
    {
        public RegisterMonitor? RegisterMonitor { get; set; }

        public RegisterState RegisterState { get; }

        public CancellationToken CancellationToken { get; set; }

        public bool IsConnected { get; set; }

        public string? ProfileName { get; set; }

        public bool HasCriticalError { get; set; }

        public byte? CurrentSlaveId { get; set; }

        public IExecutionObserver? ExecutionObserver { get; set; }

        public Dictionary<string, object> Variables { get; } = new();

        public TestContext(RegisterState registerState)
        {
            RegisterState = registerState;
        }

        public T? GetVariable<T>(string name)
        {
            if (Variables.TryGetValue(name, out var value) && value is T typed)
                return typed;

            return default;
        }

        public void SetVariable(string name, object value)
        {
            Variables[name] = value;
        }
    }
}