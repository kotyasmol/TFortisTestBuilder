using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class GetSerialNumberFromServerNodeViewModel : NodeViewModel
    {
        [ObservableProperty] private string serverBaseUrl = string.Empty;
        [ObservableProperty] private string deviceType = "PSW+UPS-Box 8x2Pro";
        [ObservableProperty] private string cpuIdVariableName = "Dut.cpu_id";
        [ObservableProperty] private int timeoutMs = 30000;
        [ObservableProperty] private int retryCount = 1;
        [ObservableProperty] private int retryDelayMs = 1000;
        [ObservableProperty] private string outputVariableName = "SerialNumber";
        [ObservableProperty] private bool failOnError = true;

        public ConnectorViewModel In { get; }
        public ConnectorViewModel TrueOut { get; }
        public ConnectorViewModel FalseOut { get; }

        public GetSerialNumberFromServerNodeViewModel()
        {
            Title = "Get Serial Number";
            In = new ConnectorViewModel { Title = "In", Parent = this };
            TrueOut = new ConnectorViewModel { Title = "True", Parent = this };
            FalseOut = new ConnectorViewModel { Title = "False", Parent = this };
            Input.Add(In);
            Output.Add(TrueOut);
            Output.Add(FalseOut);
        }

        public ITestStep CreateStep(IHttpRequestService httpRequestService, ILogger logger) =>
            new GetSerialNumberFromServerStep(
                httpRequestService,
                logger,
                ServerBaseUrlResolver.ResolveFromSettings(ServerBaseUrl),
                DeviceType,
                CpuIdVariableName,
                TimeoutMs,
                RetryCount,
                RetryDelayMs,
                OutputVariableName,
                FailOnError);

        public override NodeViewModel Clone() => new GetSerialNumberFromServerNodeViewModel
        {
            ServerBaseUrl = ServerBaseUrl,
            DeviceType = DeviceType,
            CpuIdVariableName = CpuIdVariableName,
            TimeoutMs = TimeoutMs,
            RetryCount = RetryCount,
            RetryDelayMs = RetryDelayMs,
            OutputVariableName = OutputVariableName,
            FailOnError = FailOnError
        };
    }
}
