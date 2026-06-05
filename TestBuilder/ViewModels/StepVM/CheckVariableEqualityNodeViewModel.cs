using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class CheckVariableEqualityNodeViewModel : NodeViewModel
    {
        [ObservableProperty]
        private string variableName = "Dut.init_ok";

        [ObservableProperty]
        private string expectedValue = "1";

        [ObservableProperty]
        private VariableComparisonType comparisonType = VariableComparisonType.Number;

        [ObservableProperty]
        private string failMessage = string.Empty;

        public ConnectorViewModel In { get; }

        public ConnectorViewModel TrueOut { get; }

        public ConnectorViewModel FalseOut { get; }

        public CheckVariableEqualityNodeViewModel()
        {
            Title = "Check Variable Equality";

            In = new ConnectorViewModel
            {
                Title = "In",
                Parent = this
            };

            TrueOut = new ConnectorViewModel
            {
                Title = "True",
                Parent = this
            };

            FalseOut = new ConnectorViewModel
            {
                Title = "False",
                Parent = this
            };

            Input.Add(In);
            Output.Add(TrueOut);
            Output.Add(FalseOut);
        }

        public ITestStep CreateStep(ILogger logger)
        {
            return new CheckVariableEqualityStep(
                logger,
                VariableName,
                ExpectedValue,
                ComparisonType,
                FailMessage);
        }

        public override NodeViewModel Clone() => new CheckVariableEqualityNodeViewModel
        {
            VariableName = VariableName,
            ExpectedValue = ExpectedValue,
            ComparisonType = ComparisonType,
            FailMessage = FailMessage
        };
    }
}
