using System;
using System.Collections.Generic;
using System.Linq;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
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
        private bool _ownsClients;
        private readonly HashSet<GraphWorkspaceViewModel> _compiling = new();

        public GraphCompiler(IModbusService modbusService, ILogger logger)
            : this(modbusService, new HttpRequestService(), logger)
        {
            _serialNumberRequestService = LauncherServerClient.Create(App.StartupSessionId, AppSettings.Instance.ServerBaseUrl);
            _ownsClients = true;
        }

        public GraphCompiler(
            IModbusService modbusService,
            IHttpRequestService httpRequestService,
            ILogger logger)
        {
            _modbusService = modbusService;
            _httpRequestService = httpRequestService;
            _serialNumberRequestService = httpRequestService;
            _logger = logger;
        }

        public void Dispose()
        {
            if (!_ownsClients) return;
            (_httpRequestService as IDisposable)?.Dispose();
            (_serialNumberRequestService as IDisposable)?.Dispose();
            _ownsClients = false;
        }

        public CompiledGraph Compile(GraphWorkspaceViewModel graph)
        {
            if (!_compiling.Add(graph))
                throw new InvalidOperationException($"Граф '{graph.Title}' содержит рекурсивное вложение.");
            try { return CompileCore(graph); }
            finally { _compiling.Remove(graph); }
        }

        private CompiledGraph CompileCore(GraphWorkspaceViewModel graph)
        {
            var starts = graph.Nodes.Where(n => n is StartNodeViewModel or BodyStartNodeViewModel).ToList();
            if (starts.Count != 1)
                throw new InvalidOperationException($"В графе '{graph.Title}' должна быть ровно одна стартовая нода (найдено {starts.Count}).");

            var map = graph.Nodes.ToDictionary(node => node, node => new TestNode(CreateStep(node), node));
            var transitions = map.Values.ToDictionary(node => node,
                _ => new Dictionary<StepResult, List<TestNode>>());
            foreach (var connection in graph.Connections)
            {
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
            return new CompiledGraph(map[starts[0]]);
        }

        private ITestStep CreateStep(NodeViewModel node)
        {
            return node switch
            {
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
                PrintLabelNodeViewModel printLabel => printLabel.CreateStep(_logger),
                SendTestReportNodeViewModel report => report.CreateStep(_logger),
                SubtestNodeViewModel subtest => CreateSubtestStep(subtest),
                ForEachSlaveNodeViewModel forEachSlave => CreateForEachSlaveStep(forEachSlave),
                _ => new PassThroughStep()
            };
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
                _logger);
        }

        private static StepResult ResolveTransition(NodeViewModel node, ConnectorViewModel connector)
        {
            if (node is ForEachSlaveNodeViewModel loop)
            {
                if (connector == loop.SuccessOut) return StepResult.Next;
                if (connector == loop.ErrorOut) return StepResult.False;
                throw new InvalidOperationException($"Неизвестный выход ноды '{node.Title}'.");
            }
            (ConnectorViewModel? Success, ConnectorViewModel? Error) ports = node switch
            {
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
            throw new InvalidOperationException($"Неизвестный выход '{connector.Title}' ноды '{node.Title}'.");
        }
    }
}
