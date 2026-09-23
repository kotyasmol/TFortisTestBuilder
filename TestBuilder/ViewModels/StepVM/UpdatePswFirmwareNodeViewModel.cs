using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM;

public partial class UpdatePswFirmwareNodeViewModel : NodeViewModel
{
    [ObservableProperty] private string baseUrl = "http://192.168.0.1";
    [ObservableProperty] private string firmwarePath = string.Empty;
    [ObservableProperty] private string targetVersion = "0.2.13";
    [ObservableProperty] private string versionVariable = "Dut.firmvare_vers";
    [ObservableProperty] private string expectedSha256 = string.Empty;

    public ConnectorViewModel In { get; }
    public ConnectorViewModel TrueOut { get; }
    public ConnectorViewModel FalseOut { get; }

    public UpdatePswFirmwareNodeViewModel()
    {
        Title = "Update PSW Firmware";
        In = new ConnectorViewModel { Title = "In", Parent = this };
        TrueOut = new ConnectorViewModel { Title = "True", Parent = this };
        FalseOut = new ConnectorViewModel { Title = "False", Parent = this };
        Input.Add(In);
        Output.Add(TrueOut);
        Output.Add(FalseOut);
    }

    public ITestStep CreateStep(IHttpRequestService http, ILogger logger) =>
        new UpdatePswFirmwareStep(http, logger, BaseUrl, FirmwarePath, TargetVersion,
            VersionVariable, ExpectedSha256);

    public override NodeViewModel Clone() => new UpdatePswFirmwareNodeViewModel
    {
        BaseUrl = BaseUrl,
        FirmwarePath = FirmwarePath,
        TargetVersion = TargetVersion,
        VersionVariable = VersionVariable,
        ExpectedSha256 = ExpectedSha256
    };
}
