using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Domain.Steps;

internal static class DataTestSenderCalibration
{
    internal static async Task<int> SelectAsync(Func<int, CancellationToken, Task<PacedSendResult>> probe,
        int expectedPackets, int packetBytes, int targetMbps, double allowedDeficit,
        bool allowTwoSenders, Action<string> log, CancellationToken cancellationToken,
        bool qualifyInLongerMeasurement = false)
    {
        var errors = new List<string>();
        var allProbesComplete = true;
        var bestLanes = 0;
        var bestSpeed = 0.0;
        foreach (var lanes in allowTwoSenders ? new[] { 1, 2 } : new[] { 1 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await probe(lanes, cancellationToken);
                var txMbps = result.ElapsedMs > 0
                    ? result.SentPackets * RunDataTestStep.CalculateWireSizeBytes(packetBytes) * 8.0 / result.ElapsedMs / 1000.0 : 0;
                var deficit = RunDataTestStep.CalculateTxDeficitPercent(targetMbps, txMbps);
                allProbesComplete &= result.SentPackets == expectedPackets && result.ElapsedMs > 0;
                if (result.SentPackets == expectedPackets && txMbps > bestSpeed)
                {
                    bestLanes = lanes;
                    bestSpeed = txMbps;
                }
                var countAccepted = result.SentPackets == expectedPackets ||
                    result.Engine == "SharpPcapSendPacket" && result.SentPackets > 0 && result.SentPackets < expectedPackets;
                if (countAccepted && deficit <= allowedDeficit)
                {
                    log($"Прогрев: отправителей {lanes}, TX {result.SentPackets}/{expectedPackets}, {txMbps:F3} Мбит/с, {result.Engine}.");
                    return lanes;
                }
                errors.Add($"отправителей {lanes}: TX {result.SentPackets}/{expectedPackets} за {result.ElapsedMs} мс, " +
                    $"{txMbps:F3} Мбит/с, дефицит {deficit:F3}%, {result.Engine}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                allProbesComplete = false;
                errors.Add($"отправителей {lanes}: {ex.Message}");
            }
            log("Прогрев не подтверждён: " + errors[^1]);
        }
        if (qualifyInLongerMeasurement && allProbesComplete && bestLanes > 0)
        {
            // A short warmup selects lanes; it cannot reliably predict a longer
            // sample on a busy host. The caller must still enforce the same TX,
            // capture and loss limits on that mandatory measurement.
            log($"Короткий прогрев: все кадры отправлены без ошибок; выбрано отправителей {bestLanes} " +
                $"({bestSpeed:F3} Мбит/с). Скорость не подтверждена; обязательная проверка скорости и потерь — в основном замере.");
            return bestLanes;
        }
        throw new InvalidOperationException("Генератор не подтвердил заданную скорость на прогреве. Основное измерение не началось. " +
            "Это дефицит скорости отправителя, а не измеренная потеря пакетов устройства. " + string.Join("; ", errors));
    }
}
