using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class LabelNodeViewModel : NodeViewModel
    {
        private const double MinLabelWidth = 190;
        private const double MinLabelHeight = 88;

        [ObservableProperty]
        private string text = "Этап";

        [ObservableProperty]
        private double labelWidth = 300;

        [ObservableProperty]
        private double labelHeight = 120;

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

        partial void OnLabelWidthChanged(double value)
        {
            if (value < MinLabelWidth)
                LabelWidth = MinLabelWidth;
        }

        partial void OnLabelHeightChanged(double value)
        {
            if (value < MinLabelHeight)
                LabelHeight = MinLabelHeight;
        }

        public override NodeViewModel Clone() => new LabelNodeViewModel
        {
            Text = Text,
            LabelWidth = LabelWidth,
            LabelHeight = LabelHeight
        };
    }
}
