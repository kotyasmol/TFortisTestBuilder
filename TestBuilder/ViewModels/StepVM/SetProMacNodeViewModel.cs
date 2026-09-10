using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class SetProMacNodeViewModel : NodeViewModel
    {
        [ObservableProperty] private string batchPath = "set_mac_pro.bat";
        [ObservableProperty] private string macVariableName = "Dut.NewMac";
        [ObservableProperty] private string boardVersion = "PSW+UPS-Box 8x2Pro";
        [ObservableProperty] private int timeoutMs = 60000;
        [ObservableProperty] private bool failOnError = true;

        public ConnectorViewModel In { get; }
        public ConnectorViewModel TrueOut { get; }
        public ConnectorViewModel FalseOut { get; }

        public SetProMacNodeViewModel()
        {
            Title = "Set Pro MAC";
            In = new ConnectorViewModel { Title = "In", Parent = this };
            TrueOut = new ConnectorViewModel { Title = "True", Parent = this };
            FalseOut = new ConnectorViewModel { Title = "False", Parent = this };
            Input.Add(In);
            Output.Add(TrueOut);
            Output.Add(FalseOut);
        }

        public ITestStep CreateStep(ILogger logger) =>
            new SetProMacStep(logger, BatchPath, MacVariableName, BoardVersion, TimeoutMs, FailOnError);

        public override NodeViewModel Clone() => new SetProMacNodeViewModel
        {
            BatchPath = BatchPath,
            MacVariableName = MacVariableName,
            BoardVersion = BoardVersion,
            TimeoutMs = TimeoutMs,
            FailOnError = FailOnError
        };
    }
}
