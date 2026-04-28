using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class LabelNodeViewModel : NodeViewModel
    {
        [ObservableProperty]
        private string text = "Этап";

        public ConnectorViewModel In { get; }
        public ConnectorViewModel Out { get; }

        public LabelNodeViewModel()
        {
            Title = "Метка";
            In = new ConnectorViewModel { Title = "Вход" };
            Out = new ConnectorViewModel { Title = "Выход" };
            AddInput(In);
            AddOutput(Out);
        }

        public ITestStep CreateStep(ILogger logger) => new LabelStep(Text, logger);
    }
}