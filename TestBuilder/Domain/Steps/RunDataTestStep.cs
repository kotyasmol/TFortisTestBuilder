using SharpPcap;
using SharpPcap.LibPcap;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
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
        private const int EthernetFcsLength = 4;
        private const int EthernetPreambleAndSfdLength = 8;
        private const int EthernetInterPacketGapLength = 12;
        private const int ProbePayloadOffset = EthernetHeaderLength + IpHeaderLength + UdpHeaderLength;
        private const int ProbeIdentityLength = 16;
        private const int MaximumSupportedBandwidthMbps = 100;
        private const int MaxPacketsPerBurst = 512;
        private const int SendQueueChunkMs = 500;
        private const int CaptureStartSettleMs = 100;
        private const int WarmupDrainMs = 100;
        private const int ReceiveDrainQuietMs = 75;
        private const int ReceiveDrainMaxMs = 500;
        private const int InfrastructureRetryCount = 1;

        private static readonly byte[] ProbeMagic = { 0x54, 0x46, 0x44, 0x54 }; // "TFDT"

        private readonly ILogger _logger;
        private readonly string _mode;
        private readonly int _expectedPackets;
        private readonly int _packetSizeBytes;
        private readonly int _udpPort;
        private readonly int _maxPortTestTimeMs;
        private readonly int _targetBandwidthMbps;
        private readonly int _durationMs;
        private readonly int _warmupMs;
        private readonly int _interPairDelayMs;
        private readonly double _allowedLossPercent;
        private readonly double _allowedTxDeficitPercent;
        private readonly bool _bidirectional;
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
            int targetBandwidthMbps,
            int durationMs,
            int warmupMs,
            int interPairDelayMs,
            double allowedLossPercent,
            double allowedTxDeficitPercent,
            bool bidirectional,
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
            _targetBandwidthMbps = NormalizeBandwidth(targetBandwidthMbps);
            _durationMs = Math.Max(100, durationMs);
            _warmupMs = Math.Max(0, warmupMs);
            _interPairDelayMs = Math.Max(0, interPairDelayMs);
            _allowedLossPercent = Math.Clamp(allowedLossPercent, 0.0, 100.0);
            _allowedTxDeficitPercent = Math.Clamp(allowedTxDeficitPercent, 0.0, 100.0);
            _bidirectional = bidirectional;
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
            context.SetVariable($"{_outputVariableName}.TargetBandwidthMbps", _targetBandwidthMbps);
            context.SetVariable($"{_outputVariableName}.DurationMs", _durationMs);
            context.SetVariable($"{_outputVariableName}.WarmupMs", _warmupMs);
            context.SetVariable($"{_outputVariableName}.InterPairDelayMs", _interPairDelayMs);
            context.SetVariable($"{_outputVariableName}.AllowedLossPercent", _allowedLossPercent);
            context.SetVariable($"{_outputVariableName}.AllowedTxDeficitPercent", _allowedTxDeficitPercent);
            context.SetVariable($"{_outputVariableName}.Bidirectional", _bidirectional);
            context.SetVariable($"{_outputVariableName}.BandwidthLimitMbps", MaximumSupportedBandwidthMbps);

            if (_ports.Count == 0)
            {
                return Finish(context, false, "Не настроены пары портов DataTest.");
            }

            if (!IsSoftwarePcapMode(_mode))
            {
                return Finish(context, false, $"Режим DataTest '{_mode}' не поддерживается этой нодой. Используйте SoftwarePcap.");
            }

            IReadOnlyList<ILiveDevice> devices;

            try
            {
                devices = CaptureDeviceList.Instance.ToList();
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

            _logger.Info(
                $"[ШАГ] DataTest: mode {_mode}, pairs {_ports.Count} sequentially, target {_targetBandwidthMbps} Mbps, " +
                $"duration {_durationMs} ms, warmup {_warmupMs} ms, pause {_interPairDelayMs} ms, packet {_packetSizeBytes} bytes, " +
                $"directions {(_bidirectional ? "both" : "one")}, max loss {_allowedLossPercent:F3}%, " +
                $"max TX deficit {_allowedTxDeficitPercent:F3}%.");

            var allPassed = true;
            var errors = new List<string>();

            for (var i = 0; i < _ports.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var port = _ports[i];
                _logger.Info($"DataTest {port.Name} [{i + 1}/{_ports.Count}]: запуск отдельной пары.");

                // Queue preparation and pcap injection are CPU/native-driver work. Running
                // the pair on a worker keeps the Avalonia UI responsive during line-rate tests.
                var result = await Task.Run(
                    () => RunPortPairAsync(i, port, devices, cancellationToken),
                    cancellationToken);
                WritePortResult(context, i, port, result);

                if (!result.Passed)
                {
                    allPassed = false;
                    errors.Add($"{port.Name}: {result.Error}");
                }

                if (i < _ports.Count - 1 && _interPairDelayMs > 0)
                {
                    _logger.Info($"DataTest: пауза {_interPairDelayMs} мс перед следующей парой.");
                    await Task.Delay(_interPairDelayMs, cancellationToken);
                }
            }

            return Finish(context, allPassed, string.Join("; ", errors));
        }

        private async Task<DataTestPairResult> RunPortPairAsync(
            int index,
            DataTestPortConfig port,
            IReadOnlyList<ILiveDevice> devices,
            CancellationToken cancellationToken)
        {
            if (!IPAddress.TryParse(port.InIp, out var inIp) || inIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return DataTestPairResult.Fail($"Некорректный InIp '{port.InIp}'.");
            }

            if (!IPAddress.TryParse(port.OutIp, out var outIp) || outIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return DataTestPairResult.Fail($"Некорректный OutIp '{port.OutIp}'.");
            }

            var inNetworkInterface = FindNetworkInterfaceByIp(inIp);
            var outNetworkInterface = FindNetworkInterfaceByIp(outIp);
            var inDevice = FindDeviceByNetworkInterface(devices, inNetworkInterface);
            var outDevice = FindDeviceByNetworkInterface(devices, outNetworkInterface);

            if (inDevice == null || inNetworkInterface == null)
            {
                return DataTestPairResult.Fail($"Не найдена pcap-сетевая карта с IP {inIp}.");
            }

            if (outDevice == null || outNetworkInterface == null)
            {
                return DataTestPairResult.Fail($"Не найдена pcap-сетевая карта с IP {outIp}.");
            }

            var inMac = GetMacBytes(inDevice.MacAddress);
            if (inMac == null)
            {
                return DataTestPairResult.Fail($"У карты {inIp} не найден MAC адрес.");
            }

            var outMac = GetMacBytes(outDevice.MacAddress);
            if (outMac == null)
            {
                return DataTestPairResult.Fail($"У карты {outIp} не найден MAC адрес.");
            }

            var requestedBandwidthMbps = port.TargetBandwidthMbps ?? _targetBandwidthMbps;
            var targetBandwidthMbps = NormalizeBandwidth(requestedBandwidthMbps);

            if (requestedBandwidthMbps != targetBandwidthMbps)
            {
                _logger.Warning(
                    $"DataTest {port.Name}: target {requestedBandwidthMbps} Mbps ограничен до {targetBandwidthMbps} Mbps, " +
                    $"потому что текущая нода предназначена для 100-Мбит портов.");
            }

            var linkError = ValidateLinkSpeed(inNetworkInterface, inIp, targetBandwidthMbps) ??
                            ValidateLinkSpeed(outNetworkInterface, outIp, targetBandwidthMbps);
            if (!string.IsNullOrEmpty(linkError))
            {
                return DataTestPairResult.Fail(linkError);
            }

            var forward = await RunDirectionWithRetryAsync(
                index,
                port.Name,
                "Forward",
                outDevice,
                inDevice,
                outNetworkInterface,
                inNetworkInterface,
                outIp,
                inIp,
                outMac,
                inMac,
                targetBandwidthMbps,
                cancellationToken);

            DataTestPortResult? reverse = null;
            if (_bidirectional)
            {
                await Task.Delay(CaptureStartSettleMs, cancellationToken);
                reverse = await RunDirectionWithRetryAsync(
                    index,
                    port.Name,
                    "Reverse",
                    inDevice,
                    outDevice,
                    inNetworkInterface,
                    outNetworkInterface,
                    inIp,
                    outIp,
                    inMac,
                    outMac,
                    targetBandwidthMbps,
                    cancellationToken);
            }

            return DataTestPairResult.FromDirections(forward, reverse);
        }

        private async Task<DataTestPortResult> RunDirectionWithRetryAsync(
            int index,
            string pairName,
            string direction,
            ILiveDevice sendDevice,
            ILiveDevice receiveDevice,
            NetworkInterface sendNetworkInterface,
            NetworkInterface receiveNetworkInterface,
            IPAddress sourceIp,
            IPAddress destinationIp,
            byte[] sourceMac,
            byte[] destinationMac,
            int targetBandwidthMbps,
            CancellationToken cancellationToken)
        {
            DataTestPortResult? lastResult = null;

            for (var attempt = 0; attempt <= InfrastructureRetryCount; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_maxPortTestTimeMs);

                try
                {
                    lastResult = await RunDirectionAsync(
                        index,
                        pairName,
                        direction,
                        sendDevice,
                        receiveDevice,
                        sendNetworkInterface,
                        receiveNetworkInterface,
                        sourceIp,
                        destinationIp,
                        sourceMac,
                        destinationMac,
                        targetBandwidthMbps,
                        timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    lastResult = DataTestPortResult.Fail(
                        $"Таймаут направления {sourceIp} -> {destinationIp} после {_maxPortTestTimeMs} мс.",
                        infrastructureFailure: true);
                }

                lastResult = lastResult with { AttemptCount = attempt + 1 };
                if (lastResult.Passed || !lastResult.InfrastructureFailure || attempt >= InfrastructureRetryCount)
                {
                    return lastResult;
                }

                _logger.Warning(
                    $"DataTest {pairName} {direction}: инфраструктурная ошибка генератора/захвата, " +
                    $"повтор {attempt + 2}/{InfrastructureRetryCount + 1}. {lastResult.Error}");
                await Task.Delay(CaptureStartSettleMs, cancellationToken);
            }

            return lastResult ?? DataTestPortResult.Fail("Не удалось выполнить направление DataTest.");
        }

        private async Task<DataTestPortResult> RunDirectionAsync(
            int index,
            string pairName,
            string direction,
            ILiveDevice sendDevice,
            ILiveDevice receiveDevice,
            NetworkInterface sendNetworkInterface,
            NetworkInterface receiveNetworkInterface,
            IPAddress sourceIp,
            IPAddress destinationIp,
            byte[] sourceMac,
            byte[] destinationMac,
            int targetBandwidthMbps,
            CancellationToken cancellationToken)
        {
            var packet = BuildPacket(sourceMac, destinationMac, sourceIp, destinationIp, _packetSizeBytes, _udpPort);
            var expectedPackets = CalculateExpectedPackets(targetBandwidthMbps, packet.Length, _durationMs);
            var receivedMarks = new int[expectedPackets];
            var receivedPackets = 0;
            var duplicatePackets = 0;
            var unexpectedPackets = 0;
            var countingEnabled = 0;
            var runId = CreateRunId();

            void OnPacketArrival(object sender, PacketCapture capture)
            {
                if (Volatile.Read(ref countingEnabled) == 0)
                {
                    return;
                }

                if (!TryReadProbeSequence(capture.Data, runId, expectedPackets, out var sequence))
                {
                    Interlocked.Increment(ref unexpectedPackets);
                    return;
                }

                if (Interlocked.Exchange(ref receivedMarks[sequence], 1) == 0)
                {
                    Interlocked.Increment(ref receivedPackets);
                }
                else
                {
                    Interlocked.Increment(ref duplicatePackets);
                }
            }

            try
            {
                receiveDevice.Open(DeviceModes.Promiscuous | DeviceModes.MaxResponsiveness, 100);
                sendDevice.Open(DeviceModes.Promiscuous | DeviceModes.MaxResponsiveness, 100);

                receiveDevice.Filter = BuildCaptureFilter(sourceIp, destinationIp, sourceMac, destinationMac, _udpPort);
                receiveDevice.OnPacketArrival += OnPacketArrival;
                receiveDevice.StartCapture();
                await Task.Delay(CaptureStartSettleMs, cancellationToken);

                if (_warmupMs > 0)
                {
                    await SendPacedAsync(
                        sendDevice,
                        packet,
                        targetBandwidthMbps,
                        _warmupMs,
                        CreateRunId(),
                        cancellationToken);
                    await Task.Delay(WarmupDrainMs, cancellationToken);
                }

                var captureStatsBefore = ReadCaptureStatistics(receiveDevice);
                Interlocked.Exchange(ref receivedPackets, 0);
                Interlocked.Exchange(ref duplicatePackets, 0);
                Interlocked.Exchange(ref unexpectedPackets, 0);
                Volatile.Write(ref countingEnabled, 1);

                var sendResult = await SendPacedAsync(
                    sendDevice,
                    packet,
                    targetBandwidthMbps,
                    _durationMs,
                    runId,
                    cancellationToken);
                var transmittedPackets = sendResult.SentPackets;

                await WaitForCaptureDrainAsync(() => Volatile.Read(ref receivedPackets), cancellationToken);
                Volatile.Write(ref countingEnabled, 0);

                var received = Volatile.Read(ref receivedPackets);
                var captureStatsAfter = ReadCaptureStatistics(receiveDevice);
                var captureDroppedPackets = CounterDelta(captureStatsBefore.DroppedPackets, captureStatsAfter.DroppedPackets);
                var receivedForLoss = Math.Min(received, transmittedPackets);
                var lostPackets = Math.Max(0, transmittedPackets - receivedForLoss);
                var lossPercent = transmittedPackets > 0
                    ? lostPackets * 100.0 / transmittedPackets
                    : 100.0;
                var wireSizeBytes = CalculateWireSizeBytes(packet.Length);
                var txMbps = sendResult.ElapsedMs > 0
                    ? transmittedPackets * wireSizeBytes * 8.0 / sendResult.ElapsedMs / 1000.0
                    : 0.0;
                var rxMbps = sendResult.ElapsedMs > 0
                    ? receivedForLoss * wireSizeBytes * 8.0 / sendResult.ElapsedMs / 1000.0
                    : 0.0;
                var txDeficitPercent = CalculateTxDeficitPercent(targetBandwidthMbps, txMbps);
                var infrastructureFailure = txDeficitPercent > _allowedTxDeficitPercent || captureDroppedPackets > 0;
                var passed = lossPercent <= _allowedLossPercent && !infrastructureFailure;
                var error = BuildFailureMessage(
                    passed,
                    lossPercent,
                    txDeficitPercent,
                    received,
                    transmittedPackets,
                    expectedPackets,
                    captureDroppedPackets,
                    sendResult.Engine);

                _logger.Info(
                    $"DataTest {pairName} [{index}] {direction}: {sourceIp} -> {destinationIp}, target {targetBandwidthMbps} Mbps, " +
                    $"expected {expectedPackets}, TX {transmittedPackets}, RX {received}, loss {lossPercent:F3}%, " +
                    $"tx deficit {txDeficitPercent:F3}%, tx speed {txMbps:F3} Mbps, rx speed {rxMbps:F3} Mbps, " +
                    $"capture drops {captureDroppedPackets}, duplicates {duplicatePackets}, unexpected {unexpectedPackets}, " +
                    $"engine {sendResult.Engine}, " +
                    $"{(passed ? "OK" : "FAIL")}.");

                return new DataTestPortResult(
                    passed,
                    transmittedPackets,
                    received,
                    rxMbps * 1000.0,
                    txMbps,
                    rxMbps,
                    targetBandwidthMbps,
                    _durationMs,
                    expectedPackets,
                    lossPercent,
                    txDeficitPercent,
                    receiveDevice.Name,
                    sendDevice.Name,
                    NetworkSpeedMbps(receiveNetworkInterface),
                    NetworkSpeedMbps(sendNetworkInterface),
                    captureDroppedPackets,
                    duplicatePackets,
                    unexpectedPackets,
                    sendResult.Engine,
                    infrastructureFailure,
                    1,
                    error);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return DataTestPortResult.Fail(ex.Message, infrastructureFailure: true);
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

        public static int CalculateExpectedPackets(int targetBandwidthMbps, int packetSizeBytes, int durationMs)
        {
            var packets = CalculatePacketsPerSecond(targetBandwidthMbps, packetSizeBytes) * Math.Max(1, durationMs) / 1000.0;
            return Math.Max(1, (int)Math.Round(Math.Min(int.MaxValue, packets), MidpointRounding.AwayFromZero));
        }

        public static int CalculateWireSizeBytes(int packetSizeBytes)
        {
            return Math.Max(MinEthernetFrameLength, packetSizeBytes) +
                   EthernetFcsLength +
                   EthernetPreambleAndSfdLength +
                   EthernetInterPacketGapLength;
        }

        public static double CalculateTxDeficitPercent(double targetBandwidthMbps, double actualTxMbps)
        {
            if (targetBandwidthMbps <= 0)
            {
                return 100.0;
            }

            return Math.Clamp((targetBandwidthMbps - actualTxMbps) * 100.0 / targetBandwidthMbps, 0.0, 100.0);
        }

        private static double CalculatePacketsPerSecond(int targetBandwidthMbps, int packetSizeBytes)
        {
            return Math.Max(1, targetBandwidthMbps) * 1_000_000.0 / (CalculateWireSizeBytes(packetSizeBytes) * 8.0);
        }

        private static async Task<PacedSendResult> SendPacedAsync(
            IInjectionDevice device,
            byte[] packet,
            int targetBandwidthMbps,
            int durationMs,
            ulong runId,
            CancellationToken cancellationToken)
        {
            if (device is PcapDevice pcapDevice && IsNativeSendQueueAvailable())
            {
                return SendWithNpcapQueue(
                    pcapDevice,
                    packet,
                    targetBandwidthMbps,
                    durationMs,
                    runId,
                    cancellationToken);
            }

            return await SendIndividuallyAsync(
                device,
                packet,
                targetBandwidthMbps,
                durationMs,
                runId,
                cancellationToken);
        }

        private static PacedSendResult SendWithNpcapQueue(
            PcapDevice device,
            byte[] packet,
            int targetBandwidthMbps,
            int durationMs,
            ulong runId,
            CancellationToken cancellationToken)
        {
            var targetPackets = CalculateExpectedPackets(targetBandwidthMbps, packet.Length, durationMs);
            var packetsPerSecond = CalculatePacketsPerSecond(targetBandwidthMbps, packet.Length);
            var queues = new List<SendQueuePlan>();

            try
            {
                for (var chunkStartMs = 0; chunkStartMs < durationMs; chunkStartMs += SendQueueChunkMs)
                {
                    var chunkEndMs = Math.Min(durationMs, chunkStartMs + SendQueueChunkMs);
                    var startSequence = Math.Min(
                        targetPackets,
                        (int)Math.Round(packetsPerSecond * chunkStartMs / 1000.0, MidpointRounding.AwayFromZero));
                    var endSequence = chunkEndMs >= durationMs
                        ? targetPackets
                        : Math.Min(
                            targetPackets,
                            (int)Math.Round(packetsPerSecond * chunkEndMs / 1000.0, MidpointRounding.AwayFromZero));
                    var packetCount = Math.Max(0, endSequence - startSequence);

                    if (packetCount == 0)
                    {
                        continue;
                    }

                    var queueCapacity = checked(packetCount * (packet.Length + 64));
                    var queue = new SendQueue(queueCapacity);

                    for (var sequence = startSequence; sequence < endSequence; sequence++)
                    {
                        WriteProbeIdentity(packet, runId, sequence);
                        var relativeMicroseconds = (int)Math.Round(
                            (sequence - startSequence) * 1_000_000.0 / packetsPerSecond,
                            MidpointRounding.AwayFromZero);

                        if (!queue.Add(
                                packet,
                                relativeMicroseconds / 1_000_000,
                                relativeMicroseconds % 1_000_000))
                        {
                            queue.Dispose();
                            throw new InvalidOperationException("Не удалось подготовить очередь Npcap для DataTest.");
                        }
                    }

                    queues.Add(new SendQueuePlan(queue, packetCount));
                }

                var startTimestamp = Stopwatch.GetTimestamp();
                var sent = 0;

                foreach (var plan in queues)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var transmittedBytes = plan.Queue.Transmit(device, SendQueueTransmitModes.Synchronized);
                    var entrySize = plan.Queue.CurrentLength / plan.PacketCount;
                    var sentInQueue = transmittedBytes >= plan.Queue.CurrentLength
                        ? plan.PacketCount
                        : Math.Clamp(transmittedBytes / Math.Max(1, entrySize), 0, plan.PacketCount);
                    sent += sentInQueue;

                    if (sentInQueue < plan.PacketCount)
                    {
                        break;
                    }
                }

                var elapsedMs = GetElapsedMilliseconds(startTimestamp, Stopwatch.GetTimestamp());
                return new PacedSendResult(sent, elapsedMs, "NpcapSendQueue");
            }
            finally
            {
                foreach (var plan in queues)
                {
                    plan.Queue.Dispose();
                }
            }
        }

        private static async Task<PacedSendResult> SendIndividuallyAsync(
            IInjectionDevice device,
            byte[] packet,
            int targetBandwidthMbps,
            int durationMs,
            ulong runId,
            CancellationToken cancellationToken)
        {
            var targetPackets = CalculateExpectedPackets(targetBandwidthMbps, packet.Length, durationMs);
            var packetsPerSecond = CalculatePacketsPerSecond(targetBandwidthMbps, packet.Length);
            var startTimestamp = Stopwatch.GetTimestamp();
            var deadlineTimestamp = startTimestamp + Math.Max(1, durationMs) * Stopwatch.Frequency / 1000;
            var sent = 0;

            while (sent < targetPackets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var now = Stopwatch.GetTimestamp();

                if (now >= deadlineTimestamp)
                {
                    break;
                }

                var elapsedTicks = Math.Max(0, now - startTimestamp);
                var packetsDue = Math.Min(
                    targetPackets,
                    Math.Max(1, (int)Math.Floor(elapsedTicks * packetsPerSecond / Stopwatch.Frequency) + 1));

                var burstLimit = Math.Min(packetsDue, sent + MaxPacketsPerBurst);

                while (sent < burstLimit)
                {
                    WriteProbeIdentity(packet, runId, sent);
                    device.SendPacket(packet, packet.Length);
                    sent++;
                }

                if (sent < packetsDue)
                {
                    // Do not monopolize a CPU core while catching up after a slow pcap call.
                    await Task.Yield();
                    continue;
                }

                if (sent >= targetPackets)
                {
                    break;
                }

                var nextDueTimestamp = startTimestamp + (long)(sent * Stopwatch.Frequency / packetsPerSecond);
                var waitTicks = Math.Min(nextDueTimestamp, deadlineTimestamp) - Stopwatch.GetTimestamp();

                if (waitTicks > Stopwatch.Frequency / 500)
                {
                    await Task.Delay(1, cancellationToken);
                }
                else if (waitTicks > 0)
                {
                    Thread.SpinWait(64);
                }
                else
                {
                    await Task.Yield();
                }
            }

            var elapsedMs = GetElapsedMilliseconds(startTimestamp, Stopwatch.GetTimestamp());
            return new PacedSendResult(sent, elapsedMs, "SharpPcapSendPacket");
        }

        internal static void WriteProbeIdentity(byte[] packet, ulong runId, int sequence)
        {
            if (packet.Length < ProbePayloadOffset + ProbeIdentityLength)
            {
                throw new ArgumentException("Packet is too small for the DataTest identity.", nameof(packet));
            }

            ProbeMagic.CopyTo(packet, ProbePayloadOffset);
            WriteUInt64(packet, ProbePayloadOffset + ProbeMagic.Length, runId);
            WriteUInt32(packet, ProbePayloadOffset + ProbeMagic.Length + sizeof(ulong), (uint)sequence);
        }

        internal static bool TryReadProbeSequence(
            ReadOnlySpan<byte> packet,
            ulong expectedRunId,
            int expectedPackets,
            out int sequence)
        {
            sequence = -1;

            if (packet.Length < ProbePayloadOffset + ProbeIdentityLength ||
                !packet.Slice(ProbePayloadOffset, ProbeMagic.Length).SequenceEqual(ProbeMagic))
            {
                return false;
            }

            var runId = ReadUInt64(packet, ProbePayloadOffset + ProbeMagic.Length);
            var rawSequence = ReadUInt32(packet, ProbePayloadOffset + ProbeMagic.Length + sizeof(ulong));

            if (runId != expectedRunId || rawSequence >= expectedPackets)
            {
                return false;
            }

            sequence = (int)rawSequence;
            return true;
        }

        private static bool IsNativeSendQueueAvailable()
        {
            try
            {
                return SendQueue.IsHardwareAccelerated;
            }
            catch
            {
                return false;
            }
        }

        private static ulong CreateRunId()
        {
            Span<byte> bytes = stackalloc byte[sizeof(ulong)];
            RandomNumberGenerator.Fill(bytes);
            return BitConverter.ToUInt64(bytes);
        }

        private static string BuildCaptureFilter(
            IPAddress sourceIp,
            IPAddress destinationIp,
            byte[] sourceMac,
            byte[] destinationMac,
            int udpPort)
        {
            return $"ether src {FormatMac(sourceMac)} and ether dst {FormatMac(destinationMac)} " +
                   $"and udp src port {udpPort} and udp dst port {udpPort} " +
                   $"and src host {sourceIp} and dst host {destinationIp}";
        }

        private static string FormatMac(byte[] mac)
        {
            return string.Join(":", mac.Select(value => value.ToString("x2")));
        }

        private static async Task WaitForCaptureDrainAsync(Func<int> readCount, CancellationToken cancellationToken)
        {
            var startedAt = Stopwatch.GetTimestamp();
            var quietStartedAt = startedAt;
            var lastCount = readCount();

            while (GetElapsedMilliseconds(startedAt, Stopwatch.GetTimestamp()) < ReceiveDrainMaxMs)
            {
                await Task.Delay(25, cancellationToken);
                var currentCount = readCount();

                if (currentCount != lastCount)
                {
                    lastCount = currentCount;
                    quietStartedAt = Stopwatch.GetTimestamp();
                    continue;
                }

                if (GetElapsedMilliseconds(quietStartedAt, Stopwatch.GetTimestamp()) >= ReceiveDrainQuietMs)
                {
                    break;
                }
            }
        }

        private static CaptureStatisticsSnapshot ReadCaptureStatistics(ILiveDevice device)
        {
            try
            {
                var statistics = device.Statistics;
                if (statistics == null)
                {
                    return default;
                }

                return new CaptureStatisticsSnapshot(statistics.ReceivedPackets, statistics.DroppedPackets);
            }
            catch
            {
                return default;
            }
        }

        private static uint CounterDelta(uint before, uint after)
        {
            return after >= before ? after - before : uint.MaxValue - before + after + 1;
        }

        private string BuildFailureMessage(
            bool passed,
            double lossPercent,
            double txDeficitPercent,
            int receivedPackets,
            int transmittedPackets,
            int expectedPackets,
            uint captureDroppedPackets,
            string sendEngine)
        {
            if (passed)
            {
                return string.Empty;
            }

            var parts = new List<string>();

            if (txDeficitPercent > _allowedTxDeficitPercent)
            {
                parts.Add(
                    $"TX deficit {txDeficitPercent:F3}% is greater than allowed {_allowedTxDeficitPercent:F3}%: " +
                    $"generator {sendEngine}, sent {transmittedPackets} of expected {expectedPackets} packets");
            }

            if (lossPercent > _allowedLossPercent)
            {
                parts.Add($"Loss {lossPercent:F3}% is greater than allowed {_allowedLossPercent:F3}%: RX {receivedPackets}, TX {transmittedPackets}");
            }

            if (captureDroppedPackets > 0)
            {
                parts.Add($"PC/Npcap capture dropped {captureDroppedPackets} packets; DUT result is inconclusive");
            }

            return string.Join("; ", parts);
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
            DataTestPairResult result)
        {
            var prefix = $"{_outputVariableName}.Port{index}";
            context.SetVariable($"{prefix}.Name", port.Name);
            context.SetVariable($"{prefix}.InIp", port.InIp);
            context.SetVariable($"{prefix}.OutIp", port.OutIp);
            context.SetVariable($"{prefix}.Passed", result.Passed);
            context.SetVariable($"{prefix}.TransmittedPackets", result.TransmittedPackets);
            context.SetVariable($"{prefix}.ReceivedPackets", result.ReceivedPackets);
            context.SetVariable($"{prefix}.SpeedKbps", result.SpeedKbps);
            context.SetVariable($"{prefix}.ActualTxMbps", result.ActualTxMbps);
            context.SetVariable($"{prefix}.ActualRxMbps", result.ActualRxMbps);
            context.SetVariable($"{prefix}.TargetBandwidthMbps", result.TargetBandwidthMbps);
            context.SetVariable($"{prefix}.DurationMs", result.DurationMs);
            context.SetVariable($"{prefix}.ExpectedPackets", result.ExpectedPackets);
            context.SetVariable($"{prefix}.LossPercent", result.LossPercent);
            context.SetVariable($"{prefix}.TxDeficitPercent", result.TxDeficitPercent);
            context.SetVariable($"{prefix}.ReceiveDevice", result.ReceiveDeviceName);
            context.SetVariable($"{prefix}.SendDevice", result.SendDeviceName);
            context.SetVariable($"{prefix}.CaptureDroppedPackets", result.CaptureDroppedPackets);
            context.SetVariable($"{prefix}.AttemptCount", result.AttemptCount);
            context.SetVariable($"{prefix}.DirectionsTested", result.Reverse == null ? 1 : 2);
            context.SetVariable($"{prefix}.Error", result.Error);

            WriteDirectionResult(context, $"{prefix}.Forward", result.Forward);
            if (result.Reverse != null)
            {
                WriteDirectionResult(context, $"{prefix}.Reverse", result.Reverse);
            }
        }

        private static void WriteDirectionResult(TestContext context, string prefix, DataTestPortResult result)
        {
            context.SetVariable($"{prefix}.Passed", result.Passed);
            context.SetVariable($"{prefix}.TransmittedPackets", result.TransmittedPackets);
            context.SetVariable($"{prefix}.ReceivedPackets", result.ReceivedPackets);
            context.SetVariable($"{prefix}.ExpectedPackets", result.ExpectedPackets);
            context.SetVariable($"{prefix}.TargetBandwidthMbps", result.TargetBandwidthMbps);
            context.SetVariable($"{prefix}.ActualTxMbps", result.ActualTxMbps);
            context.SetVariable($"{prefix}.ActualRxMbps", result.ActualRxMbps);
            context.SetVariable($"{prefix}.LossPercent", result.LossPercent);
            context.SetVariable($"{prefix}.TxDeficitPercent", result.TxDeficitPercent);
            context.SetVariable($"{prefix}.ReceiveLinkSpeedMbps", result.ReceiveLinkSpeedMbps);
            context.SetVariable($"{prefix}.SendLinkSpeedMbps", result.SendLinkSpeedMbps);
            context.SetVariable($"{prefix}.CaptureDroppedPackets", result.CaptureDroppedPackets);
            context.SetVariable($"{prefix}.DuplicatePackets", result.DuplicatePackets);
            context.SetVariable($"{prefix}.UnexpectedPackets", result.UnexpectedPackets);
            context.SetVariable($"{prefix}.SendEngine", result.SendEngine);
            context.SetVariable($"{prefix}.AttemptCount", result.AttemptCount);
            context.SetVariable($"{prefix}.Error", result.Error);
        }

        private static bool IsSoftwarePcapMode(string mode)
        {
            return mode.Equals("SoftwarePcap", StringComparison.OrdinalIgnoreCase) ||
                   mode.Equals("Pcap", StringComparison.OrdinalIgnoreCase) ||
                   mode.Equals("Software", StringComparison.OrdinalIgnoreCase) ||
                   mode.Equals("TYPE_SOFT_GEN", StringComparison.OrdinalIgnoreCase);
        }

        public static int NormalizeBandwidth(int bandwidthMbps)
        {
            return Math.Clamp(bandwidthMbps, 1, MaximumSupportedBandwidthMbps);
        }

        private static string? ValidateLinkSpeed(
            NetworkInterface networkInterface,
            IPAddress ip,
            int targetBandwidthMbps)
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                return $"Сетевая карта {ip} не поднята (status {networkInterface.OperationalStatus}).";
            }

            var speedMbps = NetworkSpeedMbps(networkInterface);
            if (speedMbps > 0 && speedMbps + 0.001 < targetBandwidthMbps)
            {
                return $"Сетевая карта {ip} согласовала только {speedMbps:F0} Mbps, target {targetBandwidthMbps} Mbps.";
            }

            return null;
        }

        private static double NetworkSpeedMbps(NetworkInterface networkInterface)
        {
            try
            {
                return networkInterface.Speed > 0 ? networkInterface.Speed / 1_000_000.0 : 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private static ILiveDevice? FindDeviceByNetworkInterface(
            IEnumerable<ILiveDevice> devices,
            NetworkInterface? networkInterface)
        {
            if (networkInterface == null)
            {
                return null;
            }

            var expectedMac = networkInterface.GetPhysicalAddress();
            return devices.FirstOrDefault(device => SamePhysicalAddress(device.MacAddress, expectedMac));
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

        private static NetworkInterface? FindNetworkInterfaceByIp(IPAddress ip)
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(networkInterface => networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .FirstOrDefault(networkInterface =>
                {
                    try
                    {
                        return networkInterface.GetIPProperties()
                            .UnicastAddresses
                            .Any(address => address.Address.Equals(ip));
                    }
                    catch
                    {
                        return false;
                    }
                });
        }

        private static bool SamePhysicalAddress(PhysicalAddress? left, PhysicalAddress? right)
        {
            var leftBytes = left?.GetAddressBytes();
            var rightBytes = right?.GetAddressBytes();

            return leftBytes is { Length: 6 } &&
                   rightBytes is { Length: 6 } &&
                   leftBytes.SequenceEqual(rightBytes);
        }

        private static void SafeStopAndClose(ILiveDevice device)
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

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static void WriteUInt64(byte[] buffer, int offset, ulong value)
        {
            WriteUInt32(buffer, offset, (uint)(value >> 32));
            WriteUInt32(buffer, offset + sizeof(uint), (uint)value);
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> buffer, int offset)
        {
            return ((uint)buffer[offset] << 24) |
                   ((uint)buffer[offset + 1] << 16) |
                   ((uint)buffer[offset + 2] << 8) |
                   buffer[offset + 3];
        }

        private static ulong ReadUInt64(ReadOnlySpan<byte> buffer, int offset)
        {
            return ((ulong)ReadUInt32(buffer, offset) << 32) |
                   ReadUInt32(buffer, offset + sizeof(uint));
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

    public sealed record DataTestPortConfig(string Name, string InIp, string OutIp, int? TargetBandwidthMbps = null);

    internal sealed record SendQueuePlan(SendQueue Queue, int PacketCount);

    internal readonly record struct CaptureStatisticsSnapshot(uint ReceivedPackets, uint DroppedPackets);

    internal sealed record PacedSendResult(int SentPackets, long ElapsedMs, string Engine);

    internal sealed record DataTestPortResult(
        bool Passed,
        int TransmittedPackets,
        int ReceivedPackets,
        double SpeedKbps,
        double ActualTxMbps,
        double ActualRxMbps,
        int TargetBandwidthMbps,
        int DurationMs,
        int ExpectedPackets,
        double LossPercent,
        double TxDeficitPercent,
        string ReceiveDeviceName,
        string SendDeviceName,
        double ReceiveLinkSpeedMbps,
        double SendLinkSpeedMbps,
        uint CaptureDroppedPackets,
        int DuplicatePackets,
        int UnexpectedPackets,
        string SendEngine,
        bool InfrastructureFailure,
        int AttemptCount,
        string Error)
    {
        public static DataTestPortResult Fail(string error, bool infrastructureFailure = false) => new(
            false,
            0,
            0,
            0.0,
            0.0,
            0.0,
            0,
            0,
            0,
            100.0,
            100.0,
            string.Empty,
            string.Empty,
            0.0,
            0.0,
            0,
            0,
            0,
            string.Empty,
            infrastructureFailure,
            1,
            error);
    }

    internal sealed record DataTestPairResult(
        bool Passed,
        int TransmittedPackets,
        int ReceivedPackets,
        double SpeedKbps,
        double ActualTxMbps,
        double ActualRxMbps,
        int TargetBandwidthMbps,
        int DurationMs,
        int ExpectedPackets,
        double LossPercent,
        double TxDeficitPercent,
        string ReceiveDeviceName,
        string SendDeviceName,
        uint CaptureDroppedPackets,
        int AttemptCount,
        string Error,
        DataTestPortResult Forward,
        DataTestPortResult? Reverse)
    {
        public static DataTestPairResult FromDirections(DataTestPortResult forward, DataTestPortResult? reverse)
        {
            var directions = reverse == null ? new[] { forward } : new[] { forward, reverse };
            var errors = new List<string>();

            if (!forward.Passed)
            {
                errors.Add($"Forward: {forward.Error}");
            }

            if (reverse is { Passed: false })
            {
                errors.Add($"Reverse: {reverse.Error}");
            }

            return new DataTestPairResult(
                directions.All(result => result.Passed),
                directions.Sum(result => result.TransmittedPackets),
                directions.Sum(result => result.ReceivedPackets),
                directions.Min(result => result.SpeedKbps),
                directions.Min(result => result.ActualTxMbps),
                directions.Min(result => result.ActualRxMbps),
                directions.Max(result => result.TargetBandwidthMbps),
                directions.Sum(result => result.DurationMs),
                directions.Sum(result => result.ExpectedPackets),
                directions.Max(result => result.LossPercent),
                directions.Max(result => result.TxDeficitPercent),
                forward.ReceiveDeviceName,
                forward.SendDeviceName,
                directions.Aggregate(0u, (total, result) => total + result.CaptureDroppedPackets),
                directions.Max(result => result.AttemptCount),
                string.Join("; ", errors),
                forward,
                reverse);
        }

        public static DataTestPairResult Fail(string error)
        {
            return FromDirections(DataTestPortResult.Fail(error), null);
        }
    }
}
