using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class CompareVariablesNodeViewModel : NodeViewModel
    {
        [ObservableProperty] private string leftVariableName = "Dut.default_mac";
        [ObservableProperty] private string rightVariableName = "Dut.NewMac";
        [ObservableProperty] private VariableComparisonType comparisonType = VariableComparisonType.MacAddress;
        [ObservableProperty] private string failMessage = string.Empty;

        public ConnectorViewModel In { get; }
        public ConnectorViewModel TrueOut { get; }
        public ConnectorViewModel FalseOut { get; }

        public CompareVariablesNodeViewModel()
        {
            Title = "Compare Variables";

            In = new ConnectorViewModel { Title = "In", Parent = this };
            TrueOut = new ConnectorViewModel { Title = "True", Parent = this };
            FalseOut = new ConnectorViewModel { Title = "False", Parent = this };

            Input.Add(In);
            Output.Add(TrueOut);
            Output.Add(FalseOut);
        }

        public ITestStep CreateStep(ILogger logger) =>
            new CompareVariablesStep(logger, LeftVariableName, RightVariableName, ComparisonType, FailMessage);

        public override NodeViewModel Clone() => new CompareVariablesNodeViewModel
        {
            LeftVariableName = LeftVariableName,
            RightVariableName = RightVariableName,
            ComparisonType = ComparisonType,
            FailMessage = FailMessage
        };
    }
}
