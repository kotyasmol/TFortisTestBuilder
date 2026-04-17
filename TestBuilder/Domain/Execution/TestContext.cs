using System.Threading;
using TestBuilder.Domain.Monitoring;

namespace TestBuilder.Domain.Execution
{
    public sealed class TestContext
    {
        public RegisterMonitor RegisterMonitor { get; set; }

        // ❗ теперь без new()
        public RegisterState RegisterState { get; }

        public CancellationToken CancellationToken { get; set; }

        public bool IsConnected { get; set; }
        public string ProfileName { get; set; }
        public bool HasCriticalError { get; set; }

        // ✅ добавили конструктор
        public TestContext(RegisterState registerState)
        {
            RegisterState = registerState;
        }
    }
}