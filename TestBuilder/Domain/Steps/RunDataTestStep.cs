using SharpPcap;
using SharpPcap.LibPcap;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public sealed class RunDataTestStep : ITestStep
    {
        private const int EthernetHeaderLength = 14;
        private const int IpHeaderLength = 20;
        private const int UdpHeaderLength = 8;
        private const int MinEthernetFrameLength = 64;

        private readonly ILogger _logger;
        private readonly string _mode;
        private readonly int _expectedPackets;
        private readonly int _packetSizeBytes;
        private readonly int _udpPort;
        private readonly int _maxPortTestTimeMs;
        private readonly IReadOnlyList<DataTestPortConfig> _ports;
        private readonly string _outputVariableName;
        private readonly bool _failOnError;

        public RunDataTestStep(
            ILogger logger,
            string mode,
            int expectedPackets,
            int packetSizeBytes,
            int udpPort,
            int maxPortTestTimeMs,
            IEnumerable<DataTestPortConfig> ports,
            string outputVariableName,
            bool failOnError)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mode = string.IsNullOrWhiteSpace(mode) ? "SoftwarePcap" : mode.Trim();
            _expectedPackets = Math.Max(1, expectedPackets);
            _packetSizeBytes = Math.Max(MinEthernetFrameLength, packetSizeBytes);
            _udpPort = udpPort <= 0 ? 43962 : udpPort;
            _maxPortTestTimeMs = Math.Max(1, maxPortTestTimeMs);
            _ports = ports.ToList();
            _outputVariableName = string.IsNullOrWhiteSpace(outputVariableName) ? "DataTest" : outputVariableName.Trim();
            _failOnError = failOnError;
        }

        public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            context.SetVariable($"{_outputVariableName}.Mode", _mode);
            context.SetVariable($"{_outputVariableName}.ExpectedPackets", _expectedPackets);
            context.SetVariable($"{_outputVariableName}.PacketSizeBytes", _packetSizeBytes);
            context.SetVariable($"{_outputVariableName}.UdpPort", _udpPort);
            context.SetVariable($"{_outputVariableName}.MaxPortTestTimeMs", _maxPortTestTimeMs);

            if (_ports.Count == 0)
            {
                return Finish(context, false, "Не настроены пары портов DataTest.");
            }

            if (!IsSoftwarePcapMode(_mode))
            {
                return Finish(context, false, $"Режим DataTest '{_mode}' не поддерживается этой нодой. Используйте SoftwarePcap.");
            }

            IReadOnlyList<LibPcapLiveDevice> devices;

            try
            {
                devices = LibPcapLiveDeviceList.Instance
                    .Where(device => !device.Loopback)
                    .ToList();
            }
            catch (Exception ex)
            {
                return Finish(
                    context,
                    false,
                    $"Не удалось получить список pcap-адаптеров. Проверьте, что установлен Npcap/WinPcap и есть права доступа. {ex.Message}");
            }

            if (devices.Count == 0)
            {
                return Finish(context, false, "Npcap/WinPcap не вернул ни одного сетевого адаптера.");
            }

            _logger.Info($"[ШАГ] DataTest: mode {_mode}, pairs {_ports.Count}, expected {_expectedPackets}, packet {_packetSizeBytes} bytes.");

            var allPassed = true;
            var errors = new List<string>();

            for (var i = 0; i < _ports.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var port = _ports[i];
                var result = await RunPortPairAsync(i, port, devices, cancellationToken);
                WritePortResult(context, i, port, result);

                if (!result.Passed)
                {
                    allPassed = false;
                    errors.Add($"{port.Name}: {result.Error}");
                }
            }

            return Finish(context, allPassed, string.Join("; ", errors));
        }

        private async Task<DataTestPortResult> RunPortPairAsync(
            int index,
            DataTestPortConfig port,
            IReadOnlyList<LibPcapLiveDevice> devices,
            CancellationToken cancellationToken)
        {
            if (!IPAddress.TryParse(port.InIp, out var inIp) || inIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return DataTestPortResult.Fail($"Некорректный InIp '{port.InIp}'.");
            }

            if (!IPAddress.TryParse(port.OutIp, out var outIp) || outIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return DataTestPortResult.Fail($"Некорректный OutIp '{port.OutIp}'.");
            }

            var receiveDevice = FindDeviceByIp(devices, inIp);
            if (receiveDevice == null)
            {
                return DataTestPortResult.Fail($"Не найдена pcap-сетевая карта с IP {inIp}.");
            }

            var sendDevice = FindDeviceByIp(devices, outIp);
            if (sendDevice == null)
            {
                return DataTestPortResult.Fail($"Не найдена pcap-сетевая карта с IP {outIp}.");
            }

            var receiveMac = GetMacBytes(receiveDevice.MacAddress);
            if (receiveMac == null)
            {
                return DataTestPortResult.Fail($"У карты {inIp} не найден MAC адрес.");
            }

            var sendMac = GetMacBytes(sendDevice.MacAddress);
            if (sendMac == null)
            {
                return DataTestPortResult.Fail($"У карты {outIp} не найден MAC адрес.");
            }

            var packet = BuildPacket(sendMac, receiveMac, outIp, inIp, _packetSizeBytes, _udpPort);
            var payloadBytes = Math.Max(0, packet.Length - EthernetHeaderLength - IpHeaderLength - UdpHeaderLength);
            var receivedPackets = 0;
            var transmittedPackets = 0;
            var firstPacketAt = Stopwatch.GetTimestamp();
            var lastPacketAt = firstPacketAt;
            var sawFirstPacket = false;

            void OnPacketArrival(object sender, PacketCapture capture)
            {
                var now = Stopwatch.GetTimestamp();

                if (!sawFirstPacket)
                {
                    firstPacketAt = now;
                    sawFirstPacket = true;
                }

                lastPacketAt = now;
                Interlocked.Increment(ref receivedPackets);
            }

            try
            {
                receiveDevice.Open(DeviceModes.Promiscuous | DeviceModes.MaxResponsiveness, 100);
                sendDevice.Open(DeviceModes.Promiscuous | DeviceModes.MaxResponsiveness, 100);

                receiveDevice.Filter = $"udp port {_udpPort} and ip dst host {inIp}";
                receiveDevice.OnPacketArrival += OnPacketArrival;
                receiveDevice.StartCapture();

                var deadline = Stopwatch.StartNew();

                while (Volatile.Read(ref receivedPackets) < _expectedPackets &&
                       deadline.ElapsedMilliseconds < _maxPortTestTimeMs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    sendDevice.SendPacket(packet);
                    transmittedPackets++;

                    if ((transmittedPackets & 0x3F) == 0)
                    {
                        await Task.Yield();
                    }
                }

                await Task.Delay(100, cancellationToken);

                var received = Volatile.Read(ref receivedPackets);
                var passed = received >= _expectedPackets;
                var elapsedMs = GetElapsedMilliseconds(sawFirstPacket ? firstPacketAt : Stopwatch.GetTimestamp(), lastPacketAt);
                var speedKbps = elapsedMs > 0 ? received * payloadBytes * 8.0 / elapsedMs : 0.0;
                var error = passed
                    ? string.Empty
                    : $"Принято {received} из {_expectedPackets} пакетов за {_maxPortTestTimeMs} мс.";

                _logger.Info(
                    $"DataTest {port.Name} [{index}]: {outIp} -> {inIp}, TX {transmittedPackets}, RX {received}, " +
                    $"speed {speedKbps:F1} Kbps, {(passed ? "OK" : "FAIL")}.");

                return new DataTestPortResult(
                    passed,
                    transmittedPackets,
                    received,
                    speedKbps,
                    receiveDevice.Name,
                    sendDevice.Name,
                    error);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return DataTestPortResult.Fail(ex.Message);
            }
            finally
            {
                receiveDevice.OnPacketArrival -= OnPacketArrival;
                SafeStopAndClose(receiveDevice);
                SafeStopAndClose(sendDevice);
            }
        }

        public static byte[] BuildPacket(
            byte[] sourceMac,
            byte[] destinationMac,
            IPAddress sourceIp,
            IPAddress destinationIp,
            int packetSizeBytes,
            int udpPort)
        {
            if (sourceMac.Length != 6)
            {
                throw new ArgumentException("Source MAC must contain 6 bytes.", nameof(sourceMac));
            }

            if (destinationMac.Length != 6)
            {
                throw new ArgumentException("Destination MAC must contain 6 bytes.", nameof(destinationMac));
            }

            var totalLength = Math.Max(MinEthernetFrameLength, packetSizeBytes);
            var ipTotalLength = totalLength - EthernetHeaderLength;
            var udpLength = ipTotalLength - IpHeaderLength;

            if (udpLength < UdpHeaderLength)
            {
                throw new ArgumentOutOfRangeException(nameof(packetSizeBytes), "Packet size is too small for Ethernet/IP/UDP.");
            }

            var packet = new byte[totalLength];

            destinationMac.CopyTo(packet, 0);
            sourceMac.CopyTo(packet, 6);
            packet[12] = 0x08;
            packet[13] = 0x00;

            var ipOffset = EthernetHeaderLength;
            packet[ipOffset] = 0x45;
            packet[ipOffset + 1] = 0x00;
            WriteUInt16(packet, ipOffset + 2, (ushort)ipTotalLength);
            WriteUInt16(packet, ipOffset + 4, 1234);
            WriteUInt16(packet, ipOffset + 6, 0);
            packet[ipOffset + 8] = 128;
            packet[ipOffset + 9] = 17;

            sourceIp.GetAddressBytes().CopyTo(packet, ipOffset + 12);
            destinationIp.GetAddressBytes().CopyTo(packet, ipOffset + 16);

            var ipChecksum = InternetChecksum(packet, ipOffset, IpHeaderLength);
            WriteUInt16(packet, ipOffset + 10, ipChecksum);

            var udpOffset = ipOffset + IpHeaderLength;
            WriteUInt16(packet, udpOffset, (ushort)udpPort);
            WriteUInt16(packet, udpOffset + 2, (ushort)udpPort);
            WriteUInt16(packet, udpOffset + 4, (ushort)udpLength);
            WriteUInt16(packet, udpOffset + 6, 0);

            for (var i = udpOffset + UdpHeaderLength; i < packet.Length; i++)
            {
                packet[i] = 0x41;
            }

            return packet;
        }

        private StepResult Finish(TestContext context, bool passed, string error)
        {
            context.SetVariable($"{_outputVariableName}.Passed", passed);
            context.SetVariable($"{_outputVariableName}.Error", error);

            if (passed)
            {
                _logger.Info("[OK] DataTest выполнен.");
                return StepResult.True;
            }

            _logger.Warning($"[ОШИБКА] DataTest не выполнен: {error}");
            return _failOnError ? StepResult.False : StepResult.True;
        }

        private void WritePortResult(
            TestContext context,
            int index,
            DataTestPortConfig port,
            DataTestPortResult result)
        {
            var prefix = $"{_outputVariableName}.Port{index}";
            context.SetVariable($"{prefix}.Name", port.Name);
            context.SetVariable($"{prefix}.InIp", port.InIp);
            context.SetVariable($"{prefix}.OutIp", port.OutIp);
            context.SetVariable($"{prefix}.Passed", result.Passed);
            context.SetVariable($"{prefix}.TransmittedPackets", result.TransmittedPackets);
            context.SetVariable($"{prefix}.ReceivedPackets", result.ReceivedPackets);
            context.SetVariable($"{prefix}.SpeedKbps", result.SpeedKbps);
            context.SetVariable($"{prefix}.ReceiveDevice", result.ReceiveDeviceName);
            context.SetVariable($"{prefix}.SendDevice", result.SendDeviceName);
            context.SetVariable($"{prefix}.Error", result.Error);
        }

        private static bool IsSoftwarePcapMode(string mode)
        {
            return mode.Equals("SoftwarePcap", StringComparison.OrdinalIgnoreCase) ||
                   mode.Equals("Pcap", StringComparison.OrdinalIgnoreCase) ||
                   mode.Equals("Software", StringComparison.OrdinalIgnoreCase) ||
                   mode.Equals("TYPE_SOFT_GEN", StringComparison.OrdinalIgnoreCase);
        }

        private static LibPcapLiveDevice? FindDeviceByIp(IEnumerable<LibPcapLiveDevice> devices, IPAddress ip)
        {
            var text = ip.ToString();

            return devices.FirstOrDefault(device =>
                device.Addresses.Any(address =>
                    address.Addr != null &&
                    IPAddress.TryParse(address.Addr.ToString(), out var addressIp) &&
                    addressIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    string.Equals(addressIp.ToString(), text, StringComparison.OrdinalIgnoreCase)));
        }

        private static byte[]? GetMacBytes(PhysicalAddress? address)
        {
            if (address == null)
            {
                return null;
            }

            var bytes = address.GetAddressBytes();
            return bytes.Length == 6 ? bytes : null;
        }

        private static void SafeStopAndClose(LibPcapLiveDevice device)
        {
            try
            {
                if (device.Started)
                {
                    device.StopCapture();
                }
            }
            catch
            {
            }

            try
            {
                device.Close();
            }
            catch
            {
            }
        }

        private static long GetElapsedMilliseconds(long startTimestamp, long stopTimestamp)
        {
            var ticks = Math.Max(0, stopTimestamp - startTimestamp);
            return Math.Max(1, ticks * 1000 / Stopwatch.Frequency);
        }

        private static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)(value >> 8);
            buffer[offset + 1] = (byte)(value & 0xFF);
        }

        private static ushort InternetChecksum(byte[] buffer, int offset, int length)
        {
            uint sum = 0;

            for (var i = 0; i < length; i += 2)
            {
                var word = (uint)(buffer[offset + i] << 8);

                if (i + 1 < length)
                {
                    word += buffer[offset + i + 1];
                }

                sum += word;
            }

            while ((sum >> 16) != 0)
            {
                sum = (sum & 0xFFFF) + (sum >> 16);
            }

            return (ushort)~sum;
        }
    }

    public sealed record DataTestPortConfig(string Name, string InIp, string OutIp);

    internal sealed record DataTestPortResult(
        bool Passed,
        int TransmittedPackets,
        int ReceivedPackets,
        double SpeedKbps,
        string ReceiveDeviceName,
        string SendDeviceName,
        string Error)
    {
        public static DataTestPortResult Fail(string error) => new(
            false,
            0,
            0,
            0.0,
            string.Empty,
            string.Empty,
            error);
    }
}
