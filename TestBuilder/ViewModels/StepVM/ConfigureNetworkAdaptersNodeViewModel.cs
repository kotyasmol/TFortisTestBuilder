using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM;

public partial class ConfigureNetworkAdaptersNodeViewModel : NodeViewModel
{
    [ObservableProperty] private string adaptersText = "Auto=Switch;192.168.10.1;24\r\nAuto=Switch;192.168.10.2;24";
    [ObservableProperty] private string outputVariableName = "NetworkConfig";
    [ObservableProperty] private bool failOnError = true;

    public ConnectorViewModel In { get; }
    public ConnectorViewModel TrueOut { get; }
    public ConnectorViewModel FalseOut { get; }

    public ConfigureNetworkAdaptersNodeViewModel()
    {
        Title = "Configure Network Adapters";
        In = new ConnectorViewModel { Title = "In", Parent = this };
        TrueOut = new ConnectorViewModel { Title = "True", Parent = this };
        FalseOut = new ConnectorViewModel { Title = "False", Parent = this };
        Input.Add(In);
        Output.Add(TrueOut);
        Output.Add(FalseOut);
    }

    public ITestStep CreateStep(ILogger logger) => new ConfigureNetworkAdaptersStep(
        logger,
        ConfigureNetworkAdaptersStep.ParseConfigurations(AdaptersText),
        OutputVariableName,
        FailOnError);

    public override NodeViewModel Clone() => new ConfigureNetworkAdaptersNodeViewModel
    {
        AdaptersText = AdaptersText,
        OutputVariableName = OutputVariableName,
        FailOnError = FailOnError
    };
}
