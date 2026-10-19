using System;
using System.Collections.Generic;
using System.Linq;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services.Http;
using TestBuilder.Services.Graph;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Services
{
    /// <summary>
    /// Компилирует визуальный граф Nodify ViewModel в исполняемый граф TestNode.
    /// Поддерживает обычные графы и вложенные графы внутри составных нод.
    /// </summary>
    public sealed class GraphCompiler : IDisposable
    {
        private readonly IModbusService _modbusService;
        private readonly IHttpRequestService _httpRequestService;
        private readonly ILogger _logger;
        private readonly IHttpRequestService _serialNumberRequestService;
        private readonly ISerialConsoleSession _console;
        private readonly bool _ownsConsole;
        private readonly IExternalProgramRunner _programRunner;
        private bool _ownsClients;
        private readonly HashSet<GraphWorkspaceViewModel> _compiling = new();
        private readonly List<string> _graphNames = new();
        public HardwareConfiguration? ProfileHardware { get; init; }
        public string? ConnectedStandPort { get; init; }
        private HardwareConfiguration? _activeHardware;

        public static void ValidateConnections(GraphWorkspaceViewModel graph, HardwareConfiguration? hardware)
        {
            foreach (var node in graph.Nodes)
            {
                if (node is SerialConsoleNodeViewModel console && !string.IsNullOrEmpty(console.DeviceId) &&
                    hardware?.ConsoleDevices.Any(d => d.Id == console.DeviceId) != true)
                    throw new InvalidOperationException($"В графе «{graph.Title}» консоль ссылается на отсутствующее устройство «{console.DeviceId}». Проверьте оборудование профиля.");
                if (node is ICompositeNodeViewModel composite) ValidateConnections(composite.BodyGraph, hardware);
            }
        }

        public static void ValidateRunConnections(GraphWorkspaceViewModel graph, HardwareConfiguration? hardware,
            string? standPort, NodeViewModel? entry = null, bool singleNode = false)
        {
            hardware?.Validate();
            var nodes = entry == null ? graph.Nodes.AsEnumerable() : GetRunNodes(graph, entry, singleNode);
            foreach (var node in nodes)
            {
                if (node is SubtestNodeViewModel { IsEnabled: false }) continue;
                if (node is SerialConsoleNodeViewModel console && !string.IsNullOrEmpty(console.DeviceId))
                {
                    var device = hardware?.ConsoleDevices.SingleOrDefault(d => d.Id == console.DeviceId)
                        ?? throw new InvalidOperationException($"В оборудовании нет консоли «{console.DeviceId}».");
                    device.Validate(requirePort: true);
                    if (!string.IsNullOrWhiteSpace(device.PortName) && string.Equals(device.PortName.Trim(), standPort?.Trim(), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Консоль «{device.Name}» использует порт подключённого стенда. Укажите отдельный порт USB-UART.");
                }
                if (node is ICompositeNodeViewModel composite) ValidateRunConnections(composite.BodyGraph, hardware, standPort);
            }
        }
        // Transport substitutions preserve the real report/label steps in integration tests.
        internal IRawLabelPrinter? LabelPrinter { get; init; }
        internal Func<System.Net.Http.HttpClient>? ReportClientFactory { get; init; }

        public GraphCompiler(IModbusService modbusService, ILogger logger)
            : this(modbusService, new HttpRequestService(), logger)
        {
            _serialNumberRequestService = LauncherServerClient.Create(App.StartupSessionId, AppSettings.Instance.ServerBaseUrl);
            _ownsClients = true;
        }

        public GraphCompiler(
            IModbusService modbusService,
            IHttpRequestService httpRequestService,
            ILogger logger, ISerialConsoleSession? console = null, IExternalProgramRunner? programRunner = null)
        {
            _console = console ?? new SerialConsoleSession(new SystemSerialPortCatalog(),
                id => new[] { ConnectedStandPort, ProfileHardware?.PortName }
                    .Concat(ProfileHardware?.ConsoleDevices.Where(d => d.Id != id).Select(d => d.PortName) ?? Enumerable.Empty<string>())
                    .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
            _ownsConsole = console == null;
            _programRunner = programRunner ?? new ExternalProgramRunner();
            _modbusService = modbusService;
            _httpRequestService = httpRequestService;
            _serialNumberRequestService = httpRequestService;
            _logger = logger is StepDiagnosticLogger ? logger : new StepDiagnosticLogger(logger);
        }

        public void Dispose()
        {
            if (_ownsConsole) (_console as IDisposable)?.Dispose();
            if (!_ownsClients) return;
            (_httpRequestService as IDisposable)?.Dispose();
            (_serialNumberRequestService as IDisposable)?.Dispose();
            _ownsClients = false;
        }

        public CompiledGraph Compile(GraphWorkspaceViewModel graph, NodeViewModel? entry = null, bool singleNode = false)
        {
            if (_compiling.Count == 0) _activeHardware = ProfileHardware ?? graph.Hardware;
            if (!_compiling.Add(graph))
                throw new InvalidOperationException($"Граф '{graph.Title}' содержит рекурсивное вложение.");
            _graphNames.Add(graph.Title);
            try { return CompileCore(graph, entry, singleNode); }
            finally
            {
                _graphNames.RemoveAt(_graphNames.Count - 1);
                _compiling.Remove(graph);
            }
        }

        private CompiledGraph CompileCore(GraphWorkspaceViewModel graph, NodeViewModel? entry, bool singleNode)
        {
            var starts = graph.Nodes.Where(n => n is StartNodeViewModel or BodyStartNodeViewModel).ToList();
            if (entry == null && starts.Count != 1)
                throw new InvalidOperationException($"В графе '{graph.Title}' должна быть ровно одна стартовая нода (найдено {starts.Count}).");

            var nodes = entry == null ? graph.Nodes.ToHashSet() : GetRunNodes(graph, entry, singleNode);
            foreach (var node in nodes.Where(n => n.RunOnlyOnFirstRun && !n.CanRunOnlyOnFirstRun))
                throw new InvalidOperationException($"Нода '{node.DisplayTitle}' не поддерживает выполнение только при первом запуске.");
            var map = nodes.ToDictionary(node => node, node => new TestNode(CreateStep(node), node)
            {
                RunOnlyOnFirstRun = node.RunOnlyOnFirstRun,
                SkippedResult = node.RunOnlyOnFirstRun
                    ? node.Output.Select(port => ResolveTransition(node, port)).First(result => result != StepResult.False)
                    : StepResult.Next,
                DisplayName = $"{string.Join(" / ", _graphNames)} / {node.DisplayTitle}"
            });
            var transitions = map.Values.ToDictionary(node => node,
                _ => new Dictionary<StepResult, List<TestNode>>());
            foreach (var connection in graph.Connections)
            {
                if (entry != null && (singleNode || !nodes.Contains(connection.Source.Parent!))) continue;
                var sourceVm = connection.Source.Parent;
                var targetVm = connection.Target.Parent;
                if (sourceVm == null || targetVm == null || !map.ContainsKey(sourceVm) || !map.ContainsKey(targetVm)
                    || !sourceVm.Output.Contains(connection.Source) || !targetVm.Input.Contains(connection.Target))
                    throw new InvalidOperationException($"Граф '{graph.Title}': связь должна соединять выход и вход нод одного графа.");

                var result = ResolveTransition(sourceVm, connection.Source);
                var outgoing = transitions[map[sourceVm]];
                if (!outgoing.TryGetValue(result, out var targets))
                    outgoing[result] = targets = new List<TestNode>();
                if (targets.Contains(map[targetVm]))
                    throw new InvalidOperationException($"Нода '{sourceVm.Title}': повторная связь выхода '{connection.Source.Title}' с '{targetVm.Title}'.");
                targets.Add(map[targetVm]);
            }

            foreach (var (node, outgoing) in transitions)
            {
                foreach (var (result, targets) in outgoing.Where(pair => pair.Value.Count == 1))
                {
                    switch (result)
                    {
                        case StepResult.Next: node.Next = targets[0]; break;
                        case StepResult.True: node.OnTrue = targets[0]; break;
                        case StepResult.False: node.OnFalse = targets[0]; break;
                    }
                }
            }
            ParallelGraphPlanner.Plan(transitions);
            return new CompiledGraph(map[entry ?? starts[0]]);
        }

        // Traverse only this workspace. Composite steps compile their complete bodies normally.
        internal static HashSet<NodeViewModel> GetRunNodes(GraphWorkspaceViewModel graph, NodeViewModel entry, bool singleNode)
        {
            if (!graph.Nodes.Contains(entry))
                throw new InvalidOperationException("Стартовая нода больше не находится в графе.");
            var nodes = new HashSet<NodeViewModel> { entry };
            if (singleNode) return nodes;
            var pending = new Queue<NodeViewModel>();
            pending.Enqueue(entry);
            while (pending.TryDequeue(out var node))
                foreach (var edge in graph.Connections.Where(c => ReferenceEquals(c.Source.Parent, node)))
                    if (edge.Target.Parent is { } target && graph.Nodes.Contains(target) && nodes.Add(target))
                        pending.Enqueue(target);
            return nodes;
        }

        private ITestStep CreateStep(NodeViewModel node)
        {
            return node switch
            {
                SerialConsoleNodeViewModel opSerialConsole => opSerialConsole.CreateStep(_console, _logger, ResolveConsole(opSerialConsole)),
                RunProgramNodeViewModel opRunProgram => opRunProgram.CreateStep(_programRunner, _logger),
                ParseResponseNodeViewModel opParseResponse => opParseResponse.CreateStep(_logger),
                CheckLedColorNodeViewModel opCheckLedColor => opCheckLedColor.CreateStep(_logger),
                StartNodeViewModel start => start.CreateStep(_logger),
                EndNodeViewModel end => end.CreateStep(_logger),
                BodyStartNodeViewModel => new PassThroughStep(),
                BodyEndNodeViewModel => new BodyEndStep(_logger),
                DelayNodeViewModel delay => delay.CreateStep(_logger),
                LabelNodeViewModel label => label.CreateStep(_logger),
                ModbusWriteNodeViewModel write => write.CreateStep(_modbusService, _logger),
                CheckRegisterRangeNodeViewModel check => check.CreateStep(_modbusService, _logger),
                WaitUntilNodeViewModel waitUntil => waitUntil.CreateStep(_modbusService, _logger),
                OperatorActionNodeViewModel operatorAction => operatorAction.CreateStep(_logger),
                SelfTestCheckNodeViewModel selfTest => selfTest.CreateStep(_httpRequestService, _logger),
                CheckVariableEqualityNodeViewModel variableEquality => variableEquality.CreateStep(_logger),
                CheckVariableRangeNodeViewModel variableRange => variableRange.CreateStep(_logger),
                ClearArpCacheNodeViewModel clearArp => clearArp.CreateStep(_logger),
                GetSerialNumberFromServerNodeViewModel serial => serial.CreateStep(_serialNumberRequestService, _logger),
                SetProMacNodeViewModel setMac => setMac.CreateStep(_logger),
                SetPswMacNodeViewModel pswMac => pswMac.CreateStep(_logger),
                UpdatePswFirmwareNodeViewModel firmware => firmware.CreateStep(_httpRequestService, _logger),
                RunDataTestNodeViewModel dataTest => dataTest.CreateStep(_logger),
                ReadHttpVariableNodeViewModel httpRead => httpRead.CreateStep(_httpRequestService, _logger),
                BuildMacFromSerialNodeViewModel buildMac => buildMac.CreateStep(_logger),
                CompareVariablesNodeViewModel compareVariables => compareVariables.CreateStep(_logger),
                WaitVariableUntilNodeViewModel waitVariable => waitVariable.CreateStep(_httpRequestService, _logger),
                BuildTestReportNodeViewModel buildReport => buildReport.CreateStep(_logger),
                PrintLabelNodeViewModel printLabel => printLabel.CreateStep(_logger, LabelPrinter),
                SendTestReportNodeViewModel report => report.CreateStep(_logger, ReportClientFactory),
                SubtestNodeViewModel subtest => CreateSubtestStep(subtest),
                ForEachSlaveNodeViewModel forEachSlave => CreateForEachSlaveStep(forEachSlave),
                _ => new PassThroughStep()
            };
        }

        private ConsoleDeviceDefinition? ResolveConsole(SerialConsoleNodeViewModel node)
        {
            if (string.IsNullOrEmpty(node.DeviceId)) return null;
            var device = _activeHardware?.ConsoleDevices.SingleOrDefault(d => d.Id == node.DeviceId)
                ?? throw new InvalidOperationException($"В оборудовании профиля нет консоли «{node.DeviceId}».");
            device.Validate();
            return device with { }; // Running steps keep the compiled profile's connection settings.
        }

        private ITestStep CreateSubtestStep(SubtestNodeViewModel node)
        {
            var bodyGraph = Compile(node.BodyGraph);

            return new SubtestStep(
                node.Name,
                node.IsEnabled,
                node.StopOnError,
                bodyGraph,
                _logger);
        }

        private ITestStep CreateForEachSlaveStep(ForEachSlaveNodeViewModel node)
        {
            var bodyGraph = Compile(node.BodyGraph);

            return new ForEachSlaveStep(
                node.FromSlaveId,
                node.ToSlaveId,
                node.Step,
                node.StopOnError,
                bodyGraph,
                _logger, node.RepeatOnSameSlave, node.RepeatCount, node.IndexVariable, node.UntilSuccess, node.RepeatDelayMs);
        }

        private static StepResult ResolveTransition(NodeViewModel node, ConnectorViewModel connector)
        {
            if (node is ForEachSlaveNodeViewModel loop)
            {
                if (connector == loop.SuccessOut) return StepResult.Next;
                if (connector == loop.ErrorOut) return StepResult.False;
                throw new InvalidOperationException($"Неизвестный выход ноды '{node.DisplayTitle}'.");
            }
            (ConnectorViewModel? Success, ConnectorViewModel? Error) ports = node switch
            {
                SerialConsoleNodeViewModel n => (n.TrueOut, n.FalseOut),
                RunProgramNodeViewModel n => (n.TrueOut, n.FalseOut),
                ParseResponseNodeViewModel n => (n.TrueOut, n.FalseOut),
                CheckLedColorNodeViewModel n => (n.TrueOut, n.FalseOut),
                ModbusWriteNodeViewModel n => (n.TrueOut, n.FalseOut),
                CheckRegisterRangeNodeViewModel n => (n.TrueOut, n.FalseOut),
                WaitUntilNodeViewModel n => (n.TrueOut, n.FalseOut),
                OperatorActionNodeViewModel n => (n.TrueOut, n.FalseOut),
                SelfTestCheckNodeViewModel n => (n.TrueOut, n.FalseOut),
                CheckVariableEqualityNodeViewModel n => (n.TrueOut, n.FalseOut),
                CheckVariableRangeNodeViewModel n => (n.TrueOut, n.FalseOut),
                ClearArpCacheNodeViewModel n => (n.TrueOut, n.FalseOut),
                GetSerialNumberFromServerNodeViewModel n => (n.TrueOut, n.FalseOut),
                SetProMacNodeViewModel n => (n.TrueOut, n.FalseOut),
                SetPswMacNodeViewModel n => (n.TrueOut, n.FalseOut),
                UpdatePswFirmwareNodeViewModel n => (n.TrueOut, n.FalseOut),
                RunDataTestNodeViewModel n => (n.TrueOut, n.FalseOut),
                ReadHttpVariableNodeViewModel n => (n.TrueOut, n.FalseOut),
                BuildMacFromSerialNodeViewModel n => (n.TrueOut, n.FalseOut),
                CompareVariablesNodeViewModel n => (n.TrueOut, n.FalseOut),
                WaitVariableUntilNodeViewModel n => (n.TrueOut, n.FalseOut),
                BuildTestReportNodeViewModel n => (n.TrueOut, n.FalseOut),
                PrintLabelNodeViewModel n => (n.TrueOut, n.FalseOut),
                SendTestReportNodeViewModel n => (n.TrueOut, n.FalseOut),
                SubtestNodeViewModel n => (n.SuccessOut, n.ErrorOut),
                _ => (null, null)
            };
            if (connector == ports.Success) return StepResult.True;
            if (connector == ports.Error) return StepResult.False;
            if (ports.Success == null && node.Output.Count == 1 && node.Output[0] == connector)
                return StepResult.Next;
            throw new InvalidOperationException($"Неизвестный выход '{connector.DisplayTitle}' ноды '{node.DisplayTitle}'.");
        }
    }
}
