using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class WaitVariableUntilNodeViewModel : NodeViewModel
    {
        [ObservableProperty] private string variableName = "Dut.ups_rez";
        [ObservableProperty] private string expectedValue = "1";
        [ObservableProperty] private VariableComparisonType comparisonType = VariableComparisonType.Number;
        [ObservableProperty] private string pollAction = "GetUpsStatus";
        [ObservableProperty] private string baseUrl = "http://192.168.0.1";
        [ObservableProperty] private int requestTimeoutMs = 5000;
        [ObservableProperty] private int timeoutMs = 160000;
        [ObservableProperty] private int intervalMs = 5000;
        [ObservableProperty] private bool failOnTimeout = true;

        public ConnectorViewModel In { get; }
        public ConnectorViewModel TrueOut { get; }
        public ConnectorViewModel FalseOut { get; }

        public WaitVariableUntilNodeViewModel()
        {
            Title = "Wait Variable Until";

            In = new ConnectorViewModel { Title = "In", Parent = this };
            TrueOut = new ConnectorViewModel { Title = "True", Parent = this };
            FalseOut = new ConnectorViewModel { Title = "False", Parent = this };

            Input.Add(In);
            Output.Add(TrueOut);
            Output.Add(FalseOut);
        }

        public ITestStep CreateStep(IHttpRequestService httpRequestService, ILogger logger) =>
            new WaitVariableUntilStep(
                httpRequestService,
                logger,
                VariableName,
                ExpectedValue,
                ComparisonType,
                PollAction,
                BaseUrl,
                RequestTimeoutMs,
                TimeoutMs,
                IntervalMs,
                FailOnTimeout);

        public override NodeViewModel Clone() => new WaitVariableUntilNodeViewModel
        {
            VariableName = VariableName,
            ExpectedValue = ExpectedValue,
            ComparisonType = ComparisonType,
            PollAction = PollAction,
            BaseUrl = BaseUrl,
            RequestTimeoutMs = RequestTimeoutMs,
            TimeoutMs = TimeoutMs,
            IntervalMs = IntervalMs,
            FailOnTimeout = FailOnTimeout
        };
    }
}
