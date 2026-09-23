using System.Net;
using System.Net.Http;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Http;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class UpdatePswFirmwareStepTests
{
    [Theory]
    [InlineData("20c", 524)]
    [InlineData("0x20d", 525)]
    [InlineData("0.2.13", 525)]
    [InlineData("0.2.8", 520)]
    [InlineData("520", 520)]
    public void ParsesLegacyAndDottedVersions(string text, int expected)
    {
        Assert.True(UpdatePswFirmwareStep.TryParseVersion(text, out var version));
        Assert.Equal(expected, version);
    }

    [Fact]
    public void ParsesNumericOnlyDeviceVersionAsHex()
    {
        Assert.True(UpdatePswFirmwareStep.TryParseVersion("208", out var version, deviceValue: true));
        Assert.Equal(520, version);
    }

    [Fact]
    public async Task MissingImageStopsBeforeAnyHttpRequest()
    {
        var handler = new RecordingHandler();
        var context = Context("20c");
        var step = CreateStep("/missing/sw407-0.2.8-01.06.2021.img", handler, true);

        Assert.Equal(StepResult.False, await step.ExecuteAsync(context, CancellationToken.None));
        Assert.Empty(handler.Requests);
        Assert.True(context.HasCriticalError);
    }

    [Fact]
    public async Task NewerFirmwareSkipsWithoutImageOrHttp()
    {
        var handler = new RecordingHandler();
        var context = Context("20e");
        var step = CreateStep("/missing/sw407-0.2.8-01.06.2021.img", handler, false);

        Assert.Equal(StepResult.True, await step.ExecuteAsync(context, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task EmptyImageStopsBeforeClear()
    {
        var (directory, path) = CreateImage();
        try
        {
            var handler = new RecordingHandler();
            File.WriteAllBytes(path, []);
            var step = CreateStep(path, handler, true);
            var context = Context("20c");
            Assert.Equal(StepResult.False, await step.ExecuteAsync(context, CancellationToken.None));
            Assert.Empty(handler.Requests);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task UploadsThenConfirmsAndVerifiesVersion()
    {
        var (directory, path) = CreateImage();
        try
        {
            var handler = new RecordingHandler();
            var context = Context("20c");
            var delays = new List<TimeSpan>();
            var step = CreateStep(path, handler, true,
                (ctx, _) =>
                {
                    ctx.SetVariable("Dut.firmvare_vers", "208");
                    return Task.FromResult(StepResult.True);
                },
                (duration, _) => { delays.Add(duration); return Task.CompletedTask; });

            Assert.Equal(StepResult.True, await step.ExecuteAsync(context, CancellationToken.None));
            Assert.Equal(new[] { "GET /clear.shtml", "POST /mngt/update.shtml",
                "GET /mngt/update.shtml?Update=Update" }, handler.Requests);
            Assert.Contains("name=\"updatefile\"; filename=\"sw407-0.2.8-01.06.2021.img\"", handler.UploadBody);
            Assert.Contains("multipart/form-data; boundary=", handler.UploadContentType);
            Assert.Equal(new double[] { 10, 10, 40 }, delays.Select(d => d.TotalSeconds));
            Assert.True(context.GetVariable<bool>("Firmware.Updated"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task MissingPostUpdateVersionDoesNotReuseOldValue()
    {
        var (directory, path) = CreateImage();
        try
        {
            var context = Context("20c");
            var step = CreateStep(path, new RecordingHandler(), true,
                (_, _) => Task.FromResult(StepResult.True),
                (_, _) => Task.CompletedTask);
            Assert.Equal(StepResult.False, await step.ExecuteAsync(context, CancellationToken.None));
            Assert.False(context.Variables.ContainsKey("Dut.firmvare_vers"));
            Assert.Contains("нет корректной версии", context.GetVariable<string>("Firmware.Error"));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static TestContext Context(string version)
    {
        var context = new TestContext(new RegisterState());
        context.SetVariable("Dut.firmvare_vers", version);
        return context;
    }

    private static (string Directory, string Path) CreateImage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "psw-firmware-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "sw407-0.2.8-01.06.2021.img");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        return (directory, path);
    }

    private static UpdatePswFirmwareStep CreateStep(string path, RecordingHandler handler,
        bool forceUpdate,
        Func<TestContext, CancellationToken, Task<StepResult>>? refresh = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        new(new UnusedHttp(), NullLogger.Instance, "http://192.168.0.1", path,
            "0.2.8", "Dut.firmvare_vers", forceUpdate, () => new HttpClient(handler), refresh, delay);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public string UploadBody { get; private set; } = string.Empty;
        public string UploadContentType { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri?.PathAndQuery}");
            if (request.Method == HttpMethod.Post && request.Content != null)
            {
                UploadBody = await request.Content.ReadAsStringAsync(cancellationToken);
                UploadContentType = request.Content.Headers.ContentType?.ToString() ?? string.Empty;
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class UnusedHttp : IHttpRequestService
    {
        public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout,
            CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}
