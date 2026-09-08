using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class BuildTestReportNodeViewModel : NodeViewModel
    {
        [ObservableProperty] private string reportVariableName = "TestReportText";
        [ObservableProperty] private string serialVariableName = "SerialNumber";
        [ObservableProperty] private string testType = "production";
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
                AppSettings.Instance.StandId,
                SerialVariableName,
                App.StartupSessionId,
                TestType,
                IncludeAllVariables);

        public override NodeViewModel Clone() => new BuildTestReportNodeViewModel
        {
            ReportVariableName = ReportVariableName,
            SerialVariableName = SerialVariableName,
            TestType = TestType,
            IncludeAllVariables = IncludeAllVariables
        };
    }
}
