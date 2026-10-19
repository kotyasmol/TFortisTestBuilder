using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;
namespace TestBuilder.ViewModels.StepVM;
public partial class CheckLedColorNodeViewModel : NodeViewModel
{
    [ObservableProperty] private byte slaveId = 1;
    [ObservableProperty] private bool useCurrentSlaveId = false;
    [ObservableProperty] private ushort redAddress = 2437;
    [ObservableProperty] private ushort greenAddress = 2453;
    [ObservableProperty] private ushort blueAddress = 2469;
    [ObservableProperty] private ushort whiteAddress = 2421;
    [ObservableProperty] private string expectedColor = "green";
    [ObservableProperty] private int colorMargin = 20;
    [ObservableProperty] private int minIntensity = 50;
    [ObservableProperty] private string outputPrefix = "Led";
    public ConnectorViewModel In { get; }
    public ConnectorViewModel TrueOut { get; }
    public ConnectorViewModel FalseOut { get; }
    public CheckLedColorNodeViewModel()
    {
        Title = "Check LED Color";
        In = new ConnectorViewModel { Title = "In" }; TrueOut = new ConnectorViewModel { Title = "True" }; FalseOut = new ConnectorViewModel { Title = "False" };
        AddInput(In); AddOutput(TrueOut); AddOutput(FalseOut);
    }
    public CheckLedColorOptions Options => new() { SlaveId = SlaveId, UseCurrentSlaveId = UseCurrentSlaveId, RedAddress = RedAddress, GreenAddress = GreenAddress, BlueAddress = BlueAddress, WhiteAddress = WhiteAddress, ExpectedColor = ExpectedColor, ColorMargin = ColorMargin, MinIntensity = MinIntensity, OutputPrefix = OutputPrefix };
    public ITestStep CreateStep(ILogger logger) => new CheckLedColorStep(Options, logger);
    public static CheckLedColorNodeViewModel FromOptions(CheckLedColorOptions o) => new() { SlaveId = o.SlaveId, UseCurrentSlaveId = o.UseCurrentSlaveId, RedAddress = o.RedAddress, GreenAddress = o.GreenAddress, BlueAddress = o.BlueAddress, WhiteAddress = o.WhiteAddress, ExpectedColor = o.ExpectedColor, ColorMargin = o.ColorMargin, MinIntensity = o.MinIntensity, OutputPrefix = o.OutputPrefix };
    public override NodeViewModel Clone() => FromOptions(Options);
}
