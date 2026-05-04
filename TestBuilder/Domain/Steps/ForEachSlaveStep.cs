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
            ILogger logger)
        {
            _fromSlaveId = fromSlaveId;
            _toSlaveId = toSlaveId;
            _step = step == 0 ? (byte)1 : step;
            _stopOnError = stopOnError;
            _bodyGraph = bodyGraph;
            _logger = logger;
        }

        public async Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            if (_fromSlaveId > _toSlaveId)
            {
                _logger.Warning(
                    $"[ОШИБКА] Цикл For → некорректный диапазон. С {_fromSlaveId} по {_toSlaveId}.");

                return StepResult.False;
            }

            _logger.Info(
                $"[ШАГ] Цикл For → устройства с {_fromSlaveId} по {_toSlaveId}, шаг {_step}.");

            var executor = new TestExecutor();

            for (var slaveId = _fromSlaveId; slaveId <= _toSlaveId; slaveId += _step)
            {
                cancellationToken.ThrowIfCancellationRequested();

                context.CurrentSlaveId = (byte)slaveId;
                context.SetVariable("slaveId", (byte)slaveId);

                _logger.Info($"[ШАГ] Итерация: устройство {slaveId}.");

                var result = await executor.ExecuteAsync(
                    _bodyGraph.StartNode,
                    context,
                    cancellationToken);

                if (result != ExecutionStatus.Completed)
                {
                    _logger.Warning(
                        $"[ОШИБКА] Итерация устройство {slaveId} — ошибка. Стоп при ошибке: {(_stopOnError ? "да" : "нет")}.");

                    if (_stopOnError)
                    {
                        context.CurrentSlaveId = null;
                        return StepResult.False;
                    }
                }
            }

            context.CurrentSlaveId = null;

            _logger.Info(
                $"[OK] Цикл For завершён. Диапазон {_fromSlaveId}..{_toSlaveId}.");

            return StepResult.Next;
        }
    }
}