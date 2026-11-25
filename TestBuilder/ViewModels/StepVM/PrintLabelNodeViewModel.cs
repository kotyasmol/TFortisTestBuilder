using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using TestBuilder.Services;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class PrintLabelNodeViewModel : NodeViewModel
    {
        public string[] DataSources { get; } = { "Из переменных", "Ввести вручную", "Из последнего теста" };
        public int SelectedDataSource
        {
            get => UseLastTestLabel ? 2 : UseManualSerialNumber ? 1 : 0;
            set
            {
                if (value is < 0 or > 2 || value == SelectedDataSource) return;
                DataSourceFlags = (value == 2, value == 1);
            }
        }
        // The profile format stores two flags. History records their combined value as one edit.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(UseLastTestLabel), nameof(UseManualSerialNumber), nameof(SelectedDataSource),
            nameof(IsNewLabelMode), nameof(IsLegacyFormatVisible), nameof(IsManualSerialMode), nameof(IsVariableSerialMode),
            nameof(IsManualQtProMode), nameof(IsVariableQtProMode))]
        private (bool LastTest, bool ManualSerialNumber) dataSourceFlags;

        public bool UseLastTestLabel
        {
            get => DataSourceFlags.LastTest;
            set => DataSourceFlags = (value, UseManualSerialNumber);
        }

        public bool UseManualSerialNumber
        {
            get => DataSourceFlags.ManualSerialNumber;
            set => DataSourceFlags = (UseLastTestLabel, value);
        }

        public bool IsNewLabelMode => !UseLastTestLabel;
        public bool IsManualSerialMode => !UseLastTestLabel && UseManualSerialNumber;
        public string[] PrintFormats { get; } = { "ZPL · Изделие, MAC, SN", "EPL · Серийный номер" };
        public int SelectedPrintFormat
        {
            get => (LabelTemplate?.Language ?? (UseQtProZplFormat ? "ZPL" : "EPL")) == "ZPL" ? 0 : 1;
            set
            {
                if (value is < 0 or > 1 || value == SelectedPrintFormat) return;
                LabelSelection = (null, null, value == 0);
            }
        }
        // Template ID and portable snapshot form one undoable profile edit.
        [ObservableProperty] private (string? TypeId, LabelTemplateDefinition? Template, bool Pro) labelSelection;
        public string? LabelTypeId { get => LabelSelection.TypeId; set => LabelSelection = (value, LabelTemplate, UseQtProZplFormat); }
        public LabelTemplateDefinition? LabelTemplate
        {
            get => LabelSelection.Template;
            set => LabelSelection = (LabelTypeId, value, value?.UseShortSerial ?? UseQtProZplFormat);
        }
        public bool UseQtProZplFormat { get => LabelSelection.Pro; set => LabelSelection = (LabelTypeId, LabelTemplate, value); }
        private IReadOnlyList<LabelTypeDefinition> _savedTypes = LabelTypeStore.Available(new LabelTypeFile());
        public IReadOnlyList<LabelTypeChoice> LabelTypeChoices => new[] { new LabelTypeChoice("", "По изделию профиля") }
            .Concat(_savedTypes.Select(t => new LabelTypeChoice(t.Id, t.Name)))
            .Concat(!string.IsNullOrEmpty(LabelTypeId) && !_savedTypes.Any(t => t.Id == LabelTypeId)
                ? new[] { new LabelTypeChoice(LabelTypeId, "Сохранённая копия шаблона") } : Array.Empty<LabelTypeChoice>()).ToArray();
        public LabelTypeChoice? SelectedLabelType
        {
            get => LabelTypeChoices.FirstOrDefault(t => t.Id == (LabelTypeId ?? ""));
            set
            {
                if (value == null || value.Id == (LabelTypeId ?? "")) return;
                if (value.Id.Length == 0) { LabelTypeId = null; return; }
                var type = _savedTypes.FirstOrDefault(t => t.Id == value.Id);
                if (type == null) return;
                var fallback = LabelTemplate ?? LabelTemplateCatalog.Legacy(UseQtProZplFormat ? LabelModel.ToString() : "SerialOnlyEpl");
                var snapshot = LabelTypeStore.ApplyPresentation(type.Template, fallback, null, fallback.Name);
                LabelSelection = (type.Id, snapshot, snapshot.UseShortSerial);
            }
        }
        public void RefreshLabelTypes()
        {
            try { _savedTypes = LabelTypeStore.Available(LabelTypeStore.LoadFile(AppSettings.Instance.ResolvedGraphsFolder)); LabelTypeStatus = ""; }
            catch (Exception ex) when (LabelTypesViewModel.IsCatalogError(ex)) { LabelTypeStatus = ex.Message; }
            OnPropertyChanged(nameof(LabelTypeChoices)); OnPropertyChanged(nameof(SelectedLabelType));
        }
        [ObservableProperty] private string labelTypeStatus = "";
        public bool IsLegacyFormatVisible => IsNewLabelMode && string.IsNullOrEmpty(LabelTypeId);
        public string CurrentTemplateName => LabelTemplate?.Name ?? (UseQtProZplFormat ? "Общий макет ZPL" : "Только серийный номер");
        partial void OnLabelSelectionChanged((string? TypeId, LabelTemplateDefinition? Template, bool Pro) value)
        {
            foreach (var name in new[] { nameof(LabelTypeId), nameof(LabelTemplate), nameof(UseQtProZplFormat),
                nameof(CurrentTemplateName), nameof(SelectedPrintFormat), nameof(SelectedLabelType),
                nameof(IsLegacyFormatVisible), nameof(IsManualQtProMode), nameof(IsVariableQtProMode) }) OnPropertyChanged(name);
        }
        [ObservableProperty] private string printerName = "TSC TE310";
        [ObservableProperty] private bool useDefaultPrinter = true;
        [ObservableProperty] private string serialVariableName = "SerialNumber";
        [ObservableProperty] private string serialShortVariableName = "SerialShort";
        [ObservableProperty] private string macVariableName = "Dut.default_mac";
        [ObservableProperty] private string manualSerialNumber = string.Empty;
        [ObservableProperty] private string manualMacAddress = string.Empty;
        [ObservableProperty] private int copies = 4;
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

        internal ITestStep CreateStep(ILogger logger, IRawLabelPrinter? printer)
        {
            var workstationPrinter = UseDefaultPrinter ? AppSettings.Instance.DefaultPrinterName : null;
            return new PrintLabelStep(
                logger,
                string.IsNullOrWhiteSpace(workstationPrinter) ? PrinterName : workstationPrinter,
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
                LabelModel, LabelTemplate, UseLastTestLabel, labelTypeId: LabelTypeId);
        }

        public bool IsVariableSerialMode => !UseLastTestLabel && !UseManualSerialNumber;
        public bool IsManualQtProMode => IsManualSerialMode && UseQtProZplFormat;
        public bool IsVariableQtProMode => IsVariableSerialMode && UseQtProZplFormat;

        public override NodeViewModel Clone() => new PrintLabelNodeViewModel
        {
            PrinterName = PrinterName,
            UseDefaultPrinter = UseDefaultPrinter,
            SerialVariableName = SerialVariableName,
            SerialShortVariableName = SerialShortVariableName,
            MacVariableName = MacVariableName,
            UseManualSerialNumber = UseManualSerialNumber,
            UseLastTestLabel = UseLastTestLabel,
            ManualSerialNumber = ManualSerialNumber,
            ManualMacAddress = ManualMacAddress,
            Copies = Copies,
            UseQtProZplFormat = UseQtProZplFormat,
            LabelModel = LabelModel,
            LabelTemplate = LabelTemplate?.Copy(),
            LabelTypeId = LabelTypeId,
            FailOnPrinterError = FailOnPrinterError
        };
    }
}
