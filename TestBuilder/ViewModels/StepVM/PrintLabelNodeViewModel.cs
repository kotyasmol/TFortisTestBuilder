using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class PrintLabelNodeViewModel : NodeViewModel
    {
        [ObservableProperty] private string printerName = "Zebra";
        [ObservableProperty] private string deviceName = "PSW+UPS-Box 8x2Pro";
        [ObservableProperty] private int deviceType = 32;
        [ObservableProperty] private string serialVariableName = "SerialShort";
        [ObservableProperty] private string macVariableName = "Dut.NewMac";
        [ObservableProperty] private int copies = 4;
        [ObservableProperty] private bool includeMac = true;
        [ObservableProperty] private bool equipmentFieldUse;
        [ObservableProperty] private int equipmentType;
        [ObservableProperty] private string equipmentText = string.Empty;
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
            new PrintLabelStep(logger, PrinterName, DeviceName, DeviceType, SerialVariableName, MacVariableName, Copies, IncludeMac, EquipmentFieldUse, EquipmentType, EquipmentText, FailOnPrinterError);

        public override NodeViewModel Clone() => new PrintLabelNodeViewModel
        {
            PrinterName = PrinterName,
            DeviceName = DeviceName,
            DeviceType = DeviceType,
            SerialVariableName = SerialVariableName,
            MacVariableName = MacVariableName,
            Copies = Copies,
            IncludeMac = IncludeMac,
            EquipmentFieldUse = EquipmentFieldUse,
            EquipmentType = EquipmentType,
            EquipmentText = EquipmentText,
            FailOnPrinterError = FailOnPrinterError
        };
    }
}
