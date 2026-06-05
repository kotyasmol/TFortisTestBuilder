using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class ParseTestPageNodeViewModel : NodeViewModel
    {
        [ObservableProperty]
        private string inputVariableName = ParseTestPageStep.DefaultInputVariableName;

        [ObservableProperty]
        private string outputPrefix = ParseTestPageStep.DefaultOutputPrefix;

        [ObservableProperty]
        private bool failOnInvalidXml = true;

        [ObservableProperty]
        private bool applyPsw2gAdc25Fix = true;

        [ObservableProperty]
        private string fieldNames = ParseTestPageStep.DefaultFieldNames;

        [ObservableProperty]
        private string requiredFieldNames = ParseTestPageStep.DefaultRequiredFieldNames;

        public ConnectorViewModel In { get; }

        public ConnectorViewModel TrueOut { get; }

        public ConnectorViewModel FalseOut { get; }

        public ParseTestPageNodeViewModel()
        {
            Title = "Parse Test Page";

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
            return new ParseTestPageStep(
                logger,
                InputVariableName,
                OutputPrefix,
                FailOnInvalidXml,
                ApplyPsw2gAdc25Fix,
                FieldNames,
                RequiredFieldNames);
        }

        public override NodeViewModel Clone() => new ParseTestPageNodeViewModel
        {
            InputVariableName = InputVariableName,
            OutputPrefix = OutputPrefix,
            FailOnInvalidXml = FailOnInvalidXml,
            ApplyPsw2gAdc25Fix = ApplyPsw2gAdc25Fix,
            FieldNames = FieldNames,
            RequiredFieldNames = RequiredFieldNames
        };
    }
}
