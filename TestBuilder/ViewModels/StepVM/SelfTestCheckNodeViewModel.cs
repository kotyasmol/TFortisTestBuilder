using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class SelfTestCheckNodeViewModel : NodeViewModel
    {
        [ObservableProperty] private string url = SelfTestCheckStep.DefaultUrl;
        [ObservableProperty] private int timeoutMs = SelfTestCheckStep.DefaultTimeoutMs;
        [ObservableProperty] private int pollIntervalMs = SelfTestCheckStep.DefaultPollIntervalMs;
        [ObservableProperty] private string outputPrefix = SelfTestCheckStep.DefaultOutputPrefix;
        [ObservableProperty] private string validationRules = SelfTestCheckStep.DefaultValidationRules;
        [ObservableProperty] private bool failOnError = true;

        public ConnectorViewModel In { get; }
        public ConnectorViewModel TrueOut { get; }
        public ConnectorViewModel FalseOut { get; }

        public SelfTestCheckNodeViewModel()
        {
            Title = "Selftest Check";

            In = new ConnectorViewModel { Title = "In", Parent = this };
            TrueOut = new ConnectorViewModel { Title = "True", Parent = this };
            FalseOut = new ConnectorViewModel { Title = "False", Parent = this };

            Input.Add(In);
            Output.Add(TrueOut);
            Output.Add(FalseOut);
        }

        public ITestStep CreateStep(IHttpRequestService httpRequestService, ILogger logger)
        {
            return new SelfTestCheckStep(
                httpRequestService,
                logger,
                Url,
                TimeoutMs,
                OutputPrefix,
                ValidationRules,
                FailOnError,
                pollIntervalMs: PollIntervalMs);
        }

        public override NodeViewModel Clone() => new SelfTestCheckNodeViewModel
        {
            Url = Url,
            TimeoutMs = TimeoutMs,
            PollIntervalMs = PollIntervalMs,
            OutputPrefix = OutputPrefix,
            ValidationRules = ValidationRules,
            FailOnError = FailOnError
        };
    }
}
