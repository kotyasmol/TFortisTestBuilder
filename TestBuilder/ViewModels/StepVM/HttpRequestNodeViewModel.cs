using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    /// <summary>
    /// ViewModel ноды HTTP_REQUEST.
    /// </summary>
    public partial class HttpRequestNodeViewModel : NodeViewModel
    {
        [ObservableProperty]
        private string url = HttpRequestStep.DefaultUrl;

        [ObservableProperty]
        private int timeoutMs = HttpRequestStep.DefaultTimeoutMs;

        [ObservableProperty]
        private string outputVariableName = RequestTestPageStep.DefaultOutputVariableName;

        [ObservableProperty]
        private bool requireSuccessStatusCode = true;

        public ConnectorViewModel In { get; }

        public ConnectorViewModel TrueOut { get; }

        public ConnectorViewModel FalseOut { get; }

        public HttpRequestNodeViewModel()
        {
            Title = "HTTP Request";

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
            return new HttpRequestStep(
                httpRequestService,
                logger,
                Url,
                TimeoutMs,
                OutputVariableName,
                RequireSuccessStatusCode);
        }

        public override NodeViewModel Clone() => new HttpRequestNodeViewModel
        {
            Url = Url,
            TimeoutMs = TimeoutMs,
            OutputVariableName = OutputVariableName,
            RequireSuccessStatusCode = RequireSuccessStatusCode
        };
    }
}
