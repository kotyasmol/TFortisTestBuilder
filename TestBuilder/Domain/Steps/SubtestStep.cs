using System;
using System.Linq;
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
        private readonly bool _runOnFailure;

        public SubtestStep(
            string name,
            bool isEnabled,
            bool stopOnError,
            CompiledGraph bodyGraph,
            ILogger logger, bool runOnFailure = false)
        {
            _name = string.IsNullOrWhiteSpace(name) ? "Подтест" : name;
            _isEnabled = isEnabled;
            _stopOnError = stopOnError;
            _bodyGraph = bodyGraph;
            _logger = logger;
            _runOnFailure = runOnFailure;
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

            var previousCleanup = context.IsCleanup;
            var failureState = ExecutionFailureState.Capture(context);
            var reportStart = context.ReportEntries.Count;
            context.IsCleanup |= _runOnFailure;
            try
            {
                status = await executor.ExecuteAsync(
                    _bodyGraph.StartNode,
                    context,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                context.AddReportEntry(_name, false, "Отменён до завершения.");
                throw;
            }
            catch (Exception ex) when (!ExecutionCleanupException.Contains(ex))
            {
                context.HasExecutionException |= _stopOnError;
                var message = $"[ОШИБКА] Подтест '{_name}' завершился ошибкой: {ex.Message}";
                _logger.Warning(message);
                context.AddReportEntry(_name, false, ex.Message);
                if (_runOnFailure) context.SetVariable("Cleanup.Succeeded", false);

                if (_stopOnError)
                    context.HasCriticalError = true;
                else failureState.Restore(context);
                return _stopOnError ? StepResult.False : StepResult.True;
            }
            finally { context.IsCleanup = previousCleanup; }

            if (status == ExecutionStatus.Completed)
            {
                if (_runOnFailure && context.ReportEntries.Skip(reportStart).Any(entry => !entry.IsSuccess))
                    context.SetVariable("Cleanup.Succeeded", false);
                _logger.Info($"[OK] Подтест '{_name}' завершён.");
                context.AddReportEntry(_name, true, "true");
                return StepResult.True;
            }

            var failureMessage = $"[ОШИБКА] Подтест '{_name}' завершился с результатом {status}.";
            _logger.Warning(failureMessage);
            context.AddReportEntry(_name, false, status.ToString());
            if (_runOnFailure) context.SetVariable("Cleanup.Succeeded", false);

            if (_stopOnError)
                context.HasCriticalError = true;
            else failureState.Restore(context);
            return _stopOnError ? StepResult.False : StepResult.True;
        }
    }
}
