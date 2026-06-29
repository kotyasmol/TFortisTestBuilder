using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class BuildTestReportNodeViewModel : NodeViewModel
    {
        [ObservableProperty] private string reportVariableName = "TestReportJson";
        [ObservableProperty] private string deviceName = "PSW+UPS-Box 8x2Pro";
        [ObservableProperty] private int deviceType = 32;
        [ObservableProperty] private string serialVariableName = "SerialShort";
        [ObservableProperty] private string macVariableName = "Dut.NewMac";
        [ObservableProperty] private bool includeAllVariables = true;

        public ConnectorViewModel In { get; }
        public ConnectorViewModel TrueOut { get; }
        public ConnectorViewModel FalseOut { get; }

        public BuildTestReportNodeViewModel()
        {
            Title = "Build Test Report";

            In = new ConnectorViewModel { Title = "In", Parent = this };
            TrueOut = new ConnectorViewModel { Title = "True", Parent = this };
            FalseOut = new ConnectorViewModel { Title = "False", Parent = this };

            Input.Add(In);
            Output.Add(TrueOut);
            Output.Add(FalseOut);
        }

        public ITestStep CreateStep(ILogger logger) =>
            new BuildTestReportStep(
                logger,
                ReportVariableName,
                DeviceName,
                DeviceType,
                SerialVariableName,
                MacVariableName,
                IncludeAllVariables);

        public override NodeViewModel Clone() => new BuildTestReportNodeViewModel
        {
            ReportVariableName = ReportVariableName,
            DeviceName = DeviceName,
            DeviceType = DeviceType,
            SerialVariableName = SerialVariableName,
            MacVariableName = MacVariableName,
            IncludeAllVariables = IncludeAllVariables
        };
    }
}
