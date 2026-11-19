using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;

namespace TestBuilder.Domain.Steps;

internal static class DataTestQueuePrefill
{
    internal const int ReserveQueues = 4;

    // The measurement starts only after this bounded reserve is ready. A short
    // test can finish its producer before filling the reserve; do not wait forever.
    internal static Queue<T> Read<T>(ChannelReader<T> reader, int count, Action<T> dispose, CancellationToken token)
    {
        var ready = new Queue<T>();
        try
        {
            while (ready.Count < count && reader.WaitToReadAsync(token).AsTask().GetAwaiter().GetResult())
                if (reader.TryRead(out var item)) ready.Enqueue(item);
            return ready;
        }
        catch
        {
            while (ready.TryDequeue(out var item)) dispose(item);
            throw;
        }
    }
}
