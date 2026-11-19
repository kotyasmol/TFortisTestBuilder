using System;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    /// <summary>
    /// Выполняет вложенный граф для каждого slaveId из указанного диапазона.
    /// </summary>
    public sealed class ForEachSlaveStep : ITestStep
    {
        private readonly bool _repeat;
        private readonly int _repeatCount;
        private readonly string _indexVariable;
        private readonly bool _untilSuccess;
        private readonly int _repeatDelayMs;
        private readonly byte _fromSlaveId;
        private readonly byte _toSlaveId;
        private readonly byte _step;
        private readonly bool _stopOnError;
        private readonly CompiledGraph _bodyGraph;
        private readonly ILogger _logger;

        public ForEachSlaveStep(
            byte fromSlaveId,
            byte toSlaveId,
            byte step,
            bool stopOnError,
            CompiledGraph bodyGraph,
            ILogger logger, bool repeatOnSameSlave = false, int repeatCount = 1,
            string indexVariable = "iteration", bool untilSuccess = false, int repeatDelayMs = 0)
        {
            _fromSlaveId = fromSlaveId;
            _toSlaveId = toSlaveId;
            _step = step == 0 ? (byte)1 : step;
            _stopOnError = stopOnError;
            _bodyGraph = bodyGraph;
            _logger = logger;
            _repeat = repeatOnSameSlave;
            _repeatCount = repeatCount;
            _indexVariable = indexVariable;
            _untilSuccess = untilSuccess;
            _repeatDelayMs = repeatDelayMs;
        }

        public async Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            if (_repeat) return await new GraphRepeatStep(_bodyGraph, _logger, _repeatCount, _repeatDelayMs,
                _untilSuccess, _stopOnError, _indexVariable, "Цикл For").ExecuteAsync(context, cancellationToken);
            var stopOnError = _stopOnError;
            if (_fromSlaveId > _toSlaveId)
            {
                _logger.Warning(
                    $"[ОШИБКА] Цикл For → некорректный диапазон. С {_fromSlaveId} по {_toSlaveId}.");

                context.AddReportEntry("Цикл For", false, $"Некорректный диапазон {_fromSlaveId}..{_toSlaveId}.");
                return stopOnError ? StepResult.False : StepResult.Next;
            }

            _logger.Info(
                $"[ШАГ] Цикл For → устройства с {_fromSlaveId} по {_toSlaveId}, шаг {_step}.");

            var executor = new TestExecutor();
            var previousSlave = context.CurrentSlaveId;
            var hadPreviousVariable = context.Variables.TryGetValue("slaveId", out var previousVariable);
            try
            {
                for (var slaveId = (int)_fromSlaveId; slaveId <= _toSlaveId; slaveId += _step)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    context.CurrentSlaveId = (byte)slaveId;
                    context.SetVariable("slaveId", (byte)slaveId);

                    _logger.Info($"[ШАГ] Итерация: устройство {slaveId}.");

                    var failureState = ExecutionFailureState.Capture(context);
                    var result = await GraphRepeatStep.ExecuteIterationAsync(executor, _bodyGraph, context, cancellationToken,
                        $"Цикл For: устройство {slaveId}", !stopOnError, _logger);

                    if (result != ExecutionStatus.Completed)
                    {
                        _logger.Warning(
                            $"[ОШИБКА] Итерация устройство {slaveId} — ошибка. Стоп при ошибке: {(stopOnError ? "да" : "нет")}.",
                            (context.CriticalFailure ?? context.Failure)?.Code ?? TestErrorCode.Loop);

                        if (stopOnError)
                            return StepResult.False;
                        failureState.Restore(context);
                    }
                }
            }
            finally
            {
                context.CurrentSlaveId = previousSlave;
                if (hadPreviousVariable) context.Variables["slaveId"] = previousVariable!;
                else context.Variables.Remove("slaveId");
            }

            _logger.Info(
                $"[OK] Цикл For завершён. Диапазон {_fromSlaveId}..{_toSlaveId}.");

            return StepResult.Next;
        }
    }
}
