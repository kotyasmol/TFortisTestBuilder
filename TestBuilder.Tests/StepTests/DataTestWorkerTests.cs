using TestBuilder.Domain.Steps;

namespace TestBuilder.Tests.StepTests;

public class DataTestWorkerTests
{
    [Fact]
    public async Task BlockingWorkRunsOutsideTheSharedThreadPool()
    {
        var result = await DataTestWorker.Run(() => Thread.CurrentThread.IsThreadPoolThread);
        Assert.False(result);
    }

    [Fact]
    public async Task NativeErrorIsReturnedToTheAwaitingCaller()
    {
        var task = DataTestWorker.Run<int>(() => throw new IOException("native failed"));
        var error = await Assert.ThrowsAsync<IOException>(() => task);
        Assert.Equal("native failed", error.Message);
    }
}
