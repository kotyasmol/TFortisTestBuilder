using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class SendUdpSetMacPacketNodeViewModel : NodeViewModel
    {
        [ObservableProperty] private string targetIp = "192.168.0.1";
        [ObservableProperty] private int targetPort = 43962;
        [ObservableProperty] private string macVariableName = "Dut.NewMac";
        [ObservableProperty] private int timeoutMs = 1000;
        [ObservableProperty] private int repeatCount = 1;
        [ObservableProperty] private int delayBetweenRepeatsMs = 200;
        [ObservableProperty] private bool failOnSendError = true;

        public ConnectorViewModel In { get; }
        public ConnectorViewModel TrueOut { get; }
        public ConnectorViewModel FalseOut { get; }

        public SendUdpSetMacPacketNodeViewModel()
        {
            Title = "Send UDP Set MAC";
            In = new ConnectorViewModel { Title = "In", Parent = this };
            TrueOut = new ConnectorViewModel { Title = "True", Parent = this };
            FalseOut = new ConnectorViewModel { Title = "False", Parent = this };
            Input.Add(In);
            Output.Add(TrueOut);
            Output.Add(FalseOut);
        }

        public ITestStep CreateStep(ILogger logger) =>
            new SendUdpSetMacPacketStep(logger, TargetIp, TargetPort, MacVariableName, TimeoutMs, RepeatCount, DelayBetweenRepeatsMs, FailOnSendError);

        public override NodeViewModel Clone() => new SendUdpSetMacPacketNodeViewModel
        {
            TargetIp = TargetIp,
            TargetPort = TargetPort,
            MacVariableName = MacVariableName,
            TimeoutMs = TimeoutMs,
            RepeatCount = RepeatCount,
            DelayBetweenRepeatsMs = DelayBetweenRepeatsMs,
            FailOnSendError = FailOnSendError
        };
    }
}
