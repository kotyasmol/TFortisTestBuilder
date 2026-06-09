using System;
using System.Collections.Generic;
using System.Linq;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Http;
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
        private readonly IHttpRequestService _httpRequestService;
        private readonly ILogger _logger;

        public GraphCompiler(IModbusService modbusService, ILogger logger)
            : this(modbusService, new HttpRequestService(), logger)
        {
        }

        public GraphCompiler(
            IModbusService modbusService,
            IHttpRequestService httpRequestService,
            ILogger logger)
        {
            _modbusService = modbusService;
            _httpRequestService = httpRequestService;
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
                {
                    continue;
                }

                if (!map.TryGetValue(sourceVm, out var source))
                {
                    continue;
                }

                if (!map.TryGetValue(targetVm, out var target))
                {
                    continue;
                }

                BindTransition(sourceVm, connection.Source, source, target);
            }

            NodeViewModel? startVm = graph.Nodes.OfType<BodyStartNodeViewModel>().FirstOrDefault();
            startVm ??= graph.Nodes.OfType<StartNodeViewModel>().FirstOrDefault();

            if (startVm == null)
            {
                throw new InvalidOperationException($"В графе '{graph.Title}' отсутствует стартовая нода.");
            }

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
                CheckRegisterEqualityNodeViewModel equality => equality.CreateStep(_logger),
                WaitUntilNodeViewModel waitUntil => waitUntil.CreateStep(_logger),
                PollRegisterNodeViewModel pollRegister => pollRegister.CreateStep(_logger),
                OperatorActionNodeViewModel operatorAction => operatorAction.CreateStep(_logger),
                SelfTestCheckNodeViewModel selfTest => selfTest.CreateStep(_httpRequestService, _logger),
                CheckVariableEqualityNodeViewModel variableEquality => variableEquality.CreateStep(_logger),
                CheckVariableRangeNodeViewModel variableRange => variableRange.CreateStep(_logger),
                ClearArpCacheNodeViewModel clearArp => clearArp.CreateStep(_logger),
                GetSerialNumberFromServerNodeViewModel serial => serial.CreateStep(_httpRequestService, _logger),
                SendUdpSetMacPacketNodeViewModel setMac => setMac.CreateStep(_logger),
                RunDataTestNodeViewModel dataTest => dataTest.CreateStep(_logger),
                GetUpsStatusNodeViewModel upsStatus => upsStatus.CreateStep(_httpRequestService, _logger),
                GetUpsVoltageNodeViewModel upsVoltage => upsVoltage.CreateStep(_httpRequestService, _logger),
                PrintLabelNodeViewModel printLabel => printLabel.CreateStep(_logger),
                SendTestReportNodeViewModel report => report.CreateStep(_logger),
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
                    {
                        source.OnTrue = target;
                    }
                    else if (sourceConnector == writeVm.FalseOut)
                    {
                        source.OnFalse = target;
                    }

                    break;

                case CheckRegisterRangeNodeViewModel checkVm:
                    if (sourceConnector == checkVm.TrueOut)
                    {
                        source.OnTrue = target;
                    }
                    else if (sourceConnector == checkVm.FalseOut)
                    {
                        source.OnFalse = target;
                    }

                    break;

                case CheckRegisterEqualityNodeViewModel equalityVm:
                    if (sourceConnector == equalityVm.TrueOut)
                    {
                        source.OnTrue = target;
                    }
                    else if (sourceConnector == equalityVm.FalseOut)
                    {
                        source.OnFalse = target;
                    }

                    break;

                case WaitUntilNodeViewModel waitVm:
                    if (sourceConnector == waitVm.TrueOut)
                        source.OnTrue = target;
                    else if (sourceConnector == waitVm.FalseOut)
                        source.OnFalse = target;
                    break;

                case PollRegisterNodeViewModel pollVm:
                    if (sourceConnector == pollVm.TrueOut)
                        source.OnTrue = target;
                    else if (sourceConnector == pollVm.FalseOut)
                        source.OnFalse = target;
                    break;

                case OperatorActionNodeViewModel operatorVm:
                    if (sourceConnector == operatorVm.TrueOut)
                    {
                        source.OnTrue = target;
                    }
                    else if (sourceConnector == operatorVm.FalseOut)
                    {
                        source.OnFalse = target;
                    }

                    break;

                case SelfTestCheckNodeViewModel selfTestVm:
                    BindTrueFalse(sourceConnector, source, target, selfTestVm.TrueOut, selfTestVm.FalseOut);
                    break;

                case CheckVariableEqualityNodeViewModel variableEqualityVm:
                    if (sourceConnector == variableEqualityVm.TrueOut)
                    {
                        source.OnTrue = target;
                    }
                    else if (sourceConnector == variableEqualityVm.FalseOut)
                    {
                        source.OnFalse = target;
                    }

                    break;

                case CheckVariableRangeNodeViewModel variableRangeVm:
                    if (sourceConnector == variableRangeVm.TrueOut)
                    {
                        source.OnTrue = target;
                    }
                    else if (sourceConnector == variableRangeVm.FalseOut)
                    {
                        source.OnFalse = target;
                    }

                    break;

                case ClearArpCacheNodeViewModel clearArpVm:
                    BindTrueFalse(sourceConnector, source, target, clearArpVm.TrueOut, clearArpVm.FalseOut);
                    break;

                case GetSerialNumberFromServerNodeViewModel serialVm:
                    BindTrueFalse(sourceConnector, source, target, serialVm.TrueOut, serialVm.FalseOut);
                    break;

                case SendUdpSetMacPacketNodeViewModel setMacVm:
                    BindTrueFalse(sourceConnector, source, target, setMacVm.TrueOut, setMacVm.FalseOut);
                    break;

                case RunDataTestNodeViewModel dataTestVm:
                    BindTrueFalse(sourceConnector, source, target, dataTestVm.TrueOut, dataTestVm.FalseOut);
                    break;

                case GetUpsStatusNodeViewModel upsStatusVm:
                    BindTrueFalse(sourceConnector, source, target, upsStatusVm.TrueOut, upsStatusVm.FalseOut);
                    break;

                case GetUpsVoltageNodeViewModel upsVoltageVm:
                    BindTrueFalse(sourceConnector, source, target, upsVoltageVm.TrueOut, upsVoltageVm.FalseOut);
                    break;

                case PrintLabelNodeViewModel printLabelVm:
                    BindTrueFalse(sourceConnector, source, target, printLabelVm.TrueOut, printLabelVm.FalseOut);
                    break;

                case SendTestReportNodeViewModel reportVm:
                    BindTrueFalse(sourceConnector, source, target, reportVm.TrueOut, reportVm.FalseOut);
                    break;

                case ForEachSlaveNodeViewModel forVm:
                    if (sourceConnector == forVm.SuccessOut)
                    {
                        source.Next = target;
                    }
                    else if (sourceConnector == forVm.ErrorOut)
                    {
                        source.OnFalse = target;
                    }

                    break;

                default:
                    source.Next = target;
                    break;
            }
        }

        private static void BindTrueFalse(
            ConnectorViewModel sourceConnector,
            TestNode source,
            TestNode target,
            ConnectorViewModel trueOut,
            ConnectorViewModel falseOut)
        {
            if (sourceConnector == trueOut)
            {
                source.OnTrue = target;
            }
            else if (sourceConnector == falseOut)
            {
                source.OnFalse = target;
            }
        }
    }
}
