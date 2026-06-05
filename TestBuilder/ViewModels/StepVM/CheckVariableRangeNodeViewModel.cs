using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class CheckVariableRangeNodeViewModel : NodeViewModel
    {
        [ObservableProperty]
        private string variableName = "Dut.akb_voltage";

        [ObservableProperty]
        private double min = 12.0;

        [ObservableProperty]
        private double max = 27.0;

        [ObservableProperty]
        private bool inclusive = true;

        [ObservableProperty]
        private string failMessage = string.Empty;

        public ConnectorViewModel In { get; }

        public ConnectorViewModel TrueOut { get; }

        public ConnectorViewModel FalseOut { get; }

        public CheckVariableRangeNodeViewModel()
        {
            Title = "Check Variable Range";

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
            return new CheckVariableRangeStep(
                logger,
                VariableName,
                Min,
                Max,
                Inclusive,
                FailMessage);
        }

        public override NodeViewModel Clone() => new CheckVariableRangeNodeViewModel
        {
            VariableName = VariableName,
            Min = Min,
            Max = Max,
            Inclusive = Inclusive,
            FailMessage = FailMessage
        };
    }
}
