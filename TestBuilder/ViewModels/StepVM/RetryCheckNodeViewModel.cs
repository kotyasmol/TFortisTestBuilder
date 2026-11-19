using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Linq;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM;

public partial class RetryCheckNodeViewModel : CompositeNodeViewModel
{
    [ObservableProperty] private int maxAttempts = 2;
    [ObservableProperty] private int delayMs = 1000;

    public ConnectorViewModel In { get; } = new() { Title = "In" };
    public ConnectorViewModel SuccessOut { get; } = new() { Title = "Success" };
    public ConnectorViewModel ErrorOut { get; } = new() { Title = "Error" };

    public RetryCheckNodeViewModel()
    {
        Title = "Retry Check";
        BodyGraph.Title = "Проверки для повтора";
        BodyGraph.UsesBodyBoundaryNodes = true;
        AddInput(In); AddOutput(SuccessOut); AddOutput(ErrorOut);
        EnsureDefaultBodyNodes();
    }

    public override void EnsureDefaultBodyNodes()
    {
        if (!BodyGraph.Nodes.Any(n => n is BodyStartNodeViewModel))
            BodyGraph.Nodes.Add(new BodyStartNodeViewModel { Location = new Point(100, 120), CustomName = "Начало проверки", OperatorName = "Начало проверки" });
        if (!BodyGraph.Nodes.Any(n => n is BodyEndNodeViewModel))
            BodyGraph.Nodes.Add(new BodyEndNodeViewModel { Location = new Point(560, 120), CustomName = "Конец проверки", OperatorName = "Конец проверки" });
    }

    public override NodeViewModel Clone() => new RetryCheckNodeViewModel { MaxAttempts = MaxAttempts, DelayMs = DelayMs };
}
