using TestBuilder.Domain.Steps;

namespace TestBuilder.Tests.StepTests;

public sealed class BundledBrowserTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TestBuilder browser " + Guid.NewGuid());

    [Fact]
    public void PortableBrowserIsPreferredAndSurvivesMovingTheApplication()
    {
        var original = Path.Combine(_directory, "original");
        var moved = Path.Combine(_directory, "moved");
        var relative = Path.Combine("Browser", "chrome-win64", "chrome.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(original, relative))!);
        File.WriteAllText(Path.Combine(original, relative), "browser fixture");
        var installed = Path.Combine(_directory, "installed.exe");
        File.WriteAllText(installed, "installed fixture");
        Directory.Move(original, moved);

        Assert.Equal(Path.Combine(moved, relative),
            SelfTestCheckStep.FindBrowserExecutable(moved, new[] { installed }));
    }

    [Fact]
    public void DeveloperBuildStillUsesInstalledBrowserAndReportsMissingBrowser()
    {
        Directory.CreateDirectory(_directory);
        var installed = Path.Combine(_directory, "edge.exe");
        File.WriteAllText(installed, "installed fixture");
        Assert.Equal(installed, SelfTestCheckStep.FindBrowserExecutable(_directory, new[] { installed }));
        File.Delete(installed);
        Assert.Null(SelfTestCheckStep.FindBrowserExecutable(_directory, new[] { installed }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
