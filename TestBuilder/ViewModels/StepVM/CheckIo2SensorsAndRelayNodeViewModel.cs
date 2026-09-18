using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class CheckIo2SensorsAndRelayNodeViewModel : NodeViewModel
    {
        [ObservableProperty] private byte io2SlaveId;
        [ObservableProperty] private string baseUrl = CheckIo2SensorsAndRelayStep.DefaultBaseUrl;
        [ObservableProperty] private string selftestEndpoint = CheckIo2SensorsAndRelayStep.DefaultSelftestEndpoint;
        [ObservableProperty] private string relayEndpointTemplate = CheckIo2SensorsAndRelayStep.DefaultRelayEndpointTemplate;
        [ObservableProperty] private int requestTimeoutMs = CheckIo2SensorsAndRelayStep.DefaultRequestTimeoutMs;
        [ObservableProperty] private int stateTimeoutMs = CheckIo2SensorsAndRelayStep.DefaultStateTimeoutMs;
        [ObservableProperty] private int pollIntervalMs = CheckIo2SensorsAndRelayStep.DefaultPollIntervalMs;
        [ObservableProperty] private bool useBrowserForSelftest;
        [ObservableProperty] private bool checkRelay = true;

        public ConnectorViewModel In { get; }
        public ConnectorViewModel TrueOut { get; }
        public ConnectorViewModel FalseOut { get; }

        public CheckIo2SensorsAndRelayNodeViewModel()
        {
            Title = "Check IO-2 Sensors and Relay";

            In = new ConnectorViewModel { Title = "In", Parent = this };
            TrueOut = new ConnectorViewModel { Title = "True", Parent = this };
            FalseOut = new ConnectorViewModel { Title = "False", Parent = this };

            Input.Add(In);
            Output.Add(TrueOut);
            Output.Add(FalseOut);
        }

        public ITestStep CreateStep(
            IModbusService modbusService,
            IHttpRequestService httpRequestService,
            ILogger logger) =>
            new CheckIo2SensorsAndRelayStep(
                modbusService,
                httpRequestService,
                logger,
                Io2SlaveId,
                BaseUrl,
                SelftestEndpoint,
                RelayEndpointTemplate,
                RequestTimeoutMs,
                StateTimeoutMs,
                PollIntervalMs,
                UseBrowserForSelftest,
                CheckRelay);

        public override NodeViewModel Clone() => new CheckIo2SensorsAndRelayNodeViewModel
        {
            Io2SlaveId = Io2SlaveId,
            BaseUrl = BaseUrl,
            SelftestEndpoint = SelftestEndpoint,
            RelayEndpointTemplate = RelayEndpointTemplate,
            RequestTimeoutMs = RequestTimeoutMs,
            StateTimeoutMs = StateTimeoutMs,
            PollIntervalMs = PollIntervalMs,
            UseBrowserForSelftest = UseBrowserForSelftest,
            CheckRelay = CheckRelay
        };
    }
}
