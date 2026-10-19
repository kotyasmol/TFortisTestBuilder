using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;
namespace TestBuilder.ViewModels.StepVM;
public partial class SerialConsoleNodeViewModel : NodeViewModel
{
    [ObservableProperty] private string portName = "";
    [ObservableProperty] private int baudRate = 115200;
    [ObservableProperty] private string command = "";
    [ObservableProperty] private string responseTerminator = "";
    [ObservableProperty] private int quietMs = 500;
    [ObservableProperty] private int timeoutMs = 10000;
    [ObservableProperty] private string outputVariableName = "Console.Response";
    public ConnectorViewModel In { get; }
    public ConnectorViewModel TrueOut { get; }
    public ConnectorViewModel FalseOut { get; }
    public SerialConsoleNodeViewModel()
    {
        Title = "Serial Console";
        In = new ConnectorViewModel { Title = "In" }; TrueOut = new ConnectorViewModel { Title = "True" }; FalseOut = new ConnectorViewModel { Title = "False" };
        AddInput(In); AddOutput(TrueOut); AddOutput(FalseOut);
    }
    public SerialConsoleOptions Options => new() { PortName = PortName, BaudRate = BaudRate, Command = Command, ResponseTerminator = ResponseTerminator, QuietMs = QuietMs, TimeoutMs = TimeoutMs, OutputVariableName = OutputVariableName };
    public ITestStep CreateStep(ISerialConsoleSession console, ILogger logger) => new SerialConsoleStep(Options, console, logger);
    public static SerialConsoleNodeViewModel FromOptions(SerialConsoleOptions o) => new() { PortName = o.PortName, BaudRate = o.BaudRate, Command = o.Command, ResponseTerminator = o.ResponseTerminator, QuietMs = o.QuietMs, TimeoutMs = o.TimeoutMs, OutputVariableName = o.OutputVariableName };
    public override NodeViewModel Clone() => FromOptions(Options);
}
