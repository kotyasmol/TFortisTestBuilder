using System;
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
        [ObservableProperty, NotifyPropertyChangedFor(nameof(EffectiveTimeoutText))] private string url = SelfTestCheckStep.DefaultUrl;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(EffectiveTimeoutText))] private int timeoutMs = SelfTestCheckStep.DefaultTimeoutMs;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(EffectiveTimeoutText))] private bool useLegacyTimeout;
        [ObservableProperty] private int browserSettleDelayMs = SelfTestCheckStep.DefaultBrowserSettleDelayMs;
        [ObservableProperty] private int pollIntervalMs = SelfTestCheckStep.DefaultPollIntervalMs;
        [ObservableProperty] private string outputPrefix = SelfTestCheckStep.DefaultOutputPrefix;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(IsReadingField))] private string readField = string.Empty;
        [ObservableProperty] private string validationRules = SelfTestCheckStep.DefaultValidationRules;
        [ObservableProperty] private bool failOnError = true;

        // Retain checks explicitly enabled in profiles saved with the former control.
        internal bool Psw2G6FHardwareChecks { get; set; }

        public ConnectorViewModel In { get; }
        public ConnectorViewModel TrueOut { get; }
        public ConnectorViewModel FalseOut { get; }

        public string EffectiveTimeoutText => $"Фактический таймаут {SelfTestCheckStep.EffectiveTimeout(Url, TimeoutMs, UseLegacyTimeout)} мс";
        public bool IsReadingField => !string.IsNullOrWhiteSpace(ReadField);
        internal bool RequiresTimingVersion => Math.Max(0, BrowserSettleDelayMs) != SelfTestCheckStep.DefaultBrowserSettleDelayMs ||
            (!UseLegacyTimeout && SelfTestCheckStep.EffectiveTimeout(Url, TimeoutMs, false) != SelfTestCheckStep.EffectiveTimeout(Url, TimeoutMs, true));

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
                pollIntervalMs: PollIntervalMs,
                enforceMinimumDeviceReadyTimeout: UseLegacyTimeout,
                psw2G6FHardwareChecks: Psw2G6FHardwareChecks,
                browserSettleDelayMs: BrowserSettleDelayMs,
                readField: ReadField);
        }

        public override NodeViewModel Clone() => new SelfTestCheckNodeViewModel
        {
            Url = Url,
            TimeoutMs = TimeoutMs,
            UseLegacyTimeout = UseLegacyTimeout,
            BrowserSettleDelayMs = BrowserSettleDelayMs,
            PollIntervalMs = PollIntervalMs,
            OutputPrefix = OutputPrefix,
            ReadField = ReadField,
            ValidationRules = ValidationRules,
            FailOnError = FailOnError,
            Psw2G6FHardwareChecks = Psw2G6FHardwareChecks
        };
    }
}
