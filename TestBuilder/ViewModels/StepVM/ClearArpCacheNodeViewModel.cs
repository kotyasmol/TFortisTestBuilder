using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class ClearArpCacheNodeViewModel : NodeViewModel
    {
        [ObservableProperty] private bool runArpdBat = true;
        [ObservableProperty] private string arpdBatPath = "arpd.bat";
        [ObservableProperty] private string command = "arp";
        [ObservableProperty] private string arguments = "-d *";
        [ObservableProperty] private int timeoutMs = 5000;
        [ObservableProperty] private bool failOnError;

        public ConnectorViewModel In { get; }
        public ConnectorViewModel TrueOut { get; }
        public ConnectorViewModel FalseOut { get; }

        public ClearArpCacheNodeViewModel()
        {
            Title = "Clear ARP Cache";
            In = new ConnectorViewModel { Title = "In", Parent = this };
            TrueOut = new ConnectorViewModel { Title = "True", Parent = this };
            FalseOut = new ConnectorViewModel { Title = "False", Parent = this };
            Input.Add(In);
            Output.Add(TrueOut);
            Output.Add(FalseOut);
        }

        public ITestStep CreateStep(ILogger logger) => new ClearArpCacheStep(logger, RunArpdBat, ArpdBatPath, Command, Arguments, TimeoutMs, FailOnError);

        public override NodeViewModel Clone() => new ClearArpCacheNodeViewModel
        {
            RunArpdBat = RunArpdBat,
            ArpdBatPath = ArpdBatPath,
            Command = Command,
            Arguments = Arguments,
            TimeoutMs = TimeoutMs,
            FailOnError = FailOnError
        };
    }
}
