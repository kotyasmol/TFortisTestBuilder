using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class OperatorActionNodeViewModel : NodeViewModel
    {
        [ObservableProperty] private string message = string.Empty;

        public ConnectorViewModel In { get; }
        public ConnectorViewModel TrueOut { get; }
        public ConnectorViewModel FalseOut { get; }

        public OperatorActionNodeViewModel()
        {
            Title = "Действие оператора";
            In = new ConnectorViewModel { Title = "Вход" };
            TrueOut = new ConnectorViewModel { Title = "Продолжить" };
            FalseOut = new ConnectorViewModel { Title = "Отмена" };
            AddInput(In);
            AddOutput(TrueOut);
            AddOutput(FalseOut);
        }

        public ITestStep CreateStep(ILogger logger) => new OperatorActionStep(Message, logger);

        public override NodeViewModel Clone() => new OperatorActionNodeViewModel { Message = Message };
    }
}
