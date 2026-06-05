using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class RequestTestPageNodeViewModel : NodeViewModel
    {
        [ObservableProperty]
        private string baseUrl = RequestTestPageStep.DefaultBaseUrl;

        [ObservableProperty]
        private string path = RequestTestPageStep.DefaultPath;

        [ObservableProperty]
        private int timeoutMs = RequestTestPageStep.DefaultTimeoutMs;

        [ObservableProperty]
        private int retryCount = RequestTestPageStep.DefaultRetryCount;

        [ObservableProperty]
        private int retryDelayMs = RequestTestPageStep.DefaultRetryDelayMs;

        [ObservableProperty]
        private string outputVariableName = RequestTestPageStep.DefaultOutputVariableName;

        [ObservableProperty]
        private bool failOnError = true;

        [ObservableProperty]
        private bool requireSuccessStatusCode = true;

        [ObservableProperty]
        private string expectedContentContains = RequestTestPageStep.DefaultExpectedContentContains;

        [ObservableProperty]
        private string saveStatusCodeTo = RequestTestPageStep.DefaultStatusCodeVariableName;

        [ObservableProperty]
        private string saveErrorTo = RequestTestPageStep.DefaultErrorVariableName;

        [ObservableProperty]
        private string saveElapsedMsTo = RequestTestPageStep.DefaultElapsedMsVariableName;

        public ConnectorViewModel In { get; }

        public ConnectorViewModel TrueOut { get; }

        public ConnectorViewModel FalseOut { get; }

        public RequestTestPageNodeViewModel()
        {
            Title = "Request Test Page";

            In = new ConnectorViewModel
            {
                Title = "In",
                Parent = this
            };

            TrueOut = new ConnectorViewModel
            {
                Title = "True",
                Parent = this
            };

            FalseOut = new ConnectorViewModel
            {
                Title = "False",
                Parent = this
            };

            Input.Add(In);
            Output.Add(TrueOut);
            Output.Add(FalseOut);
        }

        public ITestStep CreateStep(IHttpRequestService httpRequestService, ILogger logger)
        {
            return new RequestTestPageStep(
                httpRequestService,
                logger,
                BaseUrl,
                Path,
                TimeoutMs,
                RetryCount,
                RetryDelayMs,
                OutputVariableName,
                FailOnError,
                RequireSuccessStatusCode,
                ExpectedContentContains,
                SaveStatusCodeTo,
                SaveErrorTo,
                SaveElapsedMsTo);
        }

        public override NodeViewModel Clone() => new RequestTestPageNodeViewModel
        {
            BaseUrl = BaseUrl,
            Path = Path,
            TimeoutMs = TimeoutMs,
            RetryCount = RetryCount,
            RetryDelayMs = RetryDelayMs,
            OutputVariableName = OutputVariableName,
            FailOnError = FailOnError,
            RequireSuccessStatusCode = RequireSuccessStatusCode,
            ExpectedContentContains = ExpectedContentContains,
            SaveStatusCodeTo = SaveStatusCodeTo,
            SaveErrorTo = SaveErrorTo,
            SaveElapsedMsTo = SaveElapsedMsTo
        };
    }
}
