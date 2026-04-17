using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class DelayNodeViewModel : NodeViewModel
    {
        [ObservableProperty]
        private int milliseconds = 1000;

        public ConnectorViewModel In { get; }
        public ConnectorViewModel Out { get; }

        public DelayNodeViewModel()
        {
            Title = "Delay";

            In = new ConnectorViewModel { Title = "In" };
            Out = new ConnectorViewModel { Title = "Out" };

            AddInput(In);
            AddOutput(Out);
        }

        public ITestStep CreateStep(ILogger logger) => new DelayStep(Milliseconds, logger);
    }
}