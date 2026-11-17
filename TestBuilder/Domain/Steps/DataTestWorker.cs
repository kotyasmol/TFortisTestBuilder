using System;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Domain.Steps;

internal static class DataTestWorker
{
    // Capture, native transmission and queue production must not wait for
    // ThreadPool hill climbing while other native calls occupy pool workers.
    internal static Task<T> Run<T>(Func<T> work) => Task.Factory.StartNew(work,
        CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
}
