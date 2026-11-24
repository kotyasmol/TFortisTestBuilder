using TestBuilder.Services;

namespace TestBuilder.Tests.Support;

internal static class NetworkTestPlans
{
    // Explicit fixture for compatibility with saved ten-port workstations.
    public static BenchNetworkPlan TenPorts() => new()
    {
        Addresses = Enumerable.Range(2, 10).Select(i => new BenchNetworkAddress
            { Ip = $"192.168.0.{i}", TemporaryIp = $"192.0.2.{i}" }).ToList()
    };

    public static IDisposable UseTenPorts() => new Scope();
    private sealed class Scope : IDisposable
    {
        private readonly BenchNetworkPlan previous = AppSettings.Instance.NetworkPlan;
        public Scope() => AppSettings.Instance.NetworkPlan = TenPorts();
        public void Dispose() => AppSettings.Instance.NetworkPlan = previous;
    }
}
