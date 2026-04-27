using System;
using System.Collections.Generic;
using System.Linq;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
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
    public sealed class GraphCompiler
    {
        private readonly IModbusService _modbusService;
        private readonly ILogger _logger;

        public GraphCompiler(IModbusService modbusService, ILogger logger)
        {
            _modbusService = modbusService;
            _logger = logger;
        }

        public CompiledGraph Compile(GraphWorkspaceViewModel graph)
        {
            var map = new Dictionary<NodeViewModel, TestNode>();

            foreach (var node in graph.Nodes)
            {
                map[node] = new TestNode(CreateStep(node), node);
            }

            foreach (var connection in graph.Connections)
            {
                var sourceVm = connection.Source.Parent;
                var targetVm = connection.Target.Parent;

                if (sourceVm == null || targetVm == null)
                    continue;

                if (!map.TryGetValue(sourceVm, out var source))
                    continue;

                if (!map.TryGetValue(targetVm, out var target))
                    continue;

                BindTransition(sourceVm, connection.Source, source, target);
            }

            NodeViewModel? startVm = graph.Nodes.OfType<BodyStartNodeViewModel>().FirstOrDefault();
            startVm ??= graph.Nodes.OfType<StartNodeViewModel>().FirstOrDefault();

            if (startVm == null)
                throw new InvalidOperationException($"В графе '{graph.Title}' отсутствует стартовая нода.");

            return new CompiledGraph(map[startVm]);
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
                CheckRegisterRangeNodeViewModel check => check.CreateStep(_logger),
                ForEachSlaveNodeViewModel forEachSlave => CreateForEachSlaveStep(forEachSlave),
                _ => new PassThroughStep()
            };
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

        private static void BindTransition(
            NodeViewModel sourceVm,
            ConnectorViewModel sourceConnector,
            TestNode source,
            TestNode target)
        {
            switch (sourceVm)
            {
                case ModbusWriteNodeViewModel writeVm:
                    if (sourceConnector == writeVm.TrueOut)
                        source.OnTrue = target;
                    else if (sourceConnector == writeVm.FalseOut)
                        source.OnFalse = target;
                    break;

                case CheckRegisterRangeNodeViewModel checkVm:
                    if (sourceConnector == checkVm.TrueOut)
                        source.OnTrue = target;
                    else if (sourceConnector == checkVm.FalseOut)
                        source.OnFalse = target;
                    break;

                case ForEachSlaveNodeViewModel forVm:
                    if (sourceConnector == forVm.SuccessOut)
                        source.Next = target;
                    else if (sourceConnector == forVm.ErrorOut)
                        source.OnFalse = target;
                    break;

                default:
                    source.Next = target;
                    break;
            }
        }
    }
}
