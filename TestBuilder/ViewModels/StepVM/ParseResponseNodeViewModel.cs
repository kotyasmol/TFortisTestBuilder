using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;
namespace TestBuilder.ViewModels.StepVM;
public partial class ParseResponseNodeViewModel : NodeViewModel
{
    [ObservableProperty] private string sourceVariableName = "Console.Response";
    [ObservableProperty] private string parseMode = "Json";
    [ObservableProperty] private string selector = "";
    [ObservableProperty] private string valueType = "String";
    [ObservableProperty] private string outputVariableName = "Parsed.Value";
    public ConnectorViewModel In { get; }
    public ConnectorViewModel TrueOut { get; }
    public ConnectorViewModel FalseOut { get; }
    public ParseResponseNodeViewModel()
    {
        Title = "Parse Response";
        In = new ConnectorViewModel { Title = "In" }; TrueOut = new ConnectorViewModel { Title = "True" }; FalseOut = new ConnectorViewModel { Title = "False" };
        AddInput(In); AddOutput(TrueOut); AddOutput(FalseOut);
    }
    public ParseResponseOptions Options => new() { SourceVariableName = SourceVariableName, ParseMode = ParseMode, Selector = Selector, ValueType = ValueType, OutputVariableName = OutputVariableName };
    public ITestStep CreateStep(ILogger logger) => new ParseResponseStep(Options, logger);
    public static ParseResponseNodeViewModel FromOptions(ParseResponseOptions o) => new() { SourceVariableName = o.SourceVariableName, ParseMode = o.ParseMode, Selector = o.Selector, ValueType = o.ValueType, OutputVariableName = o.OutputVariableName };
    public override NodeViewModel Clone() => FromOptions(Options);
}
