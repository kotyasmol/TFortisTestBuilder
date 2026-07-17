using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class SendTestReportNodeViewModel : NodeViewModel
    {
        [ObservableProperty] private string serverBaseUrl = "http://server-address";
        [ObservableProperty] private string reportVariableName = "TestReportJson";
        [ObservableProperty] private string endpoint = "/api/Api.svc/result.json";
        [ObservableProperty] private int timeoutMs = 10000;
        [ObservableProperty] private int retryCount = 1;
        [ObservableProperty] private int retryDelayMs = 1000;
        [ObservableProperty] private bool saveLocalCopy = true;
        [ObservableProperty] private string localReportsDirectory = "reports";
        [ObservableProperty] private bool failOnError;

        public ConnectorViewModel In { get; }
        public ConnectorViewModel TrueOut { get; }
        public ConnectorViewModel FalseOut { get; }

        public SendTestReportNodeViewModel()
        {
            Title = "Send Test Report";
            In = new ConnectorViewModel { Title = "In", Parent = this };
            TrueOut = new ConnectorViewModel { Title = "True", Parent = this };
            FalseOut = new ConnectorViewModel { Title = "False", Parent = this };
            Input.Add(In);
            Output.Add(TrueOut);
            Output.Add(FalseOut);
        }

        public ITestStep CreateStep(ILogger logger) =>
            new SendTestReportStep(
                logger,
                ServerBaseUrlResolver.ResolveFromSettings(ServerBaseUrl),
                ReportVariableName,
                Endpoint,
                TimeoutMs,
                RetryCount,
                RetryDelayMs,
                SaveLocalCopy,
                LocalReportsDirectory,
                FailOnError);

        public override NodeViewModel Clone() => new SendTestReportNodeViewModel
        {
            ServerBaseUrl = ServerBaseUrl,
            ReportVariableName = ReportVariableName,
            Endpoint = Endpoint,
            TimeoutMs = TimeoutMs,
            RetryCount = RetryCount,
            RetryDelayMs = RetryDelayMs,
            SaveLocalCopy = SaveLocalCopy,
            LocalReportsDirectory = LocalReportsDirectory,
            FailOnError = FailOnError
        };
    }
}
