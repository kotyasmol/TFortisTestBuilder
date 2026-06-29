using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class BuildMacFromSerialNodeViewModel : NodeViewModel
    {
        [ObservableProperty] private string serialVariableName = "SerialNumber";
        [ObservableProperty] private int serialOffset = 3200000;
        [ObservableProperty] private string macPrefix = "C0:11:A6:20";
        [ObservableProperty] private string serialShortVariableName = "SerialShort";
        [ObservableProperty] private string macVariableName = "Dut.NewMac";
        [ObservableProperty] private bool failOnError = true;

        public ConnectorViewModel In { get; }
        public ConnectorViewModel TrueOut { get; }
        public ConnectorViewModel FalseOut { get; }

        public BuildMacFromSerialNodeViewModel()
        {
            Title = "Build MAC From Serial";

            In = new ConnectorViewModel { Title = "In", Parent = this };
            TrueOut = new ConnectorViewModel { Title = "True", Parent = this };
            FalseOut = new ConnectorViewModel { Title = "False", Parent = this };

            Input.Add(In);
            Output.Add(TrueOut);
            Output.Add(FalseOut);
        }

        public ITestStep CreateStep(ILogger logger) =>
            new BuildMacFromSerialStep(
                logger,
                SerialVariableName,
                SerialOffset,
                MacPrefix,
                SerialShortVariableName,
                MacVariableName,
                FailOnError);

        public override NodeViewModel Clone() => new BuildMacFromSerialNodeViewModel
        {
            SerialVariableName = SerialVariableName,
            SerialOffset = SerialOffset,
            MacPrefix = MacPrefix,
            SerialShortVariableName = SerialShortVariableName,
            MacVariableName = MacVariableName,
            FailOnError = FailOnError
        };
    }
}
