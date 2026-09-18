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
        [ObservableProperty] private string serialShortVariableName = "SerialShort";
        [ObservableProperty] private string macVariableName = "Dut.default_mac";
        [ObservableProperty] private bool useManualSerialNumber;
        [ObservableProperty] private string manualSerialNumber = string.Empty;
        [ObservableProperty] private string manualMacAddress = string.Empty;
        [ObservableProperty] private int copies = 4;
        [ObservableProperty] private bool useQtProZplFormat;
        [ObservableProperty] private DeviceLabelModel labelModel = DeviceLabelModel.PswUpsBox8x2Pro;
        public DeviceLabelModel[] LabelModels { get; } = System.Enum.GetValues<DeviceLabelModel>();
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
            new PrintLabelStep(
                logger,
                PrinterName,
                SerialVariableName,
                SerialShortVariableName,
                MacVariableName,
                UseManualSerialNumber,
                ManualSerialNumber,
                ManualMacAddress,
                Copies,
                UseQtProZplFormat,
                FailOnPrinterError,
                LabelModel);

        public bool IsVariableSerialMode => !UseManualSerialNumber;
        public bool IsManualQtProMode => UseManualSerialNumber && UseQtProZplFormat;
        public bool IsVariableQtProMode => !UseManualSerialNumber && UseQtProZplFormat;

        partial void OnUseManualSerialNumberChanged(bool value)
        {
            OnPropertyChanged(nameof(IsVariableSerialMode));
            OnPropertyChanged(nameof(IsManualQtProMode));
            OnPropertyChanged(nameof(IsVariableQtProMode));
        }

        partial void OnUseQtProZplFormatChanged(bool value)
        {
            OnPropertyChanged(nameof(IsManualQtProMode));
            OnPropertyChanged(nameof(IsVariableQtProMode));
        }

        public override NodeViewModel Clone() => new PrintLabelNodeViewModel
        {
            PrinterName = PrinterName,
            SerialVariableName = SerialVariableName,
            SerialShortVariableName = SerialShortVariableName,
            MacVariableName = MacVariableName,
            UseManualSerialNumber = UseManualSerialNumber,
            ManualSerialNumber = ManualSerialNumber,
            ManualMacAddress = ManualMacAddress,
            Copies = Copies,
            UseQtProZplFormat = UseQtProZplFormat,
            LabelModel = LabelModel,
            FailOnPrinterError = FailOnPrinterError
        };
    }
}
