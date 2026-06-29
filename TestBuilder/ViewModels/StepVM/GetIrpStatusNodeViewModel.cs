using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class GetIrpStatusNodeViewModel : NodeViewModel
    {
        [ObservableProperty] private string baseUrl = "http://192.168.0.1";
        [ObservableProperty] private int timeoutMs = 5000;
        [ObservableProperty] private string outputVariableName = "Dut.ups_det";
        [ObservableProperty] private bool failOnError = true;

        public ConnectorViewModel In { get; }
        public ConnectorViewModel TrueOut { get; }
        public ConnectorViewModel FalseOut { get; }

        public GetIrpStatusNodeViewModel()
        {
            Title = "Get IRP Status";

            In = new ConnectorViewModel { Title = "In", Parent = this };
            TrueOut = new ConnectorViewModel { Title = "True", Parent = this };
            FalseOut = new ConnectorViewModel { Title = "False", Parent = this };

            Input.Add(In);
            Output.Add(TrueOut);
            Output.Add(FalseOut);
        }

        public ITestStep CreateStep(IHttpRequestService httpRequestService, ILogger logger) =>
            new GetIrpStatusStep(httpRequestService, logger, BaseUrl, TimeoutMs, OutputVariableName, FailOnError);

        public override NodeViewModel Clone() => new GetIrpStatusNodeViewModel
        {
            BaseUrl = BaseUrl,
            TimeoutMs = TimeoutMs,
            OutputVariableName = OutputVariableName,
            FailOnError = FailOnError
        };
    }
}
