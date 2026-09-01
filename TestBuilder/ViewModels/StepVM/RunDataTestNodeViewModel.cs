using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class RunDataTestNodeViewModel : NodeViewModel
    {
        public const string DefaultPortsText =
            "port0-1,192.168.0.2,192.168.0.3,100\r\n" +
            "port2-3,192.168.0.4,192.168.0.5,100\r\n" +
            "port4-5,192.168.0.6,192.168.0.7,100\r\n" +
            "port6-7,192.168.0.8,192.168.0.9,100\r\n" +
            "port8-9,192.168.0.10,192.168.0.11,100";

        [ObservableProperty] private string mode = "SoftwarePcap";
        [ObservableProperty] private int expectedPackets = 10000;
        [ObservableProperty] private int packetSizeBytes = 1514;
        [ObservableProperty] private int udpPort = 43962;
        [ObservableProperty] private int maxPortTestTimeMs = 15000;
        [ObservableProperty] private int targetBandwidthMbps = 100;
        [ObservableProperty] private int durationMs = 5000;
        [ObservableProperty] private int warmupMs = 500;
        [ObservableProperty] private int interPairDelayMs = 5000;
        [ObservableProperty] private double allowedLossPercent = 1.0;
        [ObservableProperty] private double allowedTxDeficitPercent = 2.0;
        [ObservableProperty] private bool bidirectional = true;
        [ObservableProperty] private string portsText = DefaultPortsText;
        [ObservableProperty] private string outputVariableName = "DataTest";
        [ObservableProperty] private bool failOnError = true;

        public ConnectorViewModel In { get; }
        public ConnectorViewModel TrueOut { get; }
        public ConnectorViewModel FalseOut { get; }

        public RunDataTestNodeViewModel()
        {
            Title = "Run Data Test";
            In = new ConnectorViewModel { Title = "In", Parent = this };
            TrueOut = new ConnectorViewModel { Title = "True", Parent = this };
            FalseOut = new ConnectorViewModel { Title = "False", Parent = this };
            Input.Add(In);
            Output.Add(TrueOut);
            Output.Add(FalseOut);
        }

        partial void OnTargetBandwidthMbpsChanged(int value)
        {
            var normalized = RunDataTestStep.NormalizeBandwidth(value);
            if (normalized != value)
            {
                TargetBandwidthMbps = normalized;
            }
        }

        public ITestStep CreateStep(ILogger logger) =>
            new RunDataTestStep(
                logger,
                Mode,
                ExpectedPackets,
                PacketSizeBytes,
                UdpPort,
                MaxPortTestTimeMs,
                TargetBandwidthMbps,
                DurationMs,
                WarmupMs,
                InterPairDelayMs,
                AllowedLossPercent,
                AllowedTxDeficitPercent,
                Bidirectional,
                ParsePorts(),
                OutputVariableName,
                FailOnError);

        public override NodeViewModel Clone() => new RunDataTestNodeViewModel
        {
            Mode = Mode,
            ExpectedPackets = ExpectedPackets,
            PacketSizeBytes = PacketSizeBytes,
            UdpPort = UdpPort,
            MaxPortTestTimeMs = MaxPortTestTimeMs,
            TargetBandwidthMbps = TargetBandwidthMbps,
            DurationMs = DurationMs,
            WarmupMs = WarmupMs,
            InterPairDelayMs = InterPairDelayMs,
            AllowedLossPercent = AllowedLossPercent,
            AllowedTxDeficitPercent = AllowedTxDeficitPercent,
            Bidirectional = Bidirectional,
            PortsText = PortsText,
            OutputVariableName = OutputVariableName,
            FailOnError = FailOnError
        };

        private IEnumerable<DataTestPortConfig> ParsePorts()
        {
            return PortsText
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(','))
                .Where(parts => parts.Length >= 3)
                .Select(parts => new DataTestPortConfig(
                    parts[0].Trim(),
                    parts[1].Trim(),
                    parts[2].Trim(),
                    parts.Length >= 4 && int.TryParse(parts[3].Trim(), out var mbps)
                        ? RunDataTestStep.NormalizeBandwidth(mbps)
                        : null));
        }
    }
}
