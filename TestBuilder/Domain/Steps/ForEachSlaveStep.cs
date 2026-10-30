using System;
using System.Linq;
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
            if (_repeat) return await RepeatAsync(context, cancellationToken);
            if (_fromSlaveId > _toSlaveId)
            {
                _logger.Warning(
                    $"[ОШИБКА] Цикл For → некорректный диапазон. С {_fromSlaveId} по {_toSlaveId}.");

                context.AddReportEntry("Цикл For", false, $"Некорректный диапазон {_fromSlaveId}..{_toSlaveId}.");
                return _stopOnError ? StepResult.False : StepResult.Next;
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
                    var result = await ExecuteIterationAsync(executor, context, cancellationToken,
                        $"Цикл For: устройство {slaveId}", tolerateException: !_stopOnError);

                    if (result != ExecutionStatus.Completed)
                    {
                        _logger.Warning(
                            $"[ОШИБКА] Итерация устройство {slaveId} — ошибка. Стоп при ошибке: {(_stopOnError ? "да" : "нет")}.");

                        if (_stopOnError)
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
        private async Task<StepResult> RepeatAsync(TestContext context, CancellationToken token)
        {
            if (_repeatCount is < 1 or > 100000 || _repeatDelayMs is < 0 or > 3600000 ||
                string.IsNullOrWhiteSpace(_indexVariable) || _indexVariable == "slaveId")
            {
                _logger.Warning("[ОШИБКА] For: число повторов 1..100000, задержка 0..3600000 мс; нужна отдельная переменная счётчика.");
                context.AddReportEntry("Цикл For", false, "Некорректные параметры повторов.");
                return _stopOnError ? StepResult.False : StepResult.Next;
            }
            var previousSlave = context.CurrentSlaveId;
            var hadIndex = context.Variables.TryGetValue(_indexVariable, out var previousIndex);
            var initialCritical = context.HasCriticalError;
            var failureState = ExecutionFailureState.Capture(context);
            var reportStart = context.ReportEntries.Count;
            var anyFailure = false;
            var executor = new TestExecutor();
            try
            {
                for (var i = 1; i <= _repeatCount; i++)
                {
                    token.ThrowIfCancellationRequested();
                    await context.WaitWhilePausedAsync(token);
                    if (i > 1 && _repeatDelayMs > 0) await Task.Delay(_repeatDelayMs, token);
                    context.CurrentSlaveId = previousSlave;
                    context.SetVariable(_indexVariable, i);
                    _logger.Info($"[ШАГ] For: повтор {i}/{_repeatCount}, slave не меняется.");
                    var iterationReportStart = context.ReportEntries.Count;
                    var result = await ExecuteIterationAsync(executor, context, token,
                        $"Цикл For: повтор {i}", tolerateException: !_stopOnError || _untilSuccess);
                    token.ThrowIfCancellationRequested();
                    var success = result == ExecutionStatus.Completed && context.HasCriticalError == initialCritical &&
                        (!_untilSuccess || !context.ReportEntries.Skip(iterationReportStart).Any(entry => !entry.IsSuccess));
                    if (success && _untilSuccess) return StepResult.Next;
                    if (!success)
                    {
                        anyFailure = true;
                        if (!_untilSuccess && _stopOnError) return StepResult.False;
                        if (_untilSuccess && i < _repeatCount)
                        {
                            // Failed attempts are diagnostics, not final product failures.
                            failureState.Restore(context);
                            context.ReportEntries.RemoveRange(reportStart, context.ReportEntries.Count - reportStart);
                        }
                        else if (!_stopOnError) failureState.Restore(context);
                    }
                }
                return anyFailure && _stopOnError ? StepResult.False : StepResult.Next;
            }
            finally
            {
                context.CurrentSlaveId = previousSlave;
                if (hadIndex) context.Variables[_indexVariable] = previousIndex!;
                else context.Variables.Remove(_indexVariable);
            }
        }

        private async Task<ExecutionStatus> ExecuteIterationAsync(TestExecutor executor, TestContext context,
            CancellationToken token, string name, bool tolerateException)
        {
            var reportStart = context.ReportEntries.Count;
            ExecutionStatus result;
            try { result = await executor.ExecuteAsync(_bodyGraph.StartNode, context, token); }
            catch (Exception error) when (tolerateException && !token.IsCancellationRequested && !ExecutionCleanupException.Contains(error))
            {
                _logger.Warning($"[ОШИБКА] {name}: {error.Message}.");
                context.AddReportEntry(name, false, error.Message);
                result = ExecutionStatus.Failed;
            }
            token.ThrowIfCancellationRequested();
            if (result != ExecutionStatus.Completed && !context.ReportEntries.Skip(reportStart).Any(entry => !entry.IsSuccess))
                context.AddReportEntry(name, false, context.Failure?.Summary ?? "Проверка не пройдена.");
            return result;
        }
    }
}
