using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public sealed class RunDataTestStep : ITestStep
    {
        private readonly ILogger _logger;
        private readonly string _mode;
        private readonly int _expectedPackets;
        private readonly int _packetSizeBytes;
        private readonly int _udpPort;
        private readonly int _maxPortTestTimeMs;
        private readonly IReadOnlyList<DataTestPortConfig> _ports;
        private readonly string _outputVariableName;
        private readonly bool _failOnError;

        public RunDataTestStep(
            ILogger logger,
            string mode,
            int expectedPackets,
            int packetSizeBytes,
            int udpPort,
            int maxPortTestTimeMs,
            IEnumerable<DataTestPortConfig> ports,
            string outputVariableName,
            bool failOnError)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mode = string.IsNullOrWhiteSpace(mode) ? "SoftwarePcap" : mode.Trim();
            _expectedPackets = Math.Max(1, expectedPackets);
            _packetSizeBytes = Math.Max(64, packetSizeBytes);
            _udpPort = udpPort <= 0 ? 43962 : udpPort;
            _maxPortTestTimeMs = Math.Max(1, maxPortTestTimeMs);
            _ports = ports.ToList();
            _outputVariableName = string.IsNullOrWhiteSpace(outputVariableName) ? "DataTest" : outputVariableName.Trim();
            _failOnError = failOnError;
        }

        public Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.Info($"[ШАГ] DataTest: mode {_mode}, ports {_ports.Count}, expected {_expectedPackets} packets.");

            var allPassed = false;
            var error = _mode.Equals("SoftwarePcap", StringComparison.OrdinalIgnoreCase)
                ? "SoftwarePcap требует pcap/Npcap интеграции; в текущей сборке она не подключена."
                : $"Режим DataTest '{_mode}' не поддерживается.";

            if (_ports.Count == 0)
            {
                error = "Не настроены пары портов DataTest.";
            }

            context.SetVariable($"{_outputVariableName}.Passed", allPassed);
            context.SetVariable($"{_outputVariableName}.Mode", _mode);
            context.SetVariable($"{_outputVariableName}.ExpectedPackets", _expectedPackets);
            context.SetVariable($"{_outputVariableName}.PacketSizeBytes", _packetSizeBytes);
            context.SetVariable($"{_outputVariableName}.UdpPort", _udpPort);
            context.SetVariable($"{_outputVariableName}.MaxPortTestTimeMs", _maxPortTestTimeMs);
            context.SetVariable($"{_outputVariableName}.Error", error);

            for (var i = 0; i < _ports.Count; i++)
            {
                var port = _ports[i];
                var prefix = $"{_outputVariableName}.Port{i}";
                context.SetVariable($"{prefix}.Name", port.Name);
                context.SetVariable($"{prefix}.InIp", port.InIp);
                context.SetVariable($"{prefix}.OutIp", port.OutIp);
                context.SetVariable($"{prefix}.Passed", false);
                context.SetVariable($"{prefix}.TransmittedPackets", 0);
                context.SetVariable($"{prefix}.ReceivedPackets", 0);
                context.SetVariable($"{prefix}.SpeedKbps", 0.0);
                context.SetVariable($"{prefix}.Error", error);
            }

            _logger.Warning($"[ОШИБКА] DataTest не выполнен: {error}");
            return Task.FromResult(_failOnError ? StepResult.False : StepResult.True);
        }
    }

    public sealed record DataTestPortConfig(string Name, string InIp, string OutIp);
}
