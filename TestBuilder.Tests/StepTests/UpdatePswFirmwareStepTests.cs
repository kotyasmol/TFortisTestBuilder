using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Http;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class UpdatePswFirmwareStepTests
{
    private const string Hash = "2896bf52255e9878edc3cbe68c725d24d8754709879e897f7fe27bb1295dc05a";

    [Theory]
    [InlineData("20c", 524)]
    [InlineData("0x20d", 525)]
    [InlineData("0.2.13", 525)]
    [InlineData("520", 520)]
    public void ParsesLegacyAndDottedVersions(string text, int expected)
    {
        Assert.True(UpdatePswFirmwareStep.TryParseVersion(text, out var version));
        Assert.Equal(expected, version);
    }

    [Fact]
    public async Task MissingImageStopsBeforeAnyHttpRequest()
    {
        var handler = new RecordingHandler();
        var context = Context("20c");
        var step = CreateStep("/missing/sw407-0.2.13-05.09.2025.img", Hash, handler);

        Assert.Equal(StepResult.False, await step.ExecuteAsync(context, CancellationToken.None));
        Assert.Empty(handler.Requests);
        Assert.True(context.HasCriticalError);
    }

    [Fact]
    public async Task NewerFirmwareSkipsWithoutImageOrHttp()
    {
        var handler = new RecordingHandler();
        var context = Context("20e");
        var step = CreateStep("/missing/sw407-0.2.13-05.09.2025.img", Hash, handler);

        Assert.Equal(StepResult.True, await step.ExecuteAsync(context, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task WrongHashStopsBeforeClear()
    {
        var (directory, path) = CreateImage();
        try
        {
            var handler = new RecordingHandler();
            var step = CreateStep(path, Hash, handler);
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
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            var delays = new List<TimeSpan>();
            var step = CreateStep(path, hash, handler,
                (ctx, _) =>
                {
                    ctx.SetVariable("Dut.firmvare_vers", "20d");
                    return Task.FromResult(StepResult.True);
                },
                (duration, _) => { delays.Add(duration); return Task.CompletedTask; });

            Assert.Equal(StepResult.True, await step.ExecuteAsync(context, CancellationToken.None));
            Assert.Equal(new[] { "GET /clear.shtml", "POST /mngt/update.shtml",
                "GET /mngt/update.shtml?Update=Update" }, handler.Requests);
            Assert.Equal(new double[] { 10, 10, 40 }, delays.Select(d => d.TotalSeconds));
            Assert.True(context.GetVariable<bool>("Firmware.Updated"));
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
        var path = Path.Combine(directory, "sw407-0.2.13-05.09.2025.img");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        return (directory, path);
    }

    private static UpdatePswFirmwareStep CreateStep(string path, string hash,
        RecordingHandler handler,
        Func<TestContext, CancellationToken, Task<StepResult>>? refresh = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        new(new UnusedHttp(), NullLogger.Instance, "http://192.168.0.1", path,
            "0.2.13", "Dut.firmvare_vers", hash, () => new HttpClient(handler), refresh, delay);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri?.PathAndQuery}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class UnusedHttp : IHttpRequestService
    {
        public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout,
            CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}
