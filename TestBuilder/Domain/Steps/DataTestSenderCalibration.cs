using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Domain.Steps;

internal static class DataTestSenderCalibration
{
    internal static async Task<int> SelectAsync(Func<int, CancellationToken, Task<PacedSendResult>> probe,
        int expectedPackets, int packetBytes, int targetMbps, double allowedDeficit,
        bool allowTwoSenders, Action<string> log, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var lanes in allowTwoSenders ? new[] { 1, 2 } : new[] { 1 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await probe(lanes, cancellationToken);
                var txMbps = result.ElapsedMs > 0
                    ? result.SentPackets * RunDataTestStep.CalculateWireSizeBytes(packetBytes) * 8.0 / result.ElapsedMs / 1000.0 : 0;
                var deficit = RunDataTestStep.CalculateTxDeficitPercent(targetMbps, txMbps);
                var countAccepted = result.SentPackets == expectedPackets ||
                    result.Engine == "SharpPcapSendPacket" && result.SentPackets > 0 && result.SentPackets < expectedPackets;
                if (countAccepted && deficit <= allowedDeficit)
                {
                    log($"Прогрев: отправителей {lanes}, TX {result.SentPackets}/{expectedPackets}, {txMbps:F3} Мбит/с, {result.Engine}.");
                    return lanes;
                }
                errors.Add($"отправителей {lanes}: TX {result.SentPackets}/{expectedPackets}, {txMbps:F3} Мбит/с, дефицит {deficit:F3}%");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"отправителей {lanes}: {ex.Message}");
            }
            log("Прогрев не подтверждён: " + errors[^1]);
        }
        throw new InvalidOperationException("Генератор не подтвердил заданную скорость на прогреве. Основное измерение не началось. " + string.Join("; ", errors));
    }
}
