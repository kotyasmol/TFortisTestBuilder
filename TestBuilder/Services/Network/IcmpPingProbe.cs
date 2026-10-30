using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Services.Network;

internal enum PingOutcome { Reply, NoReply, Error }
internal sealed record PingSample(PingOutcome Outcome, long? RoundtripMs = null, string Detail = "");
internal interface IPingProbe
{
    Task<PingSample> ProbeAsync(string host, int timeoutMs, CancellationToken token);
}

internal sealed class IcmpPingProbe : IPingProbe
{
    public async Task<PingSample> ProbeAsync(string host, int timeoutMs, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeoutMs);
        try
        {
            var address = IPAddress.TryParse(host, out var ip) ? ip :
                (await Dns.GetHostAddressesAsync(host, deadline.Token).ConfigureAwait(false)).FirstOrDefault();
            if (address == null) return new(PingOutcome.Error, Detail: "Адрес узла не найден.");
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, TimeSpan.FromMilliseconds(timeoutMs), cancellationToken: deadline.Token).ConfigureAwait(false);
            return reply.Status == IPStatus.Success
                ? new(PingOutcome.Reply, reply.RoundtripTime, address.ToString())
                : new(PingOutcome.NoReply, Detail: reply.Status.ToString());
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return new(PingOutcome.NoReply, Detail: "Истёк таймаут ответа."); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new(PingOutcome.Error, Detail: ex.GetBaseException().Message); }
    }
}
