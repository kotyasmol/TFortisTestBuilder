using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Domain.Steps;

internal static class DataTestPacketPacing
{
    // A high-resolution timer may still wake too late for a 123-us packet slot
    // on some Windows systems. Qualify it at the requested rate before using it.
    // No batching/bursts, no reduced target, and no extra measured-attempt budget.
    internal static async Task<DataTestPacingSelection> SelectAsync(
        Func<int, bool, CancellationToken, Task<PacedSendResult>> probe,
        bool tryTimer, int expectedPackets, int frameBytes, int targetMbps, double allowedDeficit,
        bool allowTwoSenders, Action<string> log, CancellationToken token, bool qualifyInLongerMeasurement = false)
    {
        async Task<DataTestPacingSelection> Select(bool timed)
        {
            var timerModes = new Dictionary<int, bool>();
            var lanes = await DataTestSenderCalibration.SelectAsync(async (count, cancellation) =>
            {
                var result = await probe(count, timed, cancellation);
                timerModes[count] = result.UsesPacketTimer;
                return result;
            }, expectedPackets, frameBytes, targetMbps, allowedDeficit, allowTwoSenders, log, token,
                qualifyInLongerMeasurement);
            return new DataTestPacingSelection(lanes, timerModes[lanes]);
        }

        if (tryTimer)
        {
            log("Прогрев: проверка покадрового ожидания с таймером.");
            try { return await Select(true); }
            catch (InvalidOperationException ex)
            {
                log("Ожидание с таймером не подтверждено: " + ex.Message);
                log("Проверяется покадровое ожидание QPC с той же нагрузкой.");
            }
        }
        return await Select(false);
    }

    internal static bool NeedsActiveRetry(DataTestPortResult result, double allowedDeficit) =>
        !result.Passed && result.UsesPacketTimer && result.TxDeficitPercent > allowedDeficit;
}

internal readonly record struct DataTestPacingSelection(int SenderCount, bool UsesPacketTimer);
