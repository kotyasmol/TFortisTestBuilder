using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM;

public partial class SetPswMacNodeViewModel : NodeViewModel
{
    [ObservableProperty] private string destinationIp = "192.168.0.1";
    [ObservableProperty] private string localIp = "192.168.0.2";
    [ObservableProperty] private int localPort = 6123;
    [ObservableProperty] private int udpPort = 43962;
    [ObservableProperty] private string macVariableName = "Dut.NewMac";
    [ObservableProperty] private int timeoutMs = 5000;
    [ObservableProperty] private bool failOnError = true;

    public ConnectorViewModel In { get; }
    public ConnectorViewModel TrueOut { get; }
    public ConnectorViewModel FalseOut { get; }

    public SetPswMacNodeViewModel()
    {
        Title = "Set PSW MAC (UDP)";
        In = new ConnectorViewModel { Title = "In", Parent = this };
        TrueOut = new ConnectorViewModel { Title = "True", Parent = this };
        FalseOut = new ConnectorViewModel { Title = "False", Parent = this };
        Input.Add(In);
        Output.Add(TrueOut);
        Output.Add(FalseOut);
    }

    public ITestStep CreateStep(ILogger logger) => new SetPswMacStep(logger, DestinationIp,
        LocalIp, LocalPort, UdpPort, MacVariableName, TimeoutMs, FailOnError);

    public override NodeViewModel Clone() => new SetPswMacNodeViewModel
    {
        DestinationIp = DestinationIp,
        LocalIp = LocalIp,
        LocalPort = LocalPort,
        UdpPort = UdpPort,
        MacVariableName = MacVariableName,
        TimeoutMs = TimeoutMs,
        FailOnError = FailOnError
    };
}
