using System;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public sealed class SubtestStep : ITestStep
    {
        private readonly string _name;
        private readonly bool _isEnabled;
        private readonly bool _stopOnError;
        private readonly CompiledGraph _bodyGraph;
        private readonly ILogger _logger;

        public SubtestStep(
            string name,
            bool isEnabled,
            bool stopOnError,
            CompiledGraph bodyGraph,
            ILogger logger)
        {
            _name = string.IsNullOrWhiteSpace(name) ? "Подтест" : name;
            _isEnabled = isEnabled;
            _stopOnError = stopOnError;
            _bodyGraph = bodyGraph;
            _logger = logger;
        }

        public async Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            if (!_isEnabled)
            {
                _logger.Info($"[OK] Подтест '{_name}' пропущен: выключен.");
                return StepResult.True;
            }

            _logger.Info($"[ШАГ] Подтест '{_name}' начат.");

            var executor = new TestExecutor();
            ExecutionStatus status;

            try
            {
                status = await executor.ExecuteAsync(
                    _bodyGraph.StartNode,
                    context,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var message = $"[ОШИБКА] Подтест '{_name}' завершился ошибкой: {ex.Message}";
                _logger.Warning(message);

                if (_stopOnError)
                    context.HasCriticalError = true;

                return StepResult.False;
            }

            if (status == ExecutionStatus.Completed)
            {
                _logger.Info($"[OK] Подтест '{_name}' завершён.");
                return StepResult.True;
            }

            var failureMessage = $"[ОШИБКА] Подтест '{_name}' завершился с результатом {status}.";
            _logger.Warning(failureMessage);

            if (_stopOnError)
                context.HasCriticalError = true;

            return StepResult.False;
        }
    }
}
