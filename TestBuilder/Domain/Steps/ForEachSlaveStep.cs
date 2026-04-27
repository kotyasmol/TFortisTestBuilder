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
                    $"ЦИКЛ ПО СЛЕЙВАМ: некорректный диапазон. Начало={_fromSlaveId}, конец={_toSlaveId}.");

                return StepResult.False;
            }

            _logger.Info(
                $"ЦИКЛ ПО СЛЕЙВАМ: запуск. Диапазон={_fromSlaveId}..{_toSlaveId}, шаг={_step}.");

            var executor = new TestExecutor();

            for (var slaveId = _fromSlaveId; slaveId <= _toSlaveId; slaveId += _step)
            {
                cancellationToken.ThrowIfCancellationRequested();

                context.CurrentSlaveId = (byte)slaveId;
                context.SetVariable("slaveId", (byte)slaveId);

                _logger.Info($"ЦИКЛ ПО СЛЕЙВАМ: начало итерации. Текущий slave={slaveId}.");

                var result = await executor.ExecuteAsync(
                    _bodyGraph.StartNode,
                    context,
                    cancellationToken);

                if (result != ExecutionStatus.Completed)
                {
                    _logger.Warning(
                        $"ЦИКЛ ПО СЛЕЙВАМ: тело цикла завершилось с ошибкой. Slave={slaveId}, результат={result}.");

                    if (_stopOnError)
                    {
                        context.CurrentSlaveId = null;
                        return StepResult.False;
                    }
                }
            }

            context.CurrentSlaveId = null;

            _logger.Info(
                $"ЦИКЛ ПО СЛЕЙВАМ: успешно завершен диапазон {_fromSlaveId}..{_toSlaveId}.");

            return StepResult.Next;
        }
    }
}
