using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public sealed class SendUdpSetMacPacketStep : ITestStep
    {
        private readonly ILogger _logger;
        private readonly string _targetIp;
        private readonly int _targetPort;
        private readonly int _localPort;
        private readonly string _localIp;
        private readonly string _macVariableName;
        private readonly int _timeoutMs;
        private readonly int _repeatCount;
        private readonly int _delayBetweenRepeatsMs;
        private readonly bool _failOnSendError;

        public SendUdpSetMacPacketStep(
            ILogger logger,
            string targetIp,
            int targetPort,
            int localPort,
            string macVariableName,
            int timeoutMs,
            int repeatCount,
            int delayBetweenRepeatsMs,
            bool failOnSendError,
            string localIp = "")
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _targetIp = string.IsNullOrWhiteSpace(targetIp) ? "192.168.0.1" : targetIp.Trim();
            _targetPort = targetPort <= 0 ? 43962 : targetPort;
            _localPort = localPort;
            _localIp = localIp?.Trim() ?? string.Empty;
            _macVariableName = string.IsNullOrWhiteSpace(macVariableName) ? "Dut.NewMac" : macVariableName.Trim();
            _timeoutMs = Math.Max(1, timeoutMs);
            _repeatCount = Math.Max(1, repeatCount);
            _delayBetweenRepeatsMs = Math.Max(0, delayBetweenRepeatsMs);
            _failOnSendError = failOnSendError;
        }

        public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            ResetResult(context);
            cancellationToken.ThrowIfCancellationRequested();

            if (!context.Variables.TryGetValue(_macVariableName, out var rawMac) ||
                rawMac == null)
            {
                return Fail(context, $"MAC-переменная '{_macVariableName}' не найдена.");
            }

            if (!TryParseMac(rawMac.ToString() ?? string.Empty, out var macBytes, out var normalizedMac))
            {
                return Fail(context, $"Некорректный MAC: '{rawMac}'.");
            }

            var packet = BuildPacket(macBytes);
            var packetHex = Convert.ToHexString(packet);
            context.SetVariable("SetMac.Mac", normalizedMac);
            context.SetVariable("SetMac.PacketHex", packetHex);

            try
            {
                if (!IPAddress.TryParse(_targetIp, out var targetAddress) ||
                    targetAddress.AddressFamily != AddressFamily.InterNetwork ||
                    targetAddress.Equals(IPAddress.Any) || targetAddress.Equals(IPAddress.Broadcast))
                {
                    return Fail(context, $"Target IP должен быть IPv4-адресом DUT: '{_targetIp}'.");
                }

                if (_targetPort is < 1 or > 65535 || _localPort is < 0 or > 65535)
                {
                    return Fail(context, "Target port должен быть 1..65535, Local port — 0..65535 (0 = автоматический).");
                }

                var endpoint = new IPEndPoint(targetAddress, _targetPort);
                var localAddress = ResolveLocalAddress(endpoint);
                using var client = new UdpClient(AddressFamily.InterNetwork);
                client.ExclusiveAddressUse = true;
                client.Client.Bind(new IPEndPoint(localAddress, _localPort));
                var localEndpoint = (IPEndPoint)client.Client.LocalEndPoint!;
                var interfaceName = FindInterfaceName(localAddress);
                context.SetVariable("SetMac.LocalIp", localAddress.ToString());
                context.SetVariable("SetMac.LocalPort", localEndpoint.Port);
                context.SetVariable("SetMac.Interface", interfaceName);

                _logger.Info(
                    $"[ШАГ] Запись MAC {normalizedMac}: UDP {localEndpoint} → {endpoint}, " +
                    $"интерфейс '{interfaceName}', пакет {packetHex}, " +
                    $"попыток={_repeatCount}, ожидание ответа mr={_timeoutMs} мс.");

                for (var attempt = 1; attempt <= _repeatCount; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    context.SetVariable("SetMac.Attempts", attempt);
                    using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    attemptCts.CancelAfter(_timeoutMs);

                    try
                    {
                        await client.SendAsync(packet.AsMemory(), endpoint, attemptCts.Token);
                        context.SetVariable("SetMac.PacketSent", true);
                        _logger.Info($"UDP set MAC: попытка {attempt}/{_repeatCount}, пакет передан сокету; ожидается ответ DUT.");

                        while (true)
                        {
                            var reply = await client.ReceiveAsync(attemptCts.Token);
                            var responseHex = Convert.ToHexString(reply.Buffer.AsSpan(0, Math.Min(reply.Buffer.Length, 256)));
                            context.SetVariable("SetMac.ResponseHex", responseHex);
                            context.SetVariable("SetMac.ResponseEndpoint", reply.RemoteEndPoint.ToString());

                            // QTstand_old/Network/network.cpp identifies the acknowledgement
                            // by "mr" at offsets 10..11. No MAC echo or response port is specified.
                            if (!reply.RemoteEndPoint.Address.Equals(targetAddress) ||
                                reply.Buffer.Length < 12 || reply.Buffer[10] != (byte)'m' || reply.Buffer[11] != (byte)'r')
                            {
                                _logger.Warning($"UDP set MAC: посторонний ответ от {reply.RemoteEndPoint}, hex={responseHex}; ожидание mr продолжается.");
                                continue;
                            }

                            context.SetVariable("SetMac.Acknowledged", true);
                            context.SetVariable("SetMac.Success", true);
                            _logger.Info(
                                $"[OK] DUT {reply.RemoteEndPoint} подтвердил команду MAC ответом mr, hex={responseHex}. " +
                                "Сохранение MAC проверяется selftest после перезапуска.");
                            return StepResult.True;
                        }
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        _logger.Warning($"UDP set MAC: нет подтверждения mr за {_timeoutMs} мс, попытка {attempt}/{_repeatCount}.");
                    }

                    if (attempt < _repeatCount && _delayBetweenRepeatsMs > 0)
                    {
                        await Task.Delay(_delayBetweenRepeatsMs, cancellationToken);
                    }
                }

                var acknowledgementError =
                    $"DUT не подтвердил запись MAC: нет ответа mr после {_repeatCount} попыток " +
                    $"по маршруту {localEndpoint} → {endpoint}. " +
                    "Проверьте Local IP сетевой карты стенда, доступность UDP-порта и поддержку команды прошивкой.";
                if (!context.GetVariable<bool>("SetMac.PacketSent"))
                {
                    return Fail(context, acknowledgementError);
                }

                // The automated legacy stand did not require mr before reboot/readback.
                // A lost acknowledgement must not prevent applying and checking the MAC.
                context.SetVariable("SetMac.Error", acknowledgementError);
                _logger.Warning(
                    $"{acknowledgementError} Отправка выполнена, запись пока не подтверждена. " +
                    "Продолжаем к перезапуску и обязательной проверке MAC через selftest.");
                return StepResult.True;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                return Fail(context, $"Локальный UDP-порт {_localPort} занят. Закройте другой экземпляр стенда/Qt MAC sender. {ex.Message}");
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressNotAvailable)
            {
                return Fail(context, $"Local IP '{_localIp}' не назначен доступной сетевой карте этого ПК. {ex.Message}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Fail(context, ex.Message);
            }
        }

        private IPAddress ResolveLocalAddress(IPEndPoint endpoint)
        {
            if (!string.IsNullOrWhiteSpace(_localIp))
            {
                if (!IPAddress.TryParse(_localIp, out var address) ||
                    address.AddressFamily != AddressFamily.InterNetwork ||
                    address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast))
                {
                    throw new InvalidOperationException($"Local IP должен быть IPv4-адресом сетевой карты стенда: '{_localIp}'.");
                }

                return address;
            }

            // UDP Connect chooses a route without sending a datagram to the DUT.
            using var routeProbe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            routeProbe.Connect(endpoint);
            return ((IPEndPoint)routeProbe.LocalEndPoint!).Address;
        }

        private static string FindInterfaceName(IPAddress address)
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(nic => nic.GetIPProperties().UnicastAddresses
                        .Any(unicast => unicast.Address.Equals(address)))?.Name ?? "не определён";
            }
            catch (NetworkInformationException)
            {
                return "не определён";
            }
        }

        private void ResetResult(TestContext context)
        {
            context.SetVariable("SetMac.PacketSent", false);
            context.SetVariable("SetMac.Acknowledged", false);
            context.SetVariable("SetMac.Success", false);
            context.SetVariable("SetMac.Attempts", 0);
            context.SetVariable("SetMac.TargetIp", _targetIp);
            context.SetVariable("SetMac.TargetPort", _targetPort);
            context.SetVariable("SetMac.LocalIp", _localIp);
            context.SetVariable("SetMac.LocalPort", _localPort);
            context.SetVariable("SetMac.Interface", string.Empty);
            context.SetVariable("SetMac.Mac", string.Empty);
            context.SetVariable("SetMac.PacketHex", string.Empty);
            context.SetVariable("SetMac.ResponseHex", string.Empty);
            context.SetVariable("SetMac.ResponseEndpoint", string.Empty);
            context.SetVariable("SetMac.Error", string.Empty);
        }

        public static byte[] BuildPacket(byte[] macBytes)
        {
            if (macBytes.Length != 6)
            {
                throw new ArgumentException("MAC должен содержать 6 байт.", nameof(macBytes));
            }

            var packet = new byte[21];
            Encoding.ASCII.GetBytes("CONFIG").CopyTo(packet, 0);
            Encoding.ASCII.GetBytes("mw").CopyTo(packet, 10);
            macBytes.CopyTo(packet, 12);
            Encoding.ASCII.GetBytes("Kr2").CopyTo(packet, 18);
            return packet;
        }

        public static bool TryParseMac(string value, out byte[] bytes, out string normalized)
        {
            var hex = new string(value.Where(Uri.IsHexDigit).ToArray());
            bytes = Array.Empty<byte>();
            normalized = string.Empty;

            if (hex.Length != 12)
            {
                return false;
            }

            bytes = Enumerable.Range(0, 6)
                .Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16))
                .ToArray();
            normalized = string.Join(":", bytes.Select(x => x.ToString("X2")));
            return true;
        }

        private StepResult Fail(TestContext context, string error)
        {
            context.SetVariable("SetMac.Acknowledged", false);
            context.SetVariable("SetMac.Success", false);
            context.SetVariable("SetMac.Error", error);
            _logger.Warning($"[ОШИБКА] Запись MAC не подтверждена: {error}");
            return _failOnSendError ? StepResult.False : StepResult.True;
        }
    }
}
