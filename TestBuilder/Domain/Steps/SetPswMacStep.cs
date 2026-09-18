using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps;

/// <summary>Legacy PSW CONFIG/mw command. Sending is not confirmation of persistence.</summary>
public sealed class SetPswMacStep : ITestStep
{
    private readonly ILogger _logger;
    private readonly string _destinationIp;
    private readonly string _localIp;
    private readonly int _localPort;
    private readonly int _udpPort;
    private readonly string _macVariableName;
    private readonly int _timeoutMs;
    private readonly bool _failOnError;
    private readonly Func<IPEndPoint, IPEndPoint, byte[], CancellationToken, Task<int>> _send;

    public SetPswMacStep(ILogger logger, string destinationIp, string localIp, int localPort,
        int udpPort, string macVariableName, int timeoutMs, bool failOnError)
        : this(logger, destinationIp, localIp, localPort, udpPort, macVariableName,
            timeoutMs, failOnError, SendAsync)
    { }

    internal SetPswMacStep(ILogger logger, string destinationIp, string localIp, int localPort,
        int udpPort, string macVariableName, int timeoutMs, bool failOnError,
        Func<IPEndPoint, IPEndPoint, byte[], CancellationToken, Task<int>> send)
    {
        _logger = logger;
        _destinationIp = destinationIp;
        _localIp = localIp;
        _localPort = localPort;
        _udpPort = udpPort;
        _macVariableName = macVariableName;
        _timeoutMs = Math.Max(1, timeoutMs);
        _failOnError = failOnError;
        _send = send;
    }

    public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
    {
        context.SetVariable("SetMac.Method", "PswUdp");
        context.SetVariable("SetMac.Sent", false);
        context.SetVariable("SetMac.Success", false);
        context.SetVariable("SetMac.Error", string.Empty);
        context.SetVariable("SetMac.Mac", string.Empty);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IPAddress.TryParse(_destinationIp, out var destination) ||
            destination.AddressFamily != AddressFamily.InterNetwork ||
            destination.Equals(IPAddress.Any) || destination.Equals(IPAddress.Broadcast) ||
            destination.GetAddressBytes()[0] >= 224 ||
            !IPAddress.TryParse(_localIp, out var local) || local.AddressFamily != AddressFamily.InterNetwork ||
            _localPort is < 1 or > 65535 || _udpPort is < 1 or > 65535)
            return Fail(context, "Укажите IPv4 DUT, локальный IPv4 стенда и порты 1..65535.");

        if (!context.Variables.TryGetValue(_macVariableName, out var rawMac) ||
            !SetProMacStep.TryNormalizeMac(rawMac?.ToString() ?? string.Empty, out var mac))
            return Fail(context, $"Некорректный MAC в переменной '{_macVariableName}'.");

        var macBytes = Convert.FromHexString(mac.Replace(":", string.Empty));
        if ((macBytes[0] & 1) != 0 || Array.TrueForAll(macBytes, b => b == 0))
            return Fail(context, "MAC должен быть ненулевым индивидуальным (unicast) адресом.");

        var packet = BuildPacket(macBytes);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeoutMs);
        try
        {
            var sent = await _send(new IPEndPoint(local, _localPort),
                new IPEndPoint(destination, _udpPort), packet, timeout.Token);
            if (sent != packet.Length)
                return Fail(context, $"Отправлено {sent} из {packet.Length} байт команды MAC.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return Fail(context, "Истёк таймаут отправки команды MAC."); }
        catch (Exception ex) { return Fail(context, $"Не удалось отправить команду MAC: {ex.Message}"); }

        context.SetVariable("SetMac.Mac", mac);
        context.SetVariable("SetMac.Sent", true);
        // Success describes this send operation, not nonvolatile MAC persistence.
        context.SetVariable("SetMac.Success", true);
        _logger.Info($"[ШАГ] UDP-команда записи MAC {mac} отправлена на {destination}:{_udpPort}. " +
            "Это не подтверждение записи: после перезапуска прочитайте и сравните MAC через selftest.");
        return StepResult.True;
    }

    internal static byte[] BuildPacket(byte[] mac)
    {
        if (mac.Length != 6) throw new ArgumentException("MAC должен содержать 6 байт.", nameof(mac));
        byte[] packet = [0x43, 0x4F, 0x4E, 0x46, 0x49, 0x47, 0, 0, 0, 0, 0x6D, 0x77,
            0, 0, 0, 0, 0, 0, 0x4B, 0x72, 0x32];
        mac.CopyTo(packet, 12);
        return packet;
    }

    private static async Task<int> SendAsync(IPEndPoint local, IPEndPoint destination,
        byte[] packet, CancellationToken cancellationToken)
    {
        using var client = new UdpClient(local);
        return await client.SendAsync(packet.AsMemory(), destination, cancellationToken);
    }

    private StepResult Fail(TestContext context, string error)
    {
        context.SetVariable("SetMac.Error", error);
        _logger.Warning($"[ОШИБКА] UDP MAC: {error}");
        return _failOnError ? StepResult.False : StepResult.True;
    }
}
