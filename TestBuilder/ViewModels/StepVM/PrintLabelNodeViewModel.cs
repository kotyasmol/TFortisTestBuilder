using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class PrintLabelNodeViewModel : NodeViewModel
    {
        [ObservableProperty] private string printerName = "TSC TE310";
        [ObservableProperty] private string serialVariableName = "SerialNumber";
        [ObservableProperty] private int copies = 4;
        [ObservableProperty] private bool failOnPrinterError = true;

        public ConnectorViewModel In { get; }
        public ConnectorViewModel TrueOut { get; }
        public ConnectorViewModel FalseOut { get; }

        public PrintLabelNodeViewModel()
        {
            Title = "Print Label";
            In = new ConnectorViewModel { Title = "In", Parent = this };
            TrueOut = new ConnectorViewModel { Title = "True", Parent = this };
            FalseOut = new ConnectorViewModel { Title = "False", Parent = this };
            Input.Add(In);
            Output.Add(TrueOut);
            Output.Add(FalseOut);
        }

        public ITestStep CreateStep(ILogger logger) =>
            new PrintLabelStep(logger, PrinterName, SerialVariableName, Copies, FailOnPrinterError);

        public override NodeViewModel Clone() => new PrintLabelNodeViewModel
        {
            PrinterName = PrinterName,
            SerialVariableName = SerialVariableName,
            Copies = Copies,
            FailOnPrinterError = FailOnPrinterError
        };
    }
}
