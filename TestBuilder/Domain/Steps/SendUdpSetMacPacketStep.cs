using System;
using System.Linq;
using System.Net;
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
        private readonly string _macVariableName;
        private readonly int _timeoutMs;
        private readonly int _repeatCount;
        private readonly int _delayBetweenRepeatsMs;
        private readonly bool _failOnSendError;

        public SendUdpSetMacPacketStep(
            ILogger logger,
            string targetIp,
            int targetPort,
            string macVariableName,
            int timeoutMs,
            int repeatCount,
            int delayBetweenRepeatsMs,
            bool failOnSendError)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _targetIp = string.IsNullOrWhiteSpace(targetIp) ? "192.168.0.1" : targetIp.Trim();
            _targetPort = targetPort <= 0 ? 43962 : targetPort;
            _macVariableName = string.IsNullOrWhiteSpace(macVariableName) ? "Dut.NewMac" : macVariableName.Trim();
            _timeoutMs = Math.Max(1, timeoutMs);
            _repeatCount = Math.Max(1, repeatCount);
            _delayBetweenRepeatsMs = Math.Max(0, delayBetweenRepeatsMs);
            _failOnSendError = failOnSendError;
        }

        public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
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

            try
            {
                using var client = new UdpClient();
                client.Client.SendTimeout = _timeoutMs;
                var endpoint = new IPEndPoint(IPAddress.Parse(_targetIp), _targetPort);

                for (var i = 0; i < _repeatCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await client.SendAsync(packet, packet.Length, endpoint);

                    if (i + 1 < _repeatCount && _delayBetweenRepeatsMs > 0)
                    {
                        await Task.Delay(_delayBetweenRepeatsMs, cancellationToken);
                    }
                }

                context.SetVariable("SetMac.PacketSent", true);
                context.SetVariable("SetMac.TargetIp", _targetIp);
                context.SetVariable("SetMac.TargetPort", _targetPort);
                context.SetVariable("SetMac.Mac", normalizedMac);
                context.SetVariable("SetMac.PacketHex", packetHex);
                context.SetVariable("SetMac.Error", string.Empty);
                _logger.Info($"[OK] UDP set MAC packet отправлен на {_targetIp}:{_targetPort}, MAC {normalizedMac}.");
                return StepResult.True;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Fail(context, ex.Message);
            }
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
            context.SetVariable("SetMac.PacketSent", false);
            context.SetVariable("SetMac.TargetIp", _targetIp);
            context.SetVariable("SetMac.TargetPort", _targetPort);
            context.SetVariable("SetMac.Error", error);
            _logger.Warning($"[ОШИБКА] UDP set MAC packet не отправлен: {error}");
            return _failOnSendError ? StepResult.False : StepResult.True;
        }
    }
}
