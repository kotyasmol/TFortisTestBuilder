using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;
namespace TestBuilder.ViewModels.StepVM;
public partial class RunProgramNodeViewModel : NodeViewModel
{
    [ObservableProperty] private string programPath = "";
    [ObservableProperty] private string arguments = "";
    [ObservableProperty] private string workingDirectory = "";
    [ObservableProperty] private int timeoutMs = 60000;
    [ObservableProperty] private int expectedExitCode = 0;
    [ObservableProperty] private string outputPrefix = "Program";
    public ConnectorViewModel In { get; }
    public ConnectorViewModel TrueOut { get; }
    public ConnectorViewModel FalseOut { get; }
    public RunProgramNodeViewModel()
    {
        Title = "Run Program";
        In = new ConnectorViewModel { Title = "In" }; TrueOut = new ConnectorViewModel { Title = "True" }; FalseOut = new ConnectorViewModel { Title = "False" };
        AddInput(In); AddOutput(TrueOut); AddOutput(FalseOut);
    }
    public RunProgramOptions Options => new() { ProgramPath = ProgramPath, Arguments = Arguments, WorkingDirectory = WorkingDirectory, TimeoutMs = TimeoutMs, ExpectedExitCode = ExpectedExitCode, OutputPrefix = OutputPrefix };
    public ITestStep CreateStep(IExternalProgramRunner runner, ILogger logger) => new RunProgramStep(Options, runner, logger);
    public static RunProgramNodeViewModel FromOptions(RunProgramOptions o) => new() { ProgramPath = o.ProgramPath, Arguments = o.Arguments, WorkingDirectory = o.WorkingDirectory, TimeoutMs = o.TimeoutMs, ExpectedExitCode = o.ExpectedExitCode, OutputPrefix = o.OutputPrefix };
    public override NodeViewModel Clone() => FromOptions(Options);
}
