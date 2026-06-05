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
        [ObservableProperty] private string mode = "SoftwarePcap";
        [ObservableProperty] private int expectedPackets = 10000;
        [ObservableProperty] private int packetSizeBytes = 1514;
        [ObservableProperty] private int udpPort = 43962;
        [ObservableProperty] private int maxPortTestTimeMs = 15000;
        [ObservableProperty] private string portsText = "Port 0,192.168.10.1,192.168.10.2";
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

        public ITestStep CreateStep(ILogger logger) =>
            new RunDataTestStep(logger, Mode, ExpectedPackets, PacketSizeBytes, UdpPort, MaxPortTestTimeMs, ParsePorts(), OutputVariableName, FailOnError);

        public override NodeViewModel Clone() => new RunDataTestNodeViewModel
        {
            Mode = Mode,
            ExpectedPackets = ExpectedPackets,
            PacketSizeBytes = PacketSizeBytes,
            UdpPort = UdpPort,
            MaxPortTestTimeMs = MaxPortTestTimeMs,
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
                .Select(parts => new DataTestPortConfig(parts[0].Trim(), parts[1].Trim(), parts[2].Trim()));
        }
    }
}
