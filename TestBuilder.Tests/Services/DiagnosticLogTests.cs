using TestBuilder.Services.Logging;

namespace TestBuilder.Tests.Services;

public class DiagnosticLogTests
{
    [Fact]
    public void WriteException_PreservesStackAndInnerExceptionWithoutStartingRun()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"diagnostic-log-{Guid.NewGuid():N}");
        try
        {
            Exception exception;
            try { throw new InvalidOperationException("Load failed", new IOException("Inner failure")); }
            catch (Exception ex) { exception = ex; }
            var path = DiagnosticLog.WriteException("Load profile", exception, directory);
            Assert.NotNull(path);
            var content = File.ReadAllText(path!);
            Assert.Contains("Load profile", content);
            Assert.Contains(exception.ToString(), content);
            Assert.Equal(path, DiagnosticLog.WriteException("Connect", exception, directory));
            Assert.Contains("Connect", File.ReadAllText(path!));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void WriteException_UnwritableDestinationDoesNotThrow()
    {
        var file = Path.GetTempFileName();
        try
        {
            Assert.Null(DiagnosticLog.WriteException("Connect", new IOException("Original failure"), file));
            Assert.Equal(string.Empty, File.ReadAllText(file));
        }
        finally { File.Delete(file); }
    }
}
