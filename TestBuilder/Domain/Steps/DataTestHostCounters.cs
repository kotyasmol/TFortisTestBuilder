using System;
using System.Net.NetworkInformation;

namespace TestBuilder.Domain.Steps;

internal readonly record struct DataTestHostCounters(bool Available, long RxDrops, long RxErrors, long TxDrops, long TxErrors)
{
    internal static DataTestHostCounters Read(NetworkInterface adapter)
    {
        if (OperatingSystem.IsMacOS()) return default;
        try
        {
            var s = adapter.GetIPStatistics();
            return new(true, s.IncomingPacketsDiscarded, s.IncomingPacketsWithErrors,
                s.OutgoingPacketsDiscarded, s.OutgoingPacketsWithErrors);
        }
        catch (Exception) { return default; }
    }

    internal DataTestHostCounters Delta(DataTestHostCounters before) => !Available || !before.Available ? default :
        new(true, Math.Max(0, RxDrops - before.RxDrops), Math.Max(0, RxErrors - before.RxErrors),
            Math.Max(0, TxDrops - before.TxDrops), Math.Max(0, TxErrors - before.TxErrors));

    internal bool HasErrors => Available && (RxDrops > 0 || RxErrors > 0 || TxDrops > 0 || TxErrors > 0);
    public override string ToString() => Available
        ? $"RX drops {RxDrops}, RX errors {RxErrors}, TX drops {TxDrops}, TX errors {TxErrors}" : "недоступны";
}
