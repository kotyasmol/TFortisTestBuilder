using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Services;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class PrintLabelNodeViewModel : NodeViewModel
    {
        public string[] PrintFormats { get; } = { "ZPL · Изделие, MAC, SN", "EPL · Серийный номер" };
        public int SelectedPrintFormat
        {
            get => (LabelTemplate?.Language ?? (UseQtProZplFormat ? "ZPL" : "EPL")) == "ZPL" ? 0 : 1;
            set
            {
                if (value is < 0 or > 1 || value == SelectedPrintFormat) return;
                LabelTemplate = null;
                UseQtProZplFormat = value == 0;
                OnPropertyChanged(nameof(SelectedPrintFormat));
            }
        }
        [ObservableProperty] private LabelTemplateDefinition? labelTemplate;
        public string CurrentTemplateName => LabelTemplate?.Name ?? (UseQtProZplFormat ? LabelModel.ToString() : "Только серийный номер");
        partial void OnLabelTemplateChanged(LabelTemplateDefinition? value)
        {
            if (value != null) UseQtProZplFormat = value.UseShortSerial;
            OnPropertyChanged(nameof(CurrentTemplateName));
            OnPropertyChanged(nameof(SelectedPrintFormat));
        }
        [RelayCommand]
        private async System.Threading.Tasks.Task ChooseTemplate()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var selected = await new TestBuilder.Views.LabelTemplatesWindow(LabelTemplate).ShowDialog<LabelTemplateDefinition?>(desktop.MainWindow);
                if (selected != null) LabelTemplate = selected.Copy();
            }
        }
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
        public string[] LabelModelNames { get; } = { "PSW+UPS-Box 8x2Pro", "PSW-2G6F+" };
        public int SelectedLabelModelIndex
        {
            get => (int)LabelModel;
            set { if (System.Enum.IsDefined(typeof(DeviceLabelModel), value)) LabelModel = (DeviceLabelModel)value; }
        }
        partial void OnLabelModelChanged(DeviceLabelModel value) { OnPropertyChanged(nameof(SelectedLabelModelIndex)); OnPropertyChanged(nameof(CurrentTemplateName)); }
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

        public ITestStep CreateStep(ILogger logger) => CreateStep(logger, null);

        internal ITestStep CreateStep(ILogger logger, IRawLabelPrinter? printer) =>
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
                printer ?? new WindowsRawLabelPrinter(),
                30000,
                LabelModel, LabelTemplate);

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
            OnPropertyChanged(nameof(CurrentTemplateName));
            OnPropertyChanged(nameof(SelectedPrintFormat));
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
            LabelTemplate = LabelTemplate?.Copy(),
            FailOnPrinterError = FailOnPrinterError
        };
    }
}
